using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PatchCord;

/// <summary>
/// Main view-model for the Mac port.
/// Wraps AppConfig and exposes observable properties for the two-tab window.
/// Saves config on every change (mirrors MainWindow.Save() in the Windows shell).
/// </summary>
internal sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly AppConfig _cfg;

    public MainViewModel(AppConfig cfg)
    {
        _cfg = cfg;
        RebuildInstallRows();
    }

    // ── Install rows ──────────────────────────────────────────────────────────

    public ObservableCollection<InstallRowViewModel> Installs { get; } = new();

    /// <summary>
    /// Rebuild the install row VMs from the current config + current disk state.
    /// Call after config changes or a manual Refresh.
    /// </summary>
    public void RebuildInstallRows()
    {
        Installs.Clear();
        foreach (var inst in _cfg.Installs)
        {
            InstallState state;
            try { state = MacAppState.GetInstallState(inst, _cfg.OpenAsar); }
            catch { state = new InstallState(false, false, null, null, null, false); }
            Installs.Add(new InstallRowViewModel(inst, state));
        }
    }

    /// <summary>
    /// Remove a custom install by path.
    /// </summary>
    public void RemoveInstall(string path)
    {
        _cfg.Installs.RemoveAll(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        Save();
        RebuildInstallRows();
    }

    /// <summary>
    /// Toggle enabled/paused for an install and save.
    /// </summary>
    public void ToggleInstallEnabled(InstallRowViewModel row)
    {
        row.Enabled = !row.Enabled;
        Save();
    }

    // ── Patch history ─────────────────────────────────────────────────────────

    public IReadOnlyList<PatchEvent> History => _cfg.History;

    // ── Status tab ───────────────────────────────────────────────────────────

    public bool MonitoringEnabled
    {
        get => _cfg.MonitoringEnabled;
        set
        {
            if (_cfg.MonitoringEnabled == value) return;
            _cfg.MonitoringEnabled = value;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(MonitoringStatusText));
            OnPropertyChanged(nameof(MonitoringSubText));
            OnPropertyChanged(nameof(ToggleButtonLabel));
        }
    }

    public string MonitoringStatusText => MonitoringEnabled ? "Active" : "Paused";

    public string MonitoringSubText => MonitoringEnabled
        ? "Watching for Discord instances that need re-patching..."
        : "Monitoring is paused. Discord will not be re-patched.";

    public string ToggleButtonLabel => MonitoringEnabled ? "Turn Off" : "Turn On";

    // ── Mod-missing warning ───────────────────────────────────────────────────

    public bool ModMissingWarningVisible
    {
        get
        {
            return _cfg.Installs.Any(i =>
                i.Enabled && i.ClientMod != "none" && !MacAppState.ModInstalled(i.ClientMod));
        }
    }

    public string ModMissingWarningText
    {
        get
        {
            var missing = _cfg.Installs
                .Where(i => i.Enabled && i.ClientMod != "none" && !MacAppState.ModInstalled(i.ClientMod))
                .Select(i => i.ClientMod).Distinct().ToList();
            if (missing.Count == 0) return "";
            var label = missing[0] switch
            {
                "equicord"      => "Equicord",
                "betterdiscord" => "BetterDiscord",
                _               => "Vencord",
            };
            var dir = missing[0] switch
            {
                "equicord"      => "~/Library/Application Support/Equicord/dist",
                "betterdiscord" => "~/Library/Application Support/BetterDiscord/data",
                _               => "~/Library/Application Support/Vencord/dist",
            };
            if (missing.Count == 1)
                return $"{label} isn't installed yet. Run the {label} installer once (so {dir} exists) and this app will keep it injected after every Discord update.";
            return $"Some installs use mods that aren't installed yet ({string.Join(", ", missing.Select(m => m switch { "equicord" => "Equicord", "betterdiscord" => "BetterDiscord", _ => "Vencord" }))}). Run each one's installer once so this app can keep them injected.";
        }
    }

    // ── Options tab — Client mod ──────────────────────────────────────────────

    public string ClientMod
    {
        get => _cfg.ClientMod;
        set
        {
            if (_cfg.ClientMod == value) return;
            _cfg.ClientMod = value;
            foreach (var i in _cfg.Installs) i.ClientMod = value;
            Save();
            OnPropertyChanged();
            RebuildInstallRows();
            OnPropertyChanged(nameof(ModMissingWarningVisible));
            OnPropertyChanged(nameof(ModMissingWarningText));
        }
    }

    public static readonly (string Mod, string Title, string Desc)[] ClientModItems =
    {
        ("vencord",       "Vencord",       "The original Discord client mod — adds plugins, themes and tweaks."),
        ("equicord",      "Equicord",       "A community fork of Vencord with 300+ extra plugins."),
        ("betterdiscord", "BetterDiscord", "The long-running client mod with a plugin/theme store. Patches Discord's core (a different method than Vencord)."),
        ("none",          "No client mod",  "Don't keep any client mod injected (you can still use OpenAsar)."),
    };

    // ── Options tab — OpenAsar ────────────────────────────────────────────────

    public bool OpenAsar
    {
        get => _cfg.OpenAsar;
        set
        {
            if (_cfg.OpenAsar == value) return;
            _cfg.OpenAsar = value;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(OpenAsarToggleLabel));
        }
    }

    public string OpenAsarToggleLabel => OpenAsar ? "On" : "Off";

    // ── Options tab — Run at startup ──────────────────────────────────────────

    public bool RunAtLogin
    {
        get => MacAppState.Platform.RunAtLoginEnabled;
        set
        {
            try { MacAppState.Platform.SetRunAtLogin(value); }
            catch (Exception ex) { Log.Write($"Startup toggle failed: {ex.Message}", "WARN"); }
            OnPropertyChanged();
            OnPropertyChanged(nameof(RunAtLoginToggleLabel));
        }
    }

    public string RunAtLoginToggleLabel => RunAtLogin ? "On" : "Off";

    // ── Options tab — Notifications ───────────────────────────────────────────

    public bool NotificationsEnabled
    {
        get => _cfg.Ui.NotificationsEnabled;
        set
        {
            if (_cfg.Ui.NotificationsEnabled == value) return;
            _cfg.Ui.NotificationsEnabled = value;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(NotifyToggleLabel));
        }
    }

    public string NotifyToggleLabel => NotificationsEnabled ? "On" : "Off";

    public double NotifyScale
    {
        get => _cfg.Ui.NotifyScale * 100;
        set
        {
            var scaled = Math.Round(value / 100, 2);
            if (_cfg.Ui.NotifyScale == scaled) return;
            _cfg.Ui.NotifyScale = scaled;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(NotifyScaleLabel));
        }
    }

    public string NotifyScaleLabel => $"{(int)(_cfg.Ui.NotifyScale * 100)} %";

    public int NotifyDurationSec
    {
        get => _cfg.Ui.NotifyDurationSec;
        set
        {
            if (_cfg.Ui.NotifyDurationSec == value) return;
            _cfg.Ui.NotifyDurationSec = value;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(NotifyDurationLabel));
        }
    }

    public string NotifyDurationLabel => $"{_cfg.Ui.NotifyDurationSec} s";

    public string NotifyStyle
    {
        get => _cfg.Ui.NotifyStyle;
        set
        {
            if (_cfg.Ui.NotifyStyle == value) return;
            _cfg.Ui.NotifyStyle = value;
            Save();
            OnPropertyChanged();
        }
    }

    public static readonly string[] NotifyStyles = { "bar", "solid", "minimal", "outline" };

    // ── Options tab — Theme ───────────────────────────────────────────────────

    public string Theme
    {
        get => _cfg.Ui.Theme;
        set
        {
            if (_cfg.Ui.Theme == value) return;
            _cfg.Ui.Theme = value;
            Save();
            OnPropertyChanged();
        }
    }

    // ── Options tab — Interval slider ────────────────────────────────────────

    public int IntervalSeconds
    {
        get => _cfg.IntervalSeconds;
        set
        {
            int v = Math.Max(5, value);
            if (_cfg.IntervalSeconds == v) return;
            _cfg.IntervalSeconds = v;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IntervalLabel));
        }
    }

    public string IntervalLabel => $"Every {_cfg.IntervalSeconds} s";

    // ── Save helper ───────────────────────────────────────────────────────────

    private void Save() => MacAppState.Save();

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
