using System.IO;

namespace PatchCord;

/// <summary>
/// Static host for Mac-side app state: platform, config paths, and helpers.
/// Mirrors the role App.xaml.cs plays in the Windows shell.
/// </summary>
internal static class MacAppState
{
    // ── Paths ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Config directory: ~/Library/Application Support/PatchCord/
    /// </summary>
    public static readonly string BaseDir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "PatchCord");

    /// <summary>
    /// Config file: ~/Library/Application Support/PatchCord/config.json
    /// </summary>
    public static readonly string ConfigFile = Path.Combine(BaseDir, "config.json");

    // ── Platform ──────────────────────────────────────────────────────────────

    private static IDiscordPlatform _platform = new MacDiscordPlatform();

    /// <summary>
    /// The macOS IDiscordPlatform implementation.
    /// </summary>
    public static IDiscordPlatform Platform => _platform;

    /// <summary>
    /// Replace the active platform instance.
    /// Called by Program.Main (B3.7) when TryAcquireSingleInstance was called on a
    /// freshly-constructed MacDiscordPlatform — we swap it in so the lock file stream
    /// lives for the process lifetime.
    /// </summary>
    internal static void SetPlatform(IDiscordPlatform platform) => _platform = platform;

    // ── Config ────────────────────────────────────────────────────────────────

    private static AppConfig? _cfg;

    /// <summary>
    /// The loaded AppConfig. Call EnsureLoaded() before accessing.
    /// </summary>
    public static AppConfig Config => _cfg ?? throw new InvalidOperationException("MacAppState not loaded.");

    /// <summary>
    /// Load (or reload) config from ConfigFile. Creates BaseDir if needed.
    /// On first run (no config file yet) defaults the macOS theme to "Discord"
    /// without touching Core's UiConfig.Theme default ("Dark") shared with Windows.
    /// </summary>
    public static AppConfig EnsureLoaded()
    {
        Directory.CreateDirectory(BaseDir);
        bool isFirstRun = !File.Exists(ConfigFile);
        _cfg = AppConfig.Load(ConfigFile, () => Platform.DiscoverInstalls().ToList());
        if (isFirstRun)
        {
            // Mac-only first-run defaults (do NOT touch Core's Windows-shared defaults):
            //  - Discord palette to match the reference screenshots;
            //  - BetterDiscord as the client mod — Vencord/Equicord are parked on macOS
            //    (Rosetta), so BetterDiscord (BandagedBD) is the supported mac mod.
            _cfg.Ui.Theme = "Discord";
            _cfg.ClientMod = "betterdiscord";
            foreach (var inst in _cfg.Installs) inst.ClientMod = "betterdiscord";
            _cfg.Save(ConfigFile);
        }
        return _cfg;
    }

    /// <summary>
    /// Save current config to ConfigFile.
    /// </summary>
    public static void Save() => _cfg?.Save(ConfigFile);

    // ── State helper (mirrors MainWindow.GetInstallState in Windows shell) ───

    /// <summary>
    /// Compute InstallState for one install using the Mac platform.
    /// </summary>
    public static InstallState GetInstallState(Install inst, bool checkOpenAsar = false)
    {
        bool running = Platform.IsRunning(inst);
        string? resourcesDir = Platform.ResolveResourcesDir(inst);
        string? coreAppDir = Platform.ResolveCoreAppDir(inst);
        string? versionLabel = Platform.AppVersionLabel(inst);
        return PatchEngine.GetState(running, resourcesDir, coreAppDir, versionLabel, checkOpenAsar);
    }

    // ── Mod installed check ───────────────────────────────────────────────────

    /// <summary>
    /// True when the mod's data file exists on disk.
    /// </summary>
    public static bool ModInstalled(string mod) => mod switch
    {
        "vencord"       => File.Exists(Platform.VencordPatcherPath),
        "equicord"      => File.Exists(Platform.EquicordPatcherPath),
        "betterdiscord" => File.Exists(Platform.BetterDiscordAsarPath),
        _               => true, // "none" is always "installed"
    };

    // ── BetterDiscord self-heal ("Fix it") ───────────────────────────────────
    //
    // BandagedBD's installer (and older PatchCord) target the app-X.Y.Z layout, but current
    // macOS Discord loads the BARE X.Y.Z/modules/discord_desktop_core/index.js. So BBD commonly
    // leaves BD "malformed": the asar is placed (or not) but the LIVE index.js isn't injected.
    // We detect that and offer a one-click fix that injects the live folder (and downloads the
    // asar if missing), after which the monitor keeps it patched.

    /// <summary>
    /// True when <paramref name="inst"/> wants BetterDiscord but the LIVE core module isn't
    /// correctly injected (live index.js missing the require, or the betterdiscord.asar absent).
    /// Returns false when the mod isn't BetterDiscord or the core dir can't be resolved
    /// (Discord not installed/launched — nothing to fix yet).
    /// </summary>
    public static bool IsBdMalformed(Install inst)
    {
        if (inst.ClientMod != "betterdiscord") return false;
        var appDir = Platform.ResolveCoreAppDir(inst);
        if (appDir == null) return false;
        bool healthy = BetterDiscordEngine.IsInjected(appDir)
                       && File.Exists(Platform.BetterDiscordAsarPath);
        return !healthy;
    }

    /// <summary>
    /// Repair a malformed BetterDiscord install: download the asar if missing, then inject the
    /// LIVE core folder (stopping/restarting Discord around the write, as a patch requires).
    /// Throws if the core dir can't be resolved. Safe to call from a background thread.
    /// </summary>
    public static void FixBetterDiscord(Install inst)
    {
        var appDir = Platform.ResolveCoreAppDir(inst)
            ?? throw new DirectoryNotFoundException(
                $"Discord's core module folder for {inst.Name} was not found — launch Discord once first.");
        var asar = Platform.BetterDiscordAsarPath;

        if (!File.Exists(asar))
        {
            Log.Write($"Fix BetterDiscord: asar missing, downloading for {inst.Name}.", "INFO");
            BetterDiscordEngine.DownloadAsar(asar);
        }

        bool wasRunning = Platform.IsRunning(inst);
        if (wasRunning) Platform.Stop(inst);
        BetterDiscordEngine.Inject(appDir, asar);
        Log.Write($"Fix BetterDiscord: injected live core {appDir} for {inst.Name}.", "OK");
        if (wasRunning) Platform.Start(inst);
    }
}
