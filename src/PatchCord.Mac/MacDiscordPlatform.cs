using System.Diagnostics;
using System.IO;
using System.Threading;

namespace PatchCord;

/// <summary>
/// macOS implementation of <see cref="IDiscordPlatform"/>.
/// <para>
/// Layer A (app.asar swap / Vencord / Equicord) targets the BUNDLE's
/// Contents/Resources/ directory — verified against the official Vencord Installer
/// (find_discord_darwin.go / patcher.go / app_asar.go) and the real local install.
/// </para>
/// <para>
/// Layer B (BetterDiscord index.js rewrite) targets
/// ~/Library/Application Support/&lt;branch&gt;/app-&lt;ver&gt; — the App-Support core tree.
/// </para>
/// <para>
/// The two layers live in DIFFERENT roots on macOS (design §8.3). Both are user-owned
/// and writable without sudo; no SIP or Gatekeeper blocker (design §8.1 / §7).
/// </para>
/// </summary>
internal sealed class MacDiscordPlatform : IDiscordPlatform
{
    // ── Branch maps (design §8.4) ──────────────────────────────────────────────

    /// <summary>
    /// Map from the Windows-style branch key used throughout the codebase to the
    /// macOS bundle name (without .app), the App-Support directory name (Layer B),
    /// and the macOS process name.
    /// </summary>
    private static readonly (string Branch, string Bundle, string AppSupport, string ProcessName)[] BranchMap =
    {
        ("Discord",             "Discord",             "discord",             "Discord"),
        ("DiscordPTB",          "Discord PTB",         "discordptb",          "Discord PTB"),
        ("DiscordCanary",       "Discord Canary",      "discordcanary",       "Discord Canary"),
        ("DiscordDevelopment",  "Discord Development", "discorddevelopment",  "Discord Development"),
    };

    // ── Mod-data paths (B2.7) ─────────────────────────────────────────────────

    private static readonly string AppSupportRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     "Library", "Application Support");

    public string VencordPatcherPath { get; } =
        Environment.GetEnvironmentVariable("VENCORD_USER_DATA_DIR") is { } v && v.Length > 0
            ? Path.Combine(v, "dist", "patcher.js")
            : Path.Combine(AppSupportRoot, "Vencord", "dist", "patcher.js");

    public string EquicordPatcherPath { get; } =
        Environment.GetEnvironmentVariable("EQUICORD_USER_DATA_DIR") is { } e && e.Length > 0
            ? Path.Combine(e, "dist", "patcher.js")
            : Path.Combine(AppSupportRoot, "Equicord", "dist", "patcher.js");

    public string BetterDiscordAsarPath { get; } =
        Path.Combine(AppSupportRoot, "BetterDiscord", "data", "betterdiscord.asar");

    // These are evaluated per-instance so VENCORD_USER_DATA_DIR / EQUICORD_USER_DATA_DIR
    // are read at construction time. Re-use is fine for a single-run process.

    // ── Discovery (B2.1) ──────────────────────────────────────────────────────

    public IReadOnlyList<Install> DiscoverInstalls()
    {
        var found = new List<Install>();
        var searchDirs = new[]
        {
            "/Applications",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications"),
        };
        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var (branch, bundle, _, _) in BranchMap)
            {
                var bundlePath = Path.Combine(dir, bundle + ".app");
                if (LooksLikeInstall(bundlePath, out _))
                {
                    // Avoid duplicates (both /Applications and ~/Applications could have it)
                    if (!found.Any(f => string.Equals(f.Path, bundlePath, StringComparison.Ordinal)))
                        found.Add(new Install
                        {
                            Name = bundle,
                            Branch = branch,
                            Path = bundlePath,
                            Custom = false,
                        });
                }
            }
        }
        return found;
    }

    public bool LooksLikeInstall(string path, out string branch)
    {
        // A macOS Discord install is a *.app bundle with Contents/Resources/app.asar.
        if (!path.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(path)
            || !File.Exists(Path.Combine(path, "Contents", "Resources", "app.asar")))
        {
            branch = "Discord";
            return false;
        }
        branch = BranchFromBundle(path);
        return true;
    }

    private static string BranchFromBundle(string bundlePath)
    {
        var leaf = Path.GetFileNameWithoutExtension(bundlePath); // e.g. "Discord PTB"
        foreach (var (branch, bundle, _, _) in BranchMap)
        {
            if (string.Equals(leaf, bundle, StringComparison.OrdinalIgnoreCase))
                return branch;
        }
        // Fallback: use PatchEngine's leaf-name heuristic.
        return PatchEngine.BranchFromLeaf(bundlePath);
    }

    // ── Path resolution (B2.2 / B2.3) ────────────────────────────────────────

    /// <summary>
    /// Layer A: the bundle's Contents/Resources directory.
    /// app.asar lives here; _app.asar is the patched marker.
    /// </summary>
    public string? ResolveResourcesDir(Install inst)
    {
        var dir = Path.Combine(inst.Path, "Contents", "Resources");
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>
    /// Layer B: the highest app-X.Y.Z in ~/Library/Application Support/&lt;branch&gt;/
    /// that contains a modules/ subdirectory. BetterDiscordEngine.FindCoreIndexJs
    /// then navigates from here to the index.js.
    /// </summary>
    public string? ResolveCoreAppDir(Install inst)
    {
        return GetLatestCoreAppDir(AppSupportDirForBranch(inst.Branch))?.FullName;
    }

    public string? AppVersionLabel(Install inst)
    {
        return GetLatestCoreAppDir(AppSupportDirForBranch(inst.Branch))?.Name;
    }

    /// <summary>
    /// Maps a Windows-style branch key to the macOS App-Support root for that branch
    /// (e.g. "DiscordPTB" → "~/Library/Application Support/discordptb").
    /// </summary>
    private string AppSupportDirForBranch(string branch)
    {
        foreach (var (b, _, appSupport, _) in BranchMap)
            if (b == branch) return Path.Combine(AppSupportRoot, appSupport);
        // Unknown branch: lowercase-no-spaces heuristic (matches BD inject.ts behaviour).
        return Path.Combine(AppSupportRoot, branch.ToLowerInvariant().Replace(" ", ""));
    }

    /// <summary>
    /// Highest-versioned app-X.Y.Z directory under <paramref name="appSupportDir"/>
    /// that contains a modules/ subdirectory (mirrors PatchEngine.GetLatestAppDir logic,
    /// adapted for the macOS App-Support layout).
    /// </summary>
    private static DirectoryInfo? GetLatestCoreAppDir(string appSupportDir)
    {
        if (!Directory.Exists(appSupportDir)) return null;
        return new DirectoryInfo(appSupportDir)
            .GetDirectories("app-*")
            .Where(d => Directory.Exists(Path.Combine(d.FullName, "modules")))
            .OrderBy(d =>
            {
                // "app-X.Y.Z" → parse X.Y.Z for version ordering.
                var vStr = d.Name.Length > 4 ? d.Name[4..] : "";
                return Version.TryParse(vStr, out var v) ? v : new Version(0, 0, 0);
            })
            .LastOrDefault();
    }

    // ── Process control (B2.4 / B2.5 / B2.6) ─────────────────────────────────

    private static string ProcessNameForBranch(string branch)
    {
        foreach (var (b, _, _, procName) in BranchMap)
            if (b == branch) return procName;
        return branch; // fallback
    }

    public bool IsRunning(Install inst)
    {
        var procName = ProcessNameForBranch(inst.Branch);
        return Process.GetProcessesByName(procName).Length > 0;
    }

    /// <summary>
    /// macOS update guard (design §8.5). Returns true when:
    /// (a) a ShipIt process is running (Squirrel.Mac updater), OR
    /// (b) ShipIt_request.json in the branch's App-Support dir was modified within
    ///     the last 90 seconds (covers the window where ShipIt may not yet be running
    ///     but the request file has been written to trigger the update).
    /// Recency window: 90 s. Next-tick convergence backstops any race (patch is
    /// idempotent; _patchFailed prevents kill-loop on failure).
    /// </summary>
    public bool IsUpdateInProgress(Install inst)
    {
        // (a) Running ShipIt process
        if (Process.GetProcessesByName("ShipIt").Length > 0)
            return true;

        // (b) Fresh ShipIt_request.json in the branch App-Support dir
        var requestFile = Path.Combine(AppSupportDirForBranch(inst.Branch), "ShipIt_request.json");
        if (File.Exists(requestFile))
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(requestFile);
            if (age.TotalSeconds < 90)
                return true;
        }

        return false;
    }

    public void Stop(Install inst)
    {
        var procName = ProcessNameForBranch(inst.Branch);
        var procs = Process.GetProcessesByName(procName);
        if (procs.Length == 0) return;
        Log.Write($"Stopping {procs.Length} '{procName}' process(es) to patch.");
        foreach (var p in procs)
        {
            try { p.Kill(); } catch { }
        }
        // Poll up to 10 s for exit.
        for (int i = 0; i < 50; i++)
        {
            Thread.Sleep(200);
            if (Process.GetProcessesByName(procName).Length == 0) break;
        }
        Thread.Sleep(300);
    }

    public void Start(Install inst)
    {
        // macOS relaunch: open -a "<bundle name without .app>"
        // Works with or without the .app suffix; we use the canonical bundle name.
        var bundleName = BundleNameForBranch(inst.Branch);
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = $"-a \"{bundleName}\"",
            UseShellExecute = false,
        });
    }

    private static string BundleNameForBranch(string branch)
    {
        foreach (var (b, bundle, _, _) in BranchMap)
            if (b == branch) return bundle;
        return branch;
    }

    // ── Run-at-login (design §12) ─────────────────────────────────────────────

    private static readonly string LaunchAgentDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     "Library", "LaunchAgents");

    private static readonly string PlistPath =
        Path.Combine(LaunchAgentDir, "com.tomgks.patchcord.plist");

    /// <summary>
    /// True if the LaunchAgent plist exists at
    /// ~/Library/LaunchAgents/com.tomgks.patchcord.plist.
    /// </summary>
    public bool RunAtLoginEnabled => File.Exists(PlistPath);

    public void SetRunAtLogin(bool enabled)
    {
        if (enabled)
        {
            // Resolve the binary path from the running process.
            // TODO (B4): repoint this to the .app bundle executable once the bundle is packaged.
            //   The correct path for a released build is:
            //     <bundle>.app/Contents/MacOS/PatchCord
            //   At runtime: walk up from Environment.ProcessPath until we find a "*.app" ancestor,
            //   then construct Contents/MacOS/<CFBundleExecutable>. For now (dev build / no bundle)
            //   we use the raw executable path which is correct for dev launches.
            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            Directory.CreateDirectory(LaunchAgentDir);
            var plist = $"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>            <string>com.tomgks.patchcord</string>
  <key>ProgramArguments</key>
  <array>
    <string>{exePath}</string>
    <string>--tray</string>
  </array>
  <key>RunAtLoad</key>        <true/>
  <key>ProcessType</key>      <string>Interactive</string>
</dict>
</plist>
""";
            File.WriteAllText(PlistPath, plist);

            // Register with launchctl (bootstrap gui/$UID <plist>).
            // Swallow errors — if we're not in an interactive session the load may fail
            // but the plist is in place for the next login.
            TryLaunchctl($"bootstrap gui/{GetUid()} {PlistPath}");
        }
        else
        {
            if (File.Exists(PlistPath))
            {
                TryLaunchctl($"bootout gui/{GetUid()} {PlistPath}");
                File.Delete(PlistPath);
            }
        }
    }

    private static string GetUid()
    {
        // getuid() via a quick `id -u` — no P/Invoke required.
        try
        {
            var psi = new ProcessStartInfo("id", "-u") { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            var uid = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return uid;
        }
        catch { return ""; }
    }

    private static void TryLaunchctl(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("launchctl", args)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(5000);
        }
        catch { }
    }

    // ── Single instance (B3.7 / interface completeness) ───────────────────────

    private FileStream? _lockFile;

    /// <summary>
    /// Acquires a lock file at ~/Library/Application Support/PatchCord/.lock
    /// using an exclusive FileStream (O_EXLOCK / file-system lock). Returns true
    /// if this process is the first instance. Full double-launch test is B3.7.
    /// </summary>
    public bool TryAcquireSingleInstance()
    {
        var lockDir = Path.Combine(AppSupportRoot, "PatchCord");
        Directory.CreateDirectory(lockDir);
        var lockPath = Path.Combine(lockDir, ".lock");
        try
        {
            _lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
