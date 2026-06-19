using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PatchCord;

/// <summary>
/// Avalonia Application for PatchCord.Mac.
/// B3b: loads config via MacAppState, creates MainViewModel, initializes MainWindow.
/// B3c: wires the full tray NativeMenu (header/status, Show, Pause/Resume, Test notification, Quit).
/// </summary>
public sealed partial class App : Application
{
    // Kept to allow the MainWindow to update the tray toggle label on monitoring changes.
    internal static NativeMenuItem? TrayToggleItem { get; private set; }
    internal static NativeMenuItem? TrayHeaderItem { get; private set; }

    // Reference to the main window so the tray Show action can bring it to front.
    private MainWindow? _mainWindow;
    private MainViewModel? _vm;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Wire tray immediately after XAML load (TrayIcon items are now accessible).
        // VM will be set in OnFrameworkInitializationCompleted; use a deferred update.
        WireTrayMenuBasic();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Load config + build the VM
            var cfg = MacAppState.EnsureLoaded();
            _vm  = new MainViewModel(cfg);

            // Update tray labels now that VM is available.
            UpdateTrayHeader();
            UpdateTrayToggleLabel();

            // Create and initialize the window
            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;
            _mainWindow.Initialize(_vm);
        }
        base.OnFrameworkInitializationCompleted();
    }

    // ── Tray menu wiring ──────────────────────────────────────────────────────

    /// <summary>
    /// Wire click handlers on all tray items (called from Initialize, after XAML load).
    /// Labels are updated later in OnFrameworkInitializationCompleted once VM is ready.
    /// </summary>
    private void WireTrayMenuBasic()
    {
        var trayIcons = TrayIcon.GetIcons(this);
        if (trayIcons == null) return;

        foreach (var icon in trayIcons)
        {
            if (icon.Menu == null) continue;
            foreach (var item in icon.Menu.Items)
            {
                if (item is not NativeMenuItem mi) continue;
                switch (mi.Header)
                {
                    case "PatchCord":
                        TrayHeaderItem = mi;
                        break;

                    case "Open PatchCord":
                        mi.Click += (_, _) => ShowMainWindow();
                        break;

                    case "Pause monitoring":
                        TrayToggleItem = mi;
                        mi.Click += (_, _) => ToggleMonitoring();
                        break;

                    case "Test notification":
                        mi.Click += (_, _) => TriggerTestAlert();
                        break;

                    case "Quit":
                        mi.Click += (_, _) => Quit();
                        break;
                }
            }
        }
    }

    // ── Tray actions ──────────────────────────────────────────────────────────

    private void ShowMainWindow()
    {
        if (_mainWindow == null) return;
        _mainWindow.Show();
        _mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ToggleMonitoring()
    {
        if (_vm == null) return;
        _vm.MonitoringEnabled = !_vm.MonitoringEnabled;
        UpdateTrayHeader();
        UpdateTrayToggleLabel();
        // Let MainWindow refresh its Status UI if it is open.
        _mainWindow?.RefreshStatusUi();
    }

    private void TriggerTestAlert()
    {
        var cfg = MacAppState.Config;
        MacAlert.Show(cfg, "Test notification from PatchCord tray menu.", force: true);
    }

    // ── Tray label helpers (called by App and by MainWindow on monitoring toggle) ──

    internal void UpdateTrayHeader()
    {
        if (TrayHeaderItem == null || _vm == null) return;
        TrayHeaderItem.Header = $"PatchCord — {_vm.MonitoringStatusText}";
    }

    internal void UpdateTrayToggleLabel()
    {
        if (TrayToggleItem == null || _vm == null) return;
        TrayToggleItem.Header = _vm.MonitoringEnabled ? "Pause monitoring" : "Resume monitoring";
    }

    // ── Quit ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Exits the application cleanly from any context (tray Quit or auto-close smoke path).
    /// </summary>
    internal static void Quit()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
            lt.Shutdown();
    }
}
