using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PatchCord;

/// <summary>
/// View-model for one InstallRow in the Status tab.
/// Wraps an Install + the last-known InstallState so the UI can bind to
/// observable properties without polling engine methods directly.
/// </summary>
internal sealed class InstallRowViewModel : INotifyPropertyChanged
{
    private readonly Install _install;
    private InstallState _state;

    public InstallRowViewModel(Install install, InstallState state)
    {
        _install = install;
        _state = state;
    }

    // ── Identity / path ───────────────────────────────────────────────────────

    public string Name => _install.Name;
    public string Path => _install.Path;
    public bool IsCustom => _install.Custom;

    // ── Enabled toggle ────────────────────────────────────────────────────────

    public bool Enabled
    {
        get => _install.Enabled;
        set
        {
            if (_install.Enabled == value) return;
            _install.Enabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ToggleLabel));
        }
    }

    public string ToggleLabel => Enabled ? "Managed" : "Paused";

    // ── Mod ───────────────────────────────────────────────────────────────────

    public string ClientMod
    {
        get => _install.ClientMod;
        set
        {
            if (_install.ClientMod == value) return;
            _install.ClientMod = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ModLabel));
        }
    }

    public string ModLabel => _install.ClientMod switch
    {
        "vencord"       => "Vencord",
        "equicord"      => "Equicord",
        "betterdiscord" => "BetterDiscord",
        _               => "No mod",
    };

    // ── Status badges (from InstallState) ────────────────────────────────────

    public void RefreshState(InstallState state)
    {
        _state = state;
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(PrimaryBadgeText));
        OnPropertyChanged(nameof(PrimaryBadgeVisible));
        OnPropertyChanged(nameof(OpenAsarBadgeText));
        OnPropertyChanged(nameof(OpenAsarBadgeVisible));
        OnPropertyChanged(nameof(ModLabel));
    }

    public bool IsInstalled => _state.Installed;
    public bool IsRunning => _state.Running;

    /// <summary>
    /// Text for the main status badge (Vencord / Equicord / BetterDiscord / Not patched / …).
    /// </summary>
    public string PrimaryBadgeText
    {
        get
        {
            if (!_state.Installed) return "Not installed";
            return _state.InjectedMod switch
            {
                "vencord"       => "Vencord",
                "equicord"      => "Equicord",
                "betterdiscord" => "BetterDiscord",
                "other"         => "Other mod",
                _               => _install.Enabled && _install.ClientMod != "none"
                    ? (_state.Running ? $"No {ModLabel}" : "Not patched")
                    : "Not patched",
            };
        }
    }

    public bool PrimaryBadgeVisible => _state.Installed;

    public string OpenAsarBadgeText
    {
        get
        {
            if (!_state.Installed) return "";
            if (_state.OpenAsarPresent) return "OpenAsar";
            return "";
        }
    }

    public bool OpenAsarBadgeVisible => _state.Installed && _state.OpenAsarPresent;

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
