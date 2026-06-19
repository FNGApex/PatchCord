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

    /// <summary>
    /// Returns the App Support root directory.  When the <c>PATCHCORD_APPSUPPORT_ROOT</c>
    /// environment variable is set and non-empty it is used as the root instead of
    /// <c>~/Library/Application Support</c>.  This is a test seam (B5.1) that mirrors
    /// the existing <c>VENCORD_USER_DATA_DIR</c>/<c>EQUICORD_USER_DATA_DIR</c> pattern;
    /// the default (env var unset) is byte-identical to the previous behaviour.
    /// </summary>
    private static string GetAppSupportRoot()
    {
        var envOverride = Environment.GetEnvironmentVariable("PATCHCORD_APPSUPPORT_ROOT");
        if (envOverride is { Length: > 0 })
            return envOverride;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            "Library", "Application Support");
    }

    public string VencordPatcherPath { get; } =
        Environment.GetEnvironmentVariable("VENCORD_USER_DATA_DIR") is { } v && v.Length > 0
            ? Path.Combine(v, "dist", "patcher.js")
            : Path.Combine(GetAppSupportRoot(), "Vencord", "dist", "patcher.js");

    public string EquicordPatcherPath { get; } =
        Environment.GetEnvironmentVariable("EQUICORD_USER_DATA_DIR") is { } e && e.Length > 0
            ? Path.Combine(e, "dist", "patcher.js")
            : Path.Combine(GetAppSupportRoot(), "Equicord", "dist", "patcher.js");

    public string BetterDiscordAsarPath { get; } =
        Path.Combine(GetAppSupportRoot(), "BetterDiscord", "data", "betterdiscord.asar");

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
        var root = GetAppSupportRoot();
        foreach (var (b, _, appSupport, _) in BranchMap)
            if (b == branch) return Path.Combine(root, appSupport);
        // Unknown branch: lowercase-no-spaces heuristic (matches BD inject.ts behaviour).
        return Path.Combine(root, branch.ToLowerInvariant().Replace(" ", ""));
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
    /// The recency window for <c>ShipIt_request.json</c>: if the file was modified within
    /// this many seconds it is treated as an in-progress update, even if the ShipIt process
    /// has not yet spawned (pre-spawn window). 90 s is a reasonable upper bound for copying
    /// a ~200 MB Discord bundle.
    /// </summary>
    private const int ShipItRecencyWindowSeconds = 90;

    /// <summary>
    /// macOS update guard (design §8.5 / B5.7). Returns true when:
    /// (a) a Discord-identity-scoped ShipIt process is running (Squirrel.Mac updater), OR
    /// (b) ShipIt_request.json in the branch's App-Support dir was modified within
    ///     <see cref="ShipItRecencyWindowSeconds"/> (covers the window where ShipIt may not
    ///     yet be running but the request file has been written to trigger the update).
    /// <para>
    /// The process check is DISCORD-IDENTITY-SCOPED (B5.7 fix): a ShipIt process whose
    /// executable path / command line does not contain a Discord identity string is ignored.
    /// This prevents false positives from other Squirrel.Mac apps (e.g. VS Code's
    /// <c>com.microsoft.VSCode.ShipIt</c>, verified live false-positive before this fix).
    /// </para>
    /// </summary>
    public bool IsUpdateInProgress(Install inst)
    {
        // (a) Discord-identity-scoped ShipIt process check (B5.7).
        // Discord's ShipIt carries a Discord-specific discriminator in its path/argv:
        //   cache dir: …/com.hnc.Discord.ShipIt/…
        //   bundle id in argv: com.discord.discord / com.hnc.Discord
        // We filter for that identity rather than accepting any "ShipIt" process name.
        foreach (var p in Process.GetProcessesByName("ShipIt"))
        {
            try
            {
                if (IsDiscordShipIt(p))
                    return true;
            }
            catch { /* unreadable process info — skip */ }
        }

        // (b) Fresh ShipIt_request.json in the branch App-Support dir.
        // This file is Discord-specific (lives under the branch's App-Support dir) and its
        // presence + freshness is sufficient as a pre-spawn backstop.
        var requestFile = Path.Combine(AppSupportDirForBranch(inst.Branch), "ShipIt_request.json");
        if (File.Exists(requestFile))
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(requestFile);
            if (age.TotalSeconds < ShipItRecencyWindowSeconds)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if <paramref name="p"/> is Discord's own ShipIt process.
    /// Tries <c>Process.MainModule.FileName</c> first; falls back to
    /// <c>ps -o command= -p &lt;pid&gt;</c> if that is null/unreadable (can occur on
    /// macOS for foreign-context processes). A process is considered Discord's if its
    /// resolved command contains a Discord identity string ("discord", "com.hnc.discord",
    /// or "com.discord.discord" — case-insensitive match on the executable path).
    /// </summary>
    private static bool IsDiscordShipIt(Process p)
    {
        // Try MainModule.FileName (may be null/inaccessible on macOS for foreign processes).
        string? cmdLine = null;
        try { cmdLine = p.MainModule?.FileName; } catch { }

        // Fall back to `ps -o command= -p <pid>` which reads the argv string directly.
        if (string.IsNullOrEmpty(cmdLine))
            cmdLine = GetProcessCommandViaPsCmd(p.Id);

        if (string.IsNullOrEmpty(cmdLine))
        {
            // Cannot determine identity — assume NOT Discord's to avoid false positives.
            return false;
        }

        // Discord identity markers in the ShipIt command/path:
        //   - the ShipIt cache path: …/com.hnc.Discord.ShipIt/…
        //   - the bundle id: com.discord.discord
        //   - the word "discord" more broadly (all Discord branches)
        var cmdLower = cmdLine.ToLowerInvariant();
        return cmdLower.Contains("discord");
    }

    /// <summary>
    /// Runs <c>ps -o command= -p &lt;pid&gt;</c> and returns the stdout, or null on failure.
    /// Used as a fallback when <c>Process.MainModule.FileName</c> is not readable.
    /// </summary>
    private static string? GetProcessCommandViaPsCmd(int pid)
    {
        try
        {
            var psi = new ProcessStartInfo("ps", $"-o command= -p {pid}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            using var ps = Process.Start(psi);
            if (ps == null) return null;
            var output = ps.StandardOutput.ReadToEnd().Trim();
            ps.WaitForExit(3000);
            return output.Length > 0 ? output : null;
        }
        catch { return null; }
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
        // B5.8: relaunch the INSTALLED copy explicitly using its full bundle path
        // so LaunchServices cannot accidentally pick up a staged update bundle at
        // app-<ver>/Discord.app/ (the updateBundleURL in ShipIt_request.json).
        //
        // Prefer: open "<inst.Path>"  (e.g. /Applications/Discord.app)
        // The platform already carries the bundle path in Install.Path (set by
        // DiscoverInstalls from /Applications); this is always the installed copy.
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = $"\"{inst.Path}\"",
            UseShellExecute = false,
        });
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
            // When running as the packaged .app, Environment.ProcessPath resolves to
            //   …/PatchCord.app/Contents/MacOS/PatchCord
            // which is exactly the CFBundleExecutable path launchctl needs (design §12).
            // Verified in B4: `ps aux` shows the bundle exe path, not a dotnet/dll path.
            // For dev builds (dotnet run / no bundle) the raw path is used, which is
            // also correct for that launch context.
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
        // The single-instance lock always uses the real (non-overridden) App Support root so
        // test harnesses that set PATCHCORD_APPSUPPORT_ROOT don't change the lock location.
        var realRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                    "Library", "Application Support");
        var lockDir = Path.Combine(realRoot, "PatchCord");
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
