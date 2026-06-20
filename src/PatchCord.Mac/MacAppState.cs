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
            // Mac-only first-run default: Discord palette to match the reference screenshots.
            // The client-mod default is left to Core (vencord) for Windows/macOS parity —
            // Vencord, Equicord, OpenAsar and BetterDiscord are all first-class on macOS.
            // (Vencord/Equicord are no longer parked: Discord ships a universal arm64 binary,
            // so the old "Rosetta" concern doesn't apply; Layer-A bundle writes are gated by
            // the one-time App-Management grant, handled by FdaOnboarding.)
            _cfg.Ui.Theme = "Discord";
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

    // ── Vencord / Equicord dist self-fetch ───────────────────────────────────
    //
    // The official Vencord/Equicord installers normally place <mod>/dist/patcher.js. When an
    // installer can't (e.g. broken Discord detection), PatchCord fetches the dist itself — the
    // same self-sufficiency it already has for betterdiscord.asar and OpenAsar — so the Layer-A
    // stub's require(".../dist/patcher.js") resolves and the monitor can patch.

    /// <summary>
    /// The <c>dist</c> directory for <paramref name="mod"/> ("vencord" | "equicord"), derived from
    /// the platform's patcher path (its parent). Throws for any other mod.
    /// </summary>
    public static string ModDistDir(string mod)
    {
        var patcher = mod switch
        {
            "vencord"  => Platform.VencordPatcherPath,
            "equicord" => Platform.EquicordPatcherPath,
            _ => throw new ArgumentException($"No dist dir for mod '{mod}'.", nameof(mod)),
        };
        return Path.GetDirectoryName(patcher)
            ?? throw new InvalidOperationException($"Could not resolve dist dir for {mod} from '{patcher}'.");
    }

    /// <summary>
    /// Download the desktop dist for <paramref name="mod"/> ("vencord" | "equicord") into its
    /// <see cref="ModDistDir"/>, making <see cref="ModInstalled"/> return true so the monitor patches.
    /// Network-only (no Discord stop/start); safe on a background thread. Throws on failure.
    /// </summary>
    public static void DownloadModDist(string mod)
    {
        var distDir = ModDistDir(mod);
        Log.Write($"Fetching {mod} dist into {distDir}.", "INFO");
        VencordEngine.DownloadDist(mod, distDir);
    }

    // ── Apply a client-mod switch (wipe current unless virgin, then install target) ──
    //
    // A mod switch is an explicit, confirmed, foreground operation — the caller shows a confirm box
    // and PAUSES the monitor around it. It checks what is currently injected (from) vs the target
    // (to): unless the bundle is virgin (nothing injected) it WIPES the current mod first, then
    // installs the target — all inside a single Discord stop→start. Supersedes the older lazy
    // "switch to none removes on the next tick" path.

    /// <summary>
    /// Restore the bundle to vanilla for <paramref name="inst"/>: BD <c>index.js</c> restore (Layer B,
    /// FDA-free) and/or Vencord/Equicord <c>app.asar</c> unpatch (Layer A). No-op on a virgin install.
    /// Assumes Discord is already stopped. Returns the list of mods wiped. Throws on a write failure.
    /// </summary>
    private static List<string> WipeInjected(Install inst, InstallState state, string? resources, string? appDir)
    {
        var wiped = new List<string>();
        if (state.BdActive && appDir != null)
        {
            BetterDiscordEngine.Restore(appDir);
            wiped.Add("BetterDiscord");
        }
        if (state.AsarMod is "vencord" or "equicord" && resources != null)
        {
            PatchEngine.Unpatch(resources);
            wiped.Add(MonitorService.ModShort(state.AsarMod));
        }
        return wiped;
    }

    /// <summary>
    /// Switch <paramref name="inst"/> to <paramref name="toMod"/> ("vencord" | "equicord" |
    /// "betterdiscord" | "none"): fetch the target payload if missing, stop Discord, wipe whatever is
    /// currently injected unless the bundle is already virgin, install the target, restart Discord, and
    /// commit <c>inst.ClientMod</c>. Returns a "From → To" summary. Throws on a write failure — a
    /// Layer-A EPERM is the App-Management gate (caller shows FDA onboarding). Safe on a background
    /// thread; the caller pauses the monitor around it.
    /// <para>
    /// Wiping un-injects so the old mod no longer loads; it leaves the old mod's payload on disk
    /// (<c>betterdiscord.asar</c> / the Vencord dist) — reversible by switching back.
    /// </para>
    /// </summary>
    public static string ApplyModSwitch(Install inst, string toMod)
    {
        var resources = Platform.ResolveResourcesDir(inst);
        var appDir = Platform.ResolveCoreAppDir(inst);
        var state = GetInstallState(inst);
        var fromInjected = state.InjectedMod;   // vencord | equicord | betterdiscord | other | none

        // Fetch the target payload BEFORE stopping Discord (network; no need to be down for it).
        if (toMod is "vencord" or "equicord" && !ModInstalled(toMod))
            DownloadModDist(toMod);
        else if (toMod == "betterdiscord" && !File.Exists(Platform.BetterDiscordAsarPath))
            BetterDiscordEngine.DownloadAsar(Platform.BetterDiscordAsarPath);

        bool wasRunning = Platform.IsRunning(inst);
        if (wasRunning) Platform.Stop(inst);
        try
        {
            // WIPE current mod first — unless this is a virgin (nothing-injected) install.
            WipeInjected(inst, state, resources, appDir);

            // INSTALL target (none → wipe only, nothing to install).
            if (toMod is "vencord" or "equicord" && resources != null)
            {
                var patcher = toMod == "vencord" ? Platform.VencordPatcherPath : Platform.EquicordPatcherPath;
                PatchEngine.Patch(resources, PatchEngine.BuildStubAsar(patcher));
            }
            else if (toMod == "betterdiscord" && appDir != null)
            {
                BetterDiscordEngine.Inject(appDir, Platform.BetterDiscordAsarPath);
            }
        }
        finally
        {
            // Always restart Discord if we stopped it, even when a write threw mid-way.
            if (wasRunning) Platform.Start(inst);
        }

        inst.ClientMod = toMod;
        var summary = $"{MonitorService.ModShort(fromInjected)} → {MonitorService.ModShort(toMod)}";
        Log.Write($"Mod switch applied for {inst.Name}: {summary}.", "OK");
        return summary;
    }

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
