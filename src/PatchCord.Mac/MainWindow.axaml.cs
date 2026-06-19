using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace PatchCord;

/// <summary>
/// Main window for PatchCord.Mac — two-tab (Status + Options).
/// B3d: drives the MonitorService reconciliation loop via an Avalonia DispatcherTimer.
/// </summary>
public sealed partial class MainWindow : Window
{
    // When non-zero, the window auto-closes after this many milliseconds.
    // Set by Program.Main when --mac-uitest is passed, to keep the smoke run non-blocking.
    internal static int AutoCloseAfterMs { get; set; }

    private MainViewModel? _vm;
    private string _activeTab = "status";

    // Monitor loop (B3d).
    private MonitorService? _monitor;
    private DispatcherTimer? _monitorTimer;

    public MainWindow()
    {
        InitializeComponent();

        if (AutoCloseAfterMs > 0)
        {
            Console.WriteLine($"[uitest] MainWindow opened — auto-close in {AutoCloseAfterMs} ms.");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutoCloseAfterMs) };
            timer.Tick += (_, _) =>
            {
                Console.WriteLine("[uitest] Auto-close timer fired — calling Quit().");
                timer.Stop();
                App.Quit();
            };
            timer.Start();
        }
    }

    /// <summary>
    /// Initialize the window with a loaded view-model.
    /// Called from App.OnFrameworkInitializationCompleted after MacAppState is loaded.
    /// </summary>
    internal void Initialize(MainViewModel vm)
    {
        _vm = vm;

        // Build the monitor service (B3d).
        var cfg = MacAppState.Config;
        _monitor = new MonitorService(
            MacAppState.Platform,
            MacAppState.BaseDir,
            msg => MacAlert.Show(cfg, msg));

        // Tab switching
        TabStatusBtn.Click  += (_, _) => SwitchTab("status");
        TabOptionsBtn.Click += (_, _) => SwitchTab("options");

        // Monitoring toggle: flip the flag, persist, reset monitor state, restart the timer.
        BtnToggle.Click += (_, _) =>
        {
            if (_vm == null) return;
            _vm.MonitoringEnabled = !_vm.MonitoringEnabled;
            MacAppState.Save();
            _monitor?.Reset();
            SetMonitorTimer(_vm.MonitoringEnabled);
            UpdateStatusUi();
        };

        // Refresh button
        BtnRefresh.Click += (_, _) => RefreshInstallRows();

        // Options — OpenAsar toggle
        BtnOpenAsar.Click += (_, _) =>
        {
            if (_vm == null) return;
            _vm.OpenAsar = !_vm.OpenAsar;
            UpdateOptionsUi();
        };

        // Options — Run at startup toggle
        BtnStartup.Click += (_, _) =>
        {
            if (_vm == null) return;
            _vm.RunAtLogin = !_vm.RunAtLogin;
            UpdateOptionsUi();
        };

        // Options — Notifications toggle
        BtnNotifyToggle.Click += (_, _) =>
        {
            if (_vm == null) return;
            _vm.NotificationsEnabled = !_vm.NotificationsEnabled;
            UpdateOptionsUi();
        };

        // Interval slider
        SliderInterval.Value = vm.IntervalSeconds;
        LblInterval.Text = vm.IntervalLabel;
        SliderInterval.ValueChanged += (_, e) =>
        {
            if (_vm == null) return;
            _vm.IntervalSeconds = (int)e.NewValue;
            LblInterval.Text = _vm.IntervalLabel;
        };

        // Notify scale slider
        SliderNotifyScale.Value = vm.NotifyScale;
        LblNotifyScale.Text = vm.NotifyScaleLabel;
        SliderNotifyScale.ValueChanged += (_, e) =>
        {
            if (_vm == null) return;
            _vm.NotifyScale = e.NewValue;
            LblNotifyScale.Text = _vm.NotifyScaleLabel;
        };

        // Notify duration slider
        SliderNotifyDur.Value = vm.NotifyDurationSec;
        LblNotifyDur.Text = vm.NotifyDurationLabel;
        SliderNotifyDur.ValueChanged += (_, e) =>
        {
            if (_vm == null) return;
            _vm.NotifyDurationSec = (int)e.NewValue;
            LblNotifyDur.Text = _vm.NotifyDurationLabel;
        };

        // Build client mod chooser cards
        BuildClientModPanel();

        // Build theme chips
        BuildThemeChips();

        // Build notify style chips
        BuildStyleChips();

        // Initial render
        ApplyPalette();
        UpdateStatusUi();
        UpdateOptionsUi();
        BuildInstallRows();
        SwitchTab("status");

        // Start the monitor timer if monitoring is enabled (B3d).
        SetMonitorTimer(_vm.MonitoringEnabled);

        // Uitest: report what rendered
        if (AutoCloseAfterMs > 0)
            ReportUiTestInfo();
    }

    // ── Monitor loop (B3d) ────────────────────────────────────────────────────

    /// <summary>
    /// Start or stop the monitor timer. Interval = max(5, cfg.IntervalSeconds).
    /// A tick calls <see cref="MonitorService.RunOnce"/>, saves if recorded, refreshes UI.
    /// </summary>
    private void SetMonitorTimer(bool enabled)
    {
        _monitorTimer?.Stop();
        _monitorTimer = null;
        if (!enabled || _monitor == null) return;

        var cfg = MacAppState.Config;
        int iv = Math.Max(5, cfg.IntervalSeconds);
        _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(iv) };
        _monitorTimer.Tick += (_, _) =>
        {
            try
            {
                var r = _monitor.RunOnce(cfg);
                if (r.Recorded) MacAppState.Save();
                UpdateStatusUi();
                BuildInstallRows();
            }
            catch (Exception ex)
            {
                Log.Write($"Monitor error: {ex.Message}", "ERROR");
            }
        };
        _monitorTimer.Start();
        Log.Write($"Monitor timer started (interval={iv}s).", "INFO");
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    private void SwitchTab(string tab)
    {
        _activeTab = tab;
        bool isStatus = tab == "status";
        StatusScroll.IsVisible  = isStatus;
        OptionsScroll.IsVisible = !isStatus;

        var p = CurrentPalette();
        TabStatusBtn.Foreground  = MacTheme.Brush(isStatus  ? p.Text : p.Sub);
        TabOptionsBtn.Foreground = MacTheme.Brush(!isStatus ? p.Text : p.Sub);
    }

    // ── Status tab ────────────────────────────────────────────────────────────

    private void UpdateStatusUi()
    {
        if (_vm == null) return;
        var p = CurrentPalette();
        bool on = _vm.MonitoringEnabled;

        StatusDot.Background = MacTheme.Brush(on ? p.On : "#80848E");
        StatusText.Text = _vm.MonitoringStatusText;
        StatusText.Foreground = MacTheme.Brush(p.Text);
        StatusSub.Text = _vm.MonitoringSubText;
        StatusSub.Foreground = MacTheme.Brush(p.Sub);

        BtnToggle.Content = _vm.ToggleButtonLabel;
        BtnToggle.Background = MacTheme.Brush(on ? p.Accent : p.On);
        BtnToggle.Foreground = MacTheme.Brush(on ? p.OnAccent : p.OnText);

        // Mod missing warning
        ModWarn.IsVisible = _vm.ModMissingWarningVisible;
        if (_vm.ModMissingWarningVisible)
            ModWarnText.Text = _vm.ModMissingWarningText;

        // History
        BuildHistory();
    }

    private void BuildHistory()
    {
        if (_vm == null) return;
        var p = CurrentPalette();
        HistoryPanel.Children.Clear();
        if (_vm.History.Count == 0)
        {
            HistoryPanel.Children.Add(new TextBlock
            {
                Text = "No re-patches yet.",
                Foreground = MacTheme.Brush(p.Sub),
                FontSize = 12,
                Margin = new Avalonia.Thickness(2, 0, 0, 8),
            });
            return;
        }
        foreach (var e in _vm.History.Take(6))
        {
            var tb = new TextBlock
            {
                FontSize = 12,
                Margin = new Avalonia.Thickness(2, 0, 0, 7),
                Text = $"{Ago(e.When)}   {e.Install}  ·  {e.Summary}",
                Foreground = MacTheme.Brush(p.Sub),
            };
            HistoryPanel.Children.Add(tb);
        }
    }

    private static string Ago(DateTime utc)
    {
        var s = (DateTime.UtcNow - utc).TotalSeconds;
        if (s < 60) return "just now";
        if (s < 3600) return $"{(int)(s / 60)}m ago";
        if (s < 86400) return $"{(int)(s / 3600)}h ago";
        if (s < 7 * 86400) return $"{(int)(s / 86400)}d ago";
        return utc.ToLocalTime().ToString("MMM d");
    }

    // ── Install rows ──────────────────────────────────────────────────────────

    private void BuildInstallRows()
    {
        if (_vm == null) return;
        var p = CurrentPalette();
        InstallList.Children.Clear();

        if (_vm.Installs.Count == 0)
        {
            InstallList.Children.Add(new TextBlock
            {
                Text = "No Discord installs found. Click Refresh to detect.",
                Foreground = MacTheme.Brush(p.Sub),
                FontSize = 12,
                Margin = new Avalonia.Thickness(2, 0, 0, 8),
            });
            return;
        }

        foreach (var rowVm in _vm.Installs)
        {
            var row = new InstallRow();
            row.Bind(rowVm, p,
                onToggle: vm =>
                {
                    _vm.ToggleInstallEnabled(vm);
                    _monitor?.ClearFailed(vm.Path); // re-arm patching for this install
                    BuildInstallRows();
                },
                onRemove: vm =>
                {
                    _vm.RemoveInstall(vm.Path);
                    BuildInstallRows();
                });
            InstallList.Children.Add(row);
        }
    }

    private void RefreshInstallRows()
    {
        _vm?.RebuildInstallRows();
        BuildInstallRows();
    }

    // ── Options tab ───────────────────────────────────────────────────────────

    private void UpdateOptionsUi()
    {
        if (_vm == null) return;
        var p = CurrentPalette();

        // OpenAsar
        bool oa = _vm.OpenAsar;
        BtnOpenAsar.Content = _vm.OpenAsarToggleLabel;
        BtnOpenAsar.Background = MacTheme.Brush(oa ? p.On : p.GhostHover);
        BtnOpenAsar.Foreground = MacTheme.Brush(oa ? p.OnText : p.Text);

        // Run at startup
        bool su = _vm.RunAtLogin;
        BtnStartup.Content = _vm.RunAtLoginToggleLabel;
        BtnStartup.Background = MacTheme.Brush(su ? p.On : p.GhostHover);
        BtnStartup.Foreground = MacTheme.Brush(su ? p.OnText : p.Text);

        // Notifications
        bool n = _vm.NotificationsEnabled;
        BtnNotifyToggle.Content = _vm.NotifyToggleLabel;
        BtnNotifyToggle.Background = MacTheme.Brush(n ? p.On : p.GhostHover);
        BtnNotifyToggle.Foreground = MacTheme.Brush(n ? p.OnText : p.Text);

        // Slider labels
        LblNotifyScale.Text = _vm.NotifyScaleLabel;
        LblNotifyDur.Text   = _vm.NotifyDurationLabel;
        LblInterval.Text    = _vm.IntervalLabel;

        // Chip selections
        UpdateChipSelection(ThemePanel, _vm.Theme, p);
        UpdateChipSelection(StylePanel, _vm.NotifyStyle, p);
        UpdateClientModSelection();
    }

    private void BuildClientModPanel()
    {
        ClientModPanel.Children.Clear();
        var p = CurrentPalette();
        foreach (var (mod, title, desc) in MainViewModel.ClientModItems)
        {
            var card = new Border
            {
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(16, 12, 16, 12),
                BorderThickness = new Avalonia.Thickness(1),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Tag = mod,
            };
            var sp = new StackPanel { MaxWidth = 620, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            sp.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold,
                                            Foreground = MacTheme.Brush(p.Text) });
            sp.Children.Add(new TextBlock { Text = desc, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                                            Margin = new Avalonia.Thickness(0, 3, 0, 0),
                                            Foreground = MacTheme.Brush(p.Sub) });
            card.Child = sp;
            var capturedMod = mod;
            card.PointerPressed += (_, _) =>
            {
                if (_vm == null) return;
                _vm.ClientMod = capturedMod;
                _monitor?.ClearAllFailed(); // re-arm patching after changing the mod for all installs
                UpdateOptionsUi();
                BuildInstallRows();
            };
            ClientModPanel.Children.Add(card);
        }
        UpdateClientModSelection();
    }

    private void UpdateClientModSelection()
    {
        if (_vm == null) return;
        var p = CurrentPalette();
        foreach (var child in ClientModPanel.Children)
        {
            if (child is not Border b) continue;
            bool sel = (string?)b.Tag == _vm.ClientMod;
            b.Background   = MacTheme.Brush(sel ? p.GhostHover : p.Card);
            b.BorderBrush  = MacTheme.Brush(sel ? p.Accent : "Transparent");
        }
    }

    private void BuildThemeChips()
    {
        ThemePanel.Children.Clear();
        foreach (var key in MacTheme.Keys)
        {
            var chip = MakeChip(key, MacTheme.Palettes[key].Label);
            var capturedKey = key;
            chip.PointerPressed += (_, _) =>
            {
                if (_vm == null) return;
                _vm.Theme = capturedKey;
                ApplyPalette();
                UpdateOptionsUi();
                UpdateStatusUi();
                BuildInstallRows();
            };
            ThemePanel.Children.Add(chip);
        }
    }

    private void BuildStyleChips()
    {
        StylePanel.Children.Clear();
        var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
        foreach (var style in MainViewModel.NotifyStyles)
        {
            var chip = MakeChip(style, ti.ToTitleCase(style));
            var capturedStyle = style;
            chip.PointerPressed += (_, _) =>
            {
                if (_vm == null) return;
                _vm.NotifyStyle = capturedStyle;
                UpdateChipSelection(StylePanel, capturedStyle, CurrentPalette());
            };
            StylePanel.Children.Add(chip);
        }
    }

    private static Border MakeChip(string tag, string label)
    {
        return new Border
        {
            Tag = tag,
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(15, 8, 15, 8),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold },
        };
    }

    private void UpdateChipSelection(StackPanel panel, string selectedTag, MacPalette p)
    {
        foreach (var child in panel.Children)
        {
            if (child is not Border b) continue;
            bool sel = (string?)b.Tag == selectedTag;
            b.BorderBrush     = MacTheme.Brush(sel ? p.Accent : p.Border);
            b.BorderThickness = new Avalonia.Thickness(sel ? 2 : 1);
            b.Background      = MacTheme.Brush(sel ? p.GhostHover : p.Ghost);
            if (b.Child is TextBlock tb)
                tb.Foreground = MacTheme.Brush(p.Text);
        }
    }

    // ── Theme application ─────────────────────────────────────────────────────

    private MacPalette CurrentPalette() =>
        MacTheme.Resolve(_vm?.Theme);

    private void ApplyPalette()
    {
        var p = CurrentPalette();
        // Apply window background
        if (Content is Border border)
        {
            border.Background = MacTheme.Brush(p.Bg);
            border.BorderBrush = MacTheme.Brush(p.Border);
            border.BorderThickness = new Avalonia.Thickness(1);
        }
        // Apply text foregrounds that are always visible
        StatusText.Foreground = MacTheme.Brush(p.Text);
        StatusSub.Foreground  = MacTheme.Brush(p.Sub);
    }

    // ── Public API for App.axaml.cs (tray actions) ───────────────────────────

    /// <summary>
    /// Refresh the Status tab UI from the current VM state.
    /// Called by App when the tray monitoring toggle fires.
    /// </summary>
    internal void RefreshStatusUi() => UpdateStatusUi();

    // ── Uitest reporting ──────────────────────────────────────────────────────

    private void ReportUiTestInfo()
    {
        if (_vm == null) return;
        Console.WriteLine("[uitest] Window initialized with two-tab layout.");
        Console.WriteLine($"[uitest] Active tab: {_activeTab}");
        Console.WriteLine($"[uitest] Monitoring: {_vm.MonitoringStatusText}");
        Console.WriteLine($"[uitest] Install rows: {_vm.Installs.Count}");
        foreach (var row in _vm.Installs)
            Console.WriteLine($"[uitest]   install: name={row.Name} enabled={row.Enabled} mod={row.ModLabel} badge={row.PrimaryBadgeText} oaSar={row.OpenAsarBadgeVisible}");
        Console.WriteLine($"[uitest] Options: clientMod={_vm.ClientMod} openAsar={_vm.OpenAsar} runAtLogin={_vm.RunAtLogin} interval={_vm.IntervalSeconds}s");
        Console.WriteLine($"[uitest] Theme: {_vm.Theme}");
        Console.WriteLine($"[uitest] Config path: {MacAppState.ConfigFile}");

        // B3c/B3.4 uitest: exercise tray actions, alert, and live theme switching.
        RunB3UiTests();
    }

    private void RunB3UiTests()
    {
        if (_vm == null) return;

        // B3.3: Verify tray items are wired (header and toggle label set).
        var trayHeader = App.TrayHeaderItem?.Header ?? "(null)";
        var trayToggle = App.TrayToggleItem?.Header ?? "(null)";
        Console.WriteLine($"[uitest] B3.3 Tray header: '{trayHeader}'");
        Console.WriteLine($"[uitest] B3.3 Tray toggle label: '{trayToggle}'");
        bool trayOk = trayHeader.Contains("PatchCord") && (trayToggle == "Pause monitoring" || trayToggle == "Resume monitoring");
        Console.WriteLine($"[uitest] B3.3 Tray wired: {trayOk}");

        // B3.3: Simulate monitoring toggle via tray (flip + flip back).
        bool before = _vm.MonitoringEnabled;
        _vm.MonitoringEnabled = !before;
        if (App.Current is App app) { app.UpdateTrayHeader(); app.UpdateTrayToggleLabel(); }
        var toggledLabel = App.TrayToggleItem?.Header ?? "(null)";
        _vm.MonitoringEnabled = before;
        if (App.Current is App app2) { app2.UpdateTrayHeader(); app2.UpdateTrayToggleLabel(); }
        Console.WriteLine($"[uitest] B3.3 Tray toggle flip: before={before} toggled-label='{toggledLabel}' restored={_vm.MonitoringEnabled == before}");

        // B3.4a: Show an alert (auto-dismisses on its own timer).
        var cfg = MacAppState.Config;
        Console.WriteLine("[uitest] B3.4a Showing test Alert (bar style, auto-dismiss)...");
        MacAlert.Show(cfg, "B3.4a uitest: PatchCord alert banner.", force: true);
        Console.WriteLine("[uitest] B3.4a Alert.Show() called — banner visible top-left.");

        // B3.4b: Cycle through all 4 themes and verify palette is applied.
        Console.WriteLine("[uitest] B3.4b Live theme switching — cycling all 4 themes...");
        var originalTheme = _vm.Theme;
        foreach (var key in MacTheme.Keys)
        {
            _vm.Theme = key;
            ApplyPalette();
            UpdateOptionsUi();
            UpdateStatusUi();
            BuildInstallRows();
            var p = CurrentPalette();
            Console.WriteLine($"[uitest] B3.4b Theme '{key}' applied — Bg={p.Bg} Accent={p.Accent}");
        }
        // Restore original theme.
        _vm.Theme = originalTheme;
        ApplyPalette();
        UpdateOptionsUi();
        UpdateStatusUi();
        BuildInstallRows();
        Console.WriteLine($"[uitest] B3.4b Theme restored to '{originalTheme}'.");
        Console.WriteLine("[uitest] B3.4b Live theme switching complete — all 4 themes repainted.");
    }
}
