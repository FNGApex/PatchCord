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
    /// </summary>
    public static AppConfig EnsureLoaded()
    {
        Directory.CreateDirectory(BaseDir);
        _cfg = AppConfig.Load(ConfigFile, () => Platform.DiscoverInstalls().ToList());
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
}
