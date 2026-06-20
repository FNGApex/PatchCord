using System.Diagnostics;
using System.IO;
using System.Threading;

namespace PatchCord;

/// <summary>
/// Windows implementation of <see cref="IDiscordPlatform"/>.
/// Wraps the Windows-specific Discord discovery (%LOCALAPPDATA%), process control
/// (Update.exe, Process.Kill), mod-data paths (%APPDATA%), run-at-login (COM .lnk),
/// and the Global\ named mutex for single-instance enforcement.
/// </summary>
internal sealed class WindowsDiscordPlatform : IDiscordPlatform
{
    private readonly string _vencordPatcherPath;
    private readonly string _equicordPatcherPath;
    private readonly string _betterDiscordAsarPath;
    private Mutex? _mutex;

    public WindowsDiscordPlatform(
        string vencordPatcherPath,
        string equicordPatcherPath,
        string betterDiscordAsarPath)
    {
        _vencordPatcherPath = vencordPatcherPath;
        _equicordPatcherPath = equicordPatcherPath;
        _betterDiscordAsarPath = betterDiscordAsarPath;
    }

    // ── Discovery ──────────────────────────────────────────────────────────────

    public IReadOnlyList<Install> DiscoverInstalls()
    {
        // Mirrors former Config.FindStandardInstalls: scan %LOCALAPPDATA%\<branch>\Update.exe.
        var found = new List<Install>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var b in AppConfig.Branches)
        {
            var root = Path.Combine(local, b);
            if (File.Exists(Path.Combine(root, "Update.exe")))
                found.Add(new Install { Name = b, Branch = b, Path = root, Custom = false });
        }
        return found;
    }

    public bool LooksLikeInstall(string path, out string branch)
    {
        // A path is a valid Discord install root if it has Update.exe or at least one app-* dir.
        bool valid = File.Exists(Path.Combine(path, "Update.exe"))
                     || (Directory.Exists(path) && Directory.GetDirectories(path, "app-*").Length > 0);
        if (!valid) { branch = "Discord"; return false; }
        branch = PatchEngine.BranchFromLeaf(path);
        return true;
    }

    // ── Path resolution ────────────────────────────────────────────────────────

    public string? ResolveResourcesDir(Install inst)
    {
        var appDir = GetLatestAppDir(inst.Path);
        return appDir != null ? Path.Combine(appDir.FullName, "resources") : null;
    }

    public string? ResolveCoreAppDir(Install inst)
    {
        return GetLatestAppDir(inst.Path)?.FullName;
    }

    public string? AppVersionLabel(Install inst)
    {
        return GetLatestAppDir(inst.Path)?.Name;
    }

    /// <summary>
    /// Highest-versioned app-* folder under <paramref name="branchRoot"/> that has a resources dir.
    /// Mirrors former PatchEngine.GetLatestAppDir.
    /// </summary>
    private static DirectoryInfo? GetLatestAppDir(string branchRoot)
    {
        if (!Directory.Exists(branchRoot)) return null;
        return new DirectoryInfo(branchRoot)
            .GetDirectories("app-*")
            .Where(d => Directory.Exists(Path.Combine(d.FullName, "resources")))
            .OrderBy(d =>
            {
                return Version.TryParse(d.Name.Length > 4 ? d.Name[4..] : "", out var v)
                    ? v : new Version(0, 0, 0);
            })
            .LastOrDefault();
    }

    // ── BandagedBD (Layer C) ─────────────────────────────────────────────────────
    // Delegates to BandagedBDEngine, which snapshots/restores the resources/app folder.

    public bool BandagedBdInjected(Install inst)
        => ResolveResourcesDir(inst) is { } r && BandagedBDEngine.IsInjected(r);

    public bool BandagedBdHasSnapshot(Install inst)
        => BandagedBDEngine.HasSnapshot(inst.Path);

    public void BandagedBdSnapshot(Install inst)
    {
        if (ResolveResourcesDir(inst) is { } r) BandagedBDEngine.CaptureSnapshot(r, inst.Path);
    }

    public void BandagedBdRestore(Install inst)
    {
        if (ResolveResourcesDir(inst) is { } r) BandagedBDEngine.Restore(r, inst.Path);
    }

    public void BandagedBdRemove(Install inst)
    {
        if (ResolveResourcesDir(inst) is { } r) BandagedBDEngine.Remove(r);
    }

    // ── Process control ────────────────────────────────────────────────────────

    public bool IsRunning(Install inst)
        => Process.GetProcessesByName(inst.Branch).Length > 0;

    public bool IsUpdateInProgress(Install inst)
        => Process.GetProcessesByName("Update").Length > 0;

    public void Stop(Install inst)
    {
        // Mirrors former PatchEngine.StopProcesses.
        var procs = Process.GetProcessesByName(inst.Branch);
        if (procs.Length == 0) return;
        Log.Write($"Stopping {procs.Length} '{inst.Branch}' process(es) to patch.");
        foreach (var p in procs)
        {
            try { p.Kill(); } catch { }
        }
        for (int i = 0; i < 50; i++)
        {
            Thread.Sleep(200);
            if (Process.GetProcessesByName(inst.Branch).Length == 0) break;
        }
        Thread.Sleep(300);
    }

    public void Start(Install inst)
    {
        // Mirrors former PatchEngine.StartDiscord: launch via Update.exe --processStart.
        var update = Path.Combine(inst.Path, "Update.exe");
        if (!File.Exists(update)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = update,
            Arguments = $"--processStart {inst.Branch}.exe",
            UseShellExecute = false,
        });
    }

    // ── Mod data paths ─────────────────────────────────────────────────────────

    public string VencordPatcherPath => _vencordPatcherPath;
    public string EquicordPatcherPath => _equicordPatcherPath;
    public string BetterDiscordAsarPath => _betterDiscordAsarPath;

    // ── Run-at-login ───────────────────────────────────────────────────────────

    public bool RunAtLoginEnabled => Startup.IsEnabled;

    public void SetRunAtLogin(bool enabled) => Startup.Set(enabled);

    // ── Single instance ────────────────────────────────────────────────────────

    public bool TryAcquireSingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: false, "Global\\PatchCordApp");
        return _mutex.WaitOne(0);
    }
}
