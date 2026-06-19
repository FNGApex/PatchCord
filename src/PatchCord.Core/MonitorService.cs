using System.IO;

namespace PatchCord;

/// <summary>
/// Result of one reconciliation pass via <see cref="MonitorService.RunOnce"/>.
/// </summary>
public sealed record MonitorResult(
    bool Recorded,
    IReadOnlyDictionary<string, InstallState> States);

/// <summary>
/// Platform-neutral monitor reconciliation service.
/// Extracted from <c>MainWindow.InvokeMonitor</c> (Windows shell) so both shells
/// share one implementation. The only UI dependency is the injected
/// <paramref name="showAlert"/> callback.
/// </summary>
public sealed class MonitorService
{
    private readonly IDiscordPlatform _platform;
    private readonly string _baseDir;
    private readonly Action<string> _showAlert;

    // Stubs built from the platform mod patcher paths.
    private readonly Dictionary<string, byte[]> _stubs = new();

    // Per-session state (mirrors the private fields formerly on MainWindow).
    private readonly HashSet<string> _patchFailed = new();
    private readonly HashSet<string> _alerted = new();
    private readonly Dictionary<string, InstallState> _lastStates = new();

    /// <summary>
    /// Read-only view of the last-known install states.
    /// Callers use this to render install rows without re-querying the platform.
    /// </summary>
    public IReadOnlyDictionary<string, InstallState> LastStates => _lastStates;

    /// <summary>
    /// Create the service.
    /// </summary>
    /// <param name="platform">The active platform implementation.</param>
    /// <param name="baseDir">Base directory for caching (e.g. OpenAsar cache).</param>
    /// <param name="showAlert">
    /// Called to surface an alert to the user.
    /// Windows: <c>Alert.Show(_cfg, msg)</c>; Mac: <c>MacAlert.Show(cfg, msg)</c>.
    /// </param>
    public MonitorService(IDiscordPlatform platform, string baseDir, Action<string> showAlert)
    {
        _platform = platform;
        _baseDir = baseDir;
        _showAlert = showAlert;
        RebuildStubs();
    }

    /// <summary>
    /// Rebuild the asar stubs from the current platform patcher paths.
    /// Call after changing patcher paths (normally never needed after construction).
    /// </summary>
    public void RebuildStubs()
    {
        _stubs["vencord"] = PatchEngine.BuildStubAsar(_platform.VencordPatcherPath);
        _stubs["equicord"] = PatchEngine.BuildStubAsar(_platform.EquicordPatcherPath);
    }

    /// <summary>
    /// Clear the patch-failed and alerted sets.
    /// Call when the user toggles monitoring on/off or changes a mod.
    /// </summary>
    public void Reset()
    {
        _patchFailed.Clear();
        _alerted.Clear();
    }

    /// <summary>
    /// Clear the patch-failed flag for a SINGLE install (re-arms patching for it on
    /// the next pass). Use when the user toggles/changes the mod for one install.
    /// </summary>
    public void ClearFailed(string installPath) => _patchFailed.Remove(installPath);

    /// <summary>
    /// Clear ALL patch-failed flags (but not the alerted set). Use for an
    /// "apply mod to all installs" action.
    /// </summary>
    public void ClearAllFailed() => _patchFailed.Clear();

    /// <summary>
    /// Compute <see cref="InstallState"/> for <paramref name="inst"/>
    /// using the active <see cref="IDiscordPlatform"/>.
    /// </summary>
    public InstallState GetInstallState(Install inst, bool checkOpenAsar = false)
    {
        bool running = _platform.IsRunning(inst);
        var resourcesDir = _platform.ResolveResourcesDir(inst);
        var coreAppDir = _platform.ResolveCoreAppDir(inst);
        var versionLabel = _platform.AppVersionLabel(inst);
        return PatchEngine.GetState(running, resourcesDir, coreAppDir, versionLabel, checkOpenAsar);
    }

    /// <summary>
    /// True when the mod's data file exists on disk.
    /// </summary>
    public bool ModInstalled(string mod) => mod switch
    {
        "vencord"       => File.Exists(_platform.VencordPatcherPath),
        "equicord"      => File.Exists(_platform.EquicordPatcherPath),
        "betterdiscord" => File.Exists(_platform.BetterDiscordAsarPath),
        _               => true, // "none" is always "installed"
    };

    /// <summary>
    /// Human-readable mod name used in log/alert/history strings. Wording is byte-identical
    /// to <c>MainWindow.ModLabel</c> in the Windows shell.
    /// </summary>
    public static string ModLabel(string mod) => mod switch
    {
        "vencord"       => "Vencord",
        "equicord"      => "Equicord",
        "betterdiscord" => "BetterDiscord",
        _               => "the client mod",
    };

    /// <summary>
    /// Short mod name (badge / history). Wording is byte-identical to
    /// <c>MainWindow.ModShort</c> in the Windows shell.
    /// </summary>
    public static string ModShort(string mod) => mod switch
    {
        "vencord"       => "Vencord",
        "equicord"      => "Equicord",
        "betterdiscord" => "BetterDiscord",
        _               => "None",
    };

    /// <summary>
    /// Run one reconciliation pass.
    /// This is the WHOLE body of <c>MainWindow.InvokeMonitor</c> minus the trailing
    /// <c>UpdateStatusUi()</c> / <c>BuildInstallRows()</c> / <c>Save()</c> calls.
    /// Returns a <see cref="MonitorResult"/> the caller can use to decide whether
    /// to <c>Save()</c> and to refresh the UI.
    /// </summary>
    public MonitorResult RunOnce(AppConfig cfg)
    {
        bool wantOpenAsar = cfg.OpenAsar;
        var states = new Dictionary<string, InstallState>();
        var candidates = new List<(Install inst, string desiredAsar, bool desiredBD, bool needOpenAsar, bool asarChange, bool bdChange)>();
        foreach (var inst in cfg.Installs)
        {
            var st = GetInstallState(inst, wantOpenAsar);
            states[inst.Path] = st;
            var key = inst.Path;

            var desired = inst.ClientMod;                              // each install picks its own mod
            bool modReady = desired == "none" || ModInstalled(desired);
            string desiredAsar = desired is "vencord" or "equicord" ? desired : "none";
            bool desiredBD = desired == "betterdiscord";

            bool managed = inst.Enabled && st.Installed && st.Running && st.Resources != null && st.AppDir != null
                           && !_patchFailed.Contains(inst.Path);
            bool needOpenAsar = managed && wantOpenAsar && !st.OpenAsarPresent;

            bool asarChange = false, bdChange = false;
            if (managed && modReady)
            {
                // Layer A (app.asar): only ever touch our own vencord/equicord stubs.
                asarChange = desiredAsar == "none"
                    ? st.AsarMod is "vencord" or "equicord"
                    : st.AsarMod != desiredAsar && st.AsarMod is "vencord" or "equicord" or "none";
                // Layer B (BetterDiscord core patch).
                bdChange = desiredBD ? !st.BdActive : st.BdActive;
            }

            if (needOpenAsar || asarChange || bdChange)
            {
                candidates.Add((inst, desiredAsar, desiredBD, needOpenAsar, asarChange, bdChange));
                if (!_alerted.Contains(key))
                {
                    _alerted.Add(key);
                    var parts = new List<string>();
                    if (asarChange || bdChange) parts.Add(desired == "none" ? "no client mod" : ModLabel(desired));
                    if (needOpenAsar) parts.Add("OpenAsar");
                    var what = string.Join(" + ", parts);
                    if (cfg.MonitoringEnabled)
                    {
                        _showAlert($"Restoring {what} on {inst.Name} and restarting Discord...");
                        Log.Write($"{inst.Name}: applying {what}...", "ACTION");
                    }
                    else
                    {
                        _showAlert($"{inst.Name} needs {what}, but monitoring is OFF — leaving it as-is.");
                        Log.Write($"{inst.Name}: needs {what} (monitoring OFF).", "WARN");
                    }
                }
            }
            else
            {
                _alerted.Remove(key);
            }
        }
        foreach (var kv in states) _lastStates[kv.Key] = kv.Value;

        bool recorded = false;
        if (cfg.MonitoringEnabled && candidates.Count > 0)
        {
            if (candidates.Any(t => _platform.IsUpdateInProgress(t.inst)))
            {
                Log.Write("Discord update in progress; deferring patch.", "WARN");
            }
            else
            {
                var stopped = new List<Install>();
                var done = new List<(Install inst, string summary)>();
                foreach (var (c, desiredAsar, desiredBD, needOpenAsar, asarChange, bdChange) in candidates)
                {
                    try
                    {
                        _platform.Stop(c);
                        stopped.Add(c);
                        var st = states[c.Path];
                        var resources = st.Resources!;
                        var appDir = st.AppDir!;
                        var changes = new List<string>();
                        // OpenAsar first (underlying asar), then the app.asar client mod on top.
                        if (needOpenAsar)
                        {
                            OpenAsarEngine.Install(resources, _baseDir);
                            Log.Write($"{c.Name}: OpenAsar installed.", "OK");
                            changes.Add("OpenAsar");
                        }
                        if (asarChange)
                        {
                            if (st.AsarMod is "vencord" or "equicord")
                                PatchEngine.Unpatch(resources);
                            if (desiredAsar != "none")
                            {
                                PatchEngine.Patch(resources, _stubs[desiredAsar]);
                                Log.Write($"{c.Name}: {ModLabel(desiredAsar)} injected.", "OK");
                                changes.Add(ModShort(desiredAsar));
                            }
                            else { Log.Write($"{c.Name}: client mod removed.", "OK"); changes.Add("removed client mod"); }
                        }
                        if (bdChange)
                        {
                            if (desiredBD)
                            {
                                BetterDiscordEngine.Inject(appDir, _platform.BetterDiscordAsarPath);
                                Log.Write($"{c.Name}: BetterDiscord injected.", "OK");
                                changes.Add("BetterDiscord");
                            }
                            else
                            {
                                BetterDiscordEngine.Restore(appDir);
                                Log.Write($"{c.Name}: BetterDiscord removed.", "OK");
                                changes.Add("removed BetterDiscord");
                            }
                        }
                        done.Add((c, changes.Count > 0 ? string.Join(" + ", changes) : "re-patched"));
                    }
                    catch (Exception ex)
                    {
                        // Back off so we don't kill Discord again on the next check.
                        _patchFailed.Add(c.Path);
                        Log.Write($"Failed to patch {c.Name}: {ex.Message}. Leaving it alone. " +
                                  "Re-run that mod's installer, then toggle the install off and on.", "ERROR");
                    }
                }
                // Always restart Discord if we stopped it, even on a patch failure.
                foreach (var c in stopped)
                {
                    _platform.Start(c);
                    Log.Write($"Restarted {c.Name}.", "OK");
                    _lastStates[c.Path] = GetInstallState(c, wantOpenAsar);
                }
                foreach (var (c, summary) in done)
                {
                    _showAlert($"Restored {c.Name}. Discord has been restarted.");
                    cfg.AddHistory(c.Name, summary);
                    recorded = true;
                }
            }
        }

        return new MonitorResult(recorded, states);
    }
}
