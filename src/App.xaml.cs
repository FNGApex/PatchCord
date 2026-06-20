using System.IO;
using System.Windows;

namespace PatchCord;

public partial class App : System.Windows.Application
{
    // Folder the exe lives in; config and log sit beside it.
    public static string BaseDir { get; private set; } = AppContext.BaseDirectory;
    public static string ConfigFile { get; private set; } = "config.json";

    // The active platform implementation — available after OnStartup.
    public static IDiscordPlatform Platform { get; private set; } = null!;

    public static string PatcherPathFor(string mod) =>
        mod == "equicord" ? Platform.EquicordPatcherPath : Platform.VencordPatcherPath;

    public static bool ModInstalled(string mod) => mod switch
    {
        "vencord" => System.IO.File.Exists(Platform.VencordPatcherPath),
        "equicord" => System.IO.File.Exists(Platform.EquicordPatcherPath),
        "betterdiscord" => System.IO.File.Exists(Platform.BetterDiscordAsarPath),
        "bandagedbd" => BandagedBDEngine.HasAnySnapshot(), // re-appliable once we've snapshotted it
        _ => true, // "none"
    };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --dumpstub <outFile> <patcherPath>: writes the stub asar and exits (for byte-diffing)
        if (e.Args.Length == 3 && e.Args[0] == "--dumpstub")
        {
            File.WriteAllBytes(e.Args[1], PatchEngine.BuildStubAsar(e.Args[2]));
            Shutdown();
            return;
        }

        // Set up paths relative to the exe.
        BaseDir = AppContext.BaseDirectory;
        Log.FilePath = Path.Combine(BaseDir, "patchcord.log");
        ConfigFile = Path.Combine(BaseDir, "config.json");
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var vencordBase = Environment.GetEnvironmentVariable("VENCORD_USER_DATA_DIR")
            ?? Path.Combine(appData, "Vencord");
        var equicordBase = Environment.GetEnvironmentVariable("EQUICORD_USER_DATA_DIR")
            ?? Path.Combine(appData, "Equicord");
        var vencordPatcherPath = Path.Combine(vencordBase, "dist", "patcher.js");
        var equicordPatcherPath = Path.Combine(equicordBase, "dist", "patcher.js");
        // BD puts its asar in %APPDATA%\BetterDiscord\data
        var betterDiscordAsarPath = Path.Combine(appData, "BetterDiscord", "data", "betterdiscord.asar");

        Platform = new WindowsDiscordPlatform(vencordPatcherPath, equicordPatcherPath, betterDiscordAsarPath);

        // --openasar-test <dir>: fetches OpenAsar and checks detection
        if (e.Args.Length == 2 && e.Args[0] == "--openasar-test")
        {
            string outcome;
            try { outcome = OpenAsarEngine.TestFetchAndDetect(e.Args[1], BaseDir); }
            catch (Exception ex) { outcome = "ERROR: " + ex.Message; }
            File.WriteAllText(Path.Combine(e.Args[1], "result.txt"), outcome);
            Shutdown();
            return;
        }

        // --bd-test <appDir>: dry-runs BD detection/injection without touching anything
        if (e.Args.Length == 2 && e.Args[0] == "--bd-test")
        {
            string outcome;
            try { outcome = BetterDiscordEngine.DryRun(e.Args[1], Platform.BetterDiscordAsarPath); }
            catch (Exception ex) { outcome = "ERROR: " + ex.Message; }
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "bd_test.txt"), outcome);
            Shutdown();
            return;
        }

        bool tray = e.Args.Any(a => a.TrimStart('-', '/').Equals("tray", StringComparison.OrdinalIgnoreCase));
        bool selfTest = e.Args.Any(a => a.TrimStart('-', '/').Equals("selftest", StringComparison.OrdinalIgnoreCase));

        // Single instance check via platform (skipped for --selftest).
        if (!selfTest)
        {
            if (!Platform.TryAcquireSingleInstance())
            {
                Log.Write("App already running. Exiting.", "WARN");
                Shutdown();
                return;
            }
        }

        // Don't hard-crash on unhandled UI exceptions.
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Write($"UI exception: {ex.Exception.Message}\n{ex.Exception.StackTrace}", "ERROR");
            ex.Handled = true;
        };

        try
        {
            var win = new MainWindow();
            win.Initialize(startHidden: tray, selfTest: selfTest);
            if (selfTest)
            {
                Log.Write("Self-test OK.", "INFO");
                Shutdown();
                return;
            }
            if (!tray) win.Show();
        }
        catch (Exception ex)
        {
            Log.Write($"FATAL: {ex.Message}\n{ex.StackTrace}", "ERROR");
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
    }
}
