using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
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
    private bool _firstRenderDone;

    // Monitor loop (B3d).
    private MonitorService? _monitor;
    private DispatcherTimer? _monitorTimer;

    // B3.9: FDA onboarding handler — set in Initialize(), passed to RunOnce().
    private Action<Install, Exception>? _fdaErrorHandler;

    // F5: guards the BetterDiscord "Fix it" self-heal against double-clicks / re-entrancy.
    private bool _bdFixInProgress;

    // Guards the "Get Vencord/Equicord" dist self-fetch against double-clicks / re-entrancy.
    private bool _modGetInProgress;

    // Guards a confirmed mod switch (wipe+update with the monitor paused) against re-entrancy.
    private bool _modSwitchInProgress;

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
        // B3.9: pass the FDA onboarding handler as onPatchError.
        var cfg = MacAppState.Config;
        _monitor = new MonitorService(
            MacAppState.Platform,
            MacAppState.BaseDir,
            msg => MacAlert.Show(cfg, msg));
        // FDA onboarding handler — captured in a local so the closure captures the
        // final _monitor reference. Detects EPERM/UnauthorizedAccess and shows the
        // onboarding window with the Re-check / retry button.
        var monitorRef = _monitor;
        _fdaErrorHandler = (install, ex) =>
        {
            if (!FdaOnboarding.IsPermissionError(ex)) return;
            FdaOnboarding.ShowOnboarding(install, monitorRef, MacAppState.Config);
        };

        // Close-to-tray (B3): the red traffic-light / Cmd-W hides the window and keeps
        // the app + monitor loop alive in the menu-bar tray. App.Quit (tray Quit) sets
        // IsQuitting so a real shutdown is allowed through.
        Closing += (_, e) =>
        {
            if (!App.IsQuitting)
            {
                e.Cancel = true;
                Hide();
            }
        };

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

        // Mod-missing CTA: PatchCord fetches the missing mod's dist itself (no dependence on the
        // mod's own — possibly broken — installer). Only ever fires for vencord/equicord (the only
        // mods ModMissingWarningVisible covers; BetterDiscord has its own "Fix it" path).
        // On failure it falls back to opening the install page so the user isn't stuck.
        BtnGetMod.Click += (_, _) =>
        {
            if (_vm == null || _modGetInProgress) return;
            var mod = _vm.MissingMod;
            if (mod == null) return;

            _modGetInProgress = true;
            ModWarn.IsVisible = false; // hide immediately; download then re-check
            var cfg = MacAppState.Config;
            var label = MonitorService.ModShort(mod);
            MacAlert.Show(cfg, $"Downloading {label}…", force: true);

            // Network download — off the UI thread.
            System.Threading.Tasks.Task.Run(() =>
            {
                Exception? error = null;
                try { MacAppState.DownloadModDist(mod); }
                catch (Exception ex) { error = ex; Log.Write($"Download {mod} dist failed: {ex}", "ERROR"); }

                Dispatcher.UIThread.Post(() =>
                {
                    _modGetInProgress = false;
                    if (error != null)
                    {
                        MacAlert.Show(cfg,
                            $"Couldn't download {label}: {error.Message}. Opening the install page instead…",
                            force: true);
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                                "open", $"\"{_vm.ModMissingGetUrl}\"") { UseShellExecute = false });
                        }
                        catch (Exception ex) { Log.Write($"Open mod URL fallback failed: {ex.Message}", "WARN"); }
                    }
                    else
                    {
                        // Keep patching: ensure monitoring is on so the mod stays injected across updates.
                        if (_vm != null && !_vm.MonitoringEnabled)
                        {
                            _vm.MonitoringEnabled = true;
                            _monitor?.Reset();
                            SetMonitorTimer(true);
                        }
                        MacAppState.Save();
                        MacAlert.Show(cfg,
                            $"{label} downloaded. PatchCord will inject it and keep it patched.",
                            force: true);
                    }
                    UpdateStatusUi();
                    BuildInstallRows();
                });
            });
        };

        // F5: BetterDiscord "Fix it" — repair a malformed BD install, then keep patching.
        BtnFixBd.Click += (_, _) =>
        {
            if (_vm == null || _bdFixInProgress) return;
            var target = _vm.FirstBdFixInstall();
            if (target == null) return;

            _bdFixInProgress = true;
            BdFixWarn.IsVisible = false; // hide immediately; FixBetterDiscord stops/starts Discord
            var cfg = MacAppState.Config;
            MacAlert.Show(cfg, $"Repairing BetterDiscord for {target.Name}…", force: true);

            // The repair downloads (maybe) and stops/starts Discord — do it off the UI thread.
            System.Threading.Tasks.Task.Run(() =>
            {
                Exception? error = null;
                try { MacAppState.FixBetterDiscord(target); }
                catch (Exception ex) { error = ex; Log.Write($"Fix BetterDiscord failed: {ex}", "ERROR"); }

                Dispatcher.UIThread.Post(() =>
                {
                    _bdFixInProgress = false;
                    if (error != null)
                    {
                        MacAlert.Show(cfg, $"Couldn't fix BetterDiscord: {error.Message}", force: true);
                    }
                    else
                    {
                        // Keep patching: ensure monitoring is on so the fix persists across updates.
                        if (_vm != null && !_vm.MonitoringEnabled)
                        {
                            _vm.MonitoringEnabled = true;
                            _monitor?.Reset();
                            SetMonitorTimer(true);
                        }
                        MacAppState.Save();
                        MacAlert.Show(cfg,
                            $"BetterDiscord fixed for {target.Name}. PatchCord will keep it patched.",
                            force: true);
                    }
                    UpdateStatusUi();
                    BuildInstallRows();
                });
            });
        };

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

        // Options — Check permissions (B3.9 user-initiated FDA guidance)
        // Does NOT auto-probe the bundle — just shows guidance + deep-link buttons.
        BtnCheckPermissions.Click += (_, _) =>
        {
            FdaOnboarding.ShowGuidance(MacAppState.Config);
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

        // Defer first full render to after the window is realized (laid out + hit-testable).
        // This fixes blank-until-refresh: controls added to the tree before the window is
        // shown are not measured/hit-testable until after the first layout pass.
        void OnFirstOpened(object? s, EventArgs e)
        {
            Opened -= OnFirstOpened;
            RenderInitial();
        }
        Opened += OnFirstOpened;
    }

    /// <summary>
    /// Runs exactly once, from the <see cref="Window.Opened"/> hook, after the window is
    /// realized. Guarded by <see cref="_firstRenderDone"/> so a second Opened fire (e.g.
    /// restore from minimize) cannot re-run it.
    /// </summary>
    private void RenderInitial()
    {
        if (_firstRenderDone) return;
        _firstRenderDone = true;
        if (_vm == null) return;

        ApplyPalette();
        UpdateStatusUi();
        UpdateOptionsUi();
        BuildInstallRows();
        SwitchTab("status");

        // Start the monitor timer if monitoring is enabled (B3d).
        SetMonitorTimer(_vm.MonitoringEnabled);

        // Phase P: App-Management launch gate. If an enabled install is ready to patch a
        // bundle-write mod (Vencord/Equicord/OpenAsar) but isn't injected yet, the TCC grant
        // is the likely blocker — surface it proactively. Skipped during uitest smoke runs so
        // they auto-close cleanly. macOS shows no in-app prompt for App Management, so this gate
        // (plus the reactive on-patch-error handler) is how the user is guided to grant it.
        if (AutoCloseAfterMs == 0 && _monitor != null)
            FdaOnboarding.ShowGateIfNeeded(MacAppState.Config, _monitor);

        // Uitest: report what rendered — MUST run after BuildInstallRows() so row count > 0.
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
        var fdaHandler = _fdaErrorHandler; // capture for closure
        _monitorTimer.Tick += (_, _) =>
        {
            try
            {
                // B3.9: pass the FDA onboarding handler so permission errors show
                // the onboarding window rather than silently backing off.
                var r = _monitor.RunOnce(cfg, onPatchError: fdaHandler);
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

    /// <summary>
    /// Begin a client-mod switch for <paramref name="installs"/> to <paramref name="toMod"/>: show a
    /// confirm box describing the from→to change, and only on confirm PAUSE the monitor, apply the
    /// wipe-then-update via <see cref="MacAppState.ApplyModSwitch"/> off the UI thread, then resume the
    /// monitor and commit the selection. Cancel leaves config + the bundle untouched. A Layer-A
    /// permission error routes to FDA onboarding (same as the monitor's patch path).
    /// </summary>
    private void BeginModSwitch(IReadOnlyList<Install> installs, string toMod)
    {
        if (_modSwitchInProgress || _modGetInProgress || _bdFixInProgress || _vm == null) return;
        var targets = installs.Where(i => i.Enabled).ToList();
        if (targets.Count == 0) return;

        var cfg = MacAppState.Config;
        var toL = MonitorService.ModShort(toMod);

        // Determine "from" = what is actually injected (first target drives the copy when there are many).
        var fromInjected = MacAppState.GetInstallState(targets[0]).InjectedMod;
        var fromL = MonitorService.ModShort(fromInjected);
        bool virginAll = targets.All(i => MacAppState.GetInstallState(i).InjectedMod == "none");

        string title, body;
        string confirmLabel = toMod == "none" ? "Remove" : "Switch";
        string who = targets.Count == 1 ? targets[0].Name : $"{targets.Count} installs";
        if (toMod == "none")
        {
            title = $"Remove {fromL}?";
            body  = $"This removes {fromL} from {who}, restores Discord to vanilla, and restarts Discord.";
        }
        else if (virginAll)
        {
            title = $"Install {toL}?";
            body  = $"PatchCord will set up {toL} on {who} and restart Discord."
                  + (MacAppState.ModInstalled(toMod) ? "" : $" {toL} will be downloaded first.");
            confirmLabel = "Install";
        }
        else
        {
            title = $"Switch to {toL}?";
            body  = $"PatchCord will remove the current client mod ({fromL}), install {toL} on {who}, and restart Discord."
                  + (MacAppState.ModInstalled(toMod) ? "" : $" {toL} will be downloaded first.");
        }

        ModSwitchConfirm.Show(cfg, title, body, confirmLabel, onConfirm: () =>
        {
            _modSwitchInProgress = true;
            bool wasMonitoring = _vm?.MonitoringEnabled ?? false;
            SetMonitorTimer(false); // PAUSE the background task while wiping+updating
            MacAlert.Show(cfg, toMod == "none" ? $"Removing {fromL}…" : $"Switching to {toL}…", force: true);

            System.Threading.Tasks.Task.Run(() =>
            {
                var results = new List<(Install inst, string summary, Exception? err)>();
                foreach (var inst in targets)
                {
                    try { results.Add((inst, MacAppState.ApplyModSwitch(inst, toMod), null)); }
                    catch (Exception ex) { results.Add((inst, "", ex)); Log.Write($"Mod switch failed for {inst.Name}: {ex}", "ERROR"); }
                }

                Dispatcher.UIThread.Post(() =>
                {
                    _modSwitchInProgress = false;
                    foreach (var (inst, summary, err) in results)
                    {
                        if (err != null)
                        {
                            if (FdaOnboarding.IsPermissionError(err) && _monitor != null)
                                FdaOnboarding.ShowOnboarding(inst, _monitor, cfg);
                            else
                                MacAlert.Show(cfg, $"Couldn't switch {inst.Name}: {err.Message}", force: true);
                        }
                        else
                        {
                            cfg.AddHistory(inst.Name, summary);
                            MacAlert.Show(cfg, $"{inst.Name}: {summary}. Discord restarted.", force: true);
                        }
                    }
                    // Commit the global default to match (installs were set by ApplyModSwitch on success).
                    if (results.Any(r => r.err == null)) cfg.ClientMod = toMod;
                    MacAppState.Save();
                    _monitor?.ClearAllFailed();
                    if (wasMonitoring) SetMonitorTimer(true); // RESUME the background task
                    // RefreshInstallRows (not BuildInstallRows) RE-COMPUTES each install's state from
                    // disk — BuildInstallRows alone reuses the now-stale pre-switch snapshot, which is
                    // why the row showed "nothing" until a manual Refresh.
                    RefreshInstallRows();
                    UpdateStatusUi();
                    UpdateOptionsUi();
                    // Discord was just relaunched and may not be "running" yet at this instant; the
                    // patched badge already shows (disk state), but re-poll once after it settles so the
                    // running indicator updates promptly instead of waiting for the next monitor tick.
                    Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                    {
                        if (_modSwitchInProgress) return; // a newer switch is mid-flight; let it own the UI
                        RefreshInstallRows();
                        UpdateStatusUi();
                    }, TimeSpan.FromSeconds(3));
                });
            });
        });
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    private void SwitchTab(string tab)
    {
        _activeTab = tab;
        bool isStatus = tab == "status";
        StatusScroll.IsVisible  = isStatus;
        OptionsScroll.IsVisible = !isStatus;

        // Drive active/inactive appearance via Classes so the TabButton ControlTheme
        // handles foreground + underline — no direct Foreground fight with Fluent.
        if (isStatus)
        {
            TabStatusBtn.Classes.Add("active");
            TabOptionsBtn.Classes.Remove("active");
        }
        else
        {
            TabOptionsBtn.Classes.Add("active");
            TabStatusBtn.Classes.Remove("active");
        }
    }

    // ── Status tab ────────────────────────────────────────────────────────────

    private void UpdateStatusUi()
    {
        if (_vm == null) return;
        var p = CurrentPalette();
        bool on = _vm.MonitoringEnabled;

        StatusDot.Background = MacTheme.Brush(on ? p.On : "#80848E");
        StatusText.Text = _vm.MonitoringStatusText;
        // StatusText.Foreground and StatusSub.Foreground are DynamicResource in XAML — no repaint needed.
        StatusSub.Text = _vm.MonitoringSubText;

        BtnToggle.Content = _vm.ToggleButtonLabel;
        // Swap ControlTheme: AccentPill (blue "Turn Off") when monitoring is on;
        // OnPill (green "Turn On") when monitoring is off.
        // Both themes use DynamicResource brushes, so live palette switching is free.
        // Use TryFindResource so Avalonia walks the full resource chain (including Styles.Resources).
        if (Application.Current!.TryFindResource(on ? "AccentPill" : "OnPill", out var themeObj)
            && themeObj is ControlTheme ct)
            BtnToggle.Theme = ct;

        // Mod missing warning
        ModWarn.IsVisible = _vm.ModMissingWarningVisible;
        if (_vm.ModMissingWarningVisible)
        {
            ModWarnText.Text = _vm.ModMissingWarningText;
            BtnGetMod.Content = _vm.ModMissingGetLabel; // B2: reflect the actual missing mod
        }

        // BetterDiscord malformed → "Fix it" self-heal banner (F5).
        BdFixWarn.IsVisible = _vm.BdFixVisible && !_bdFixInProgress;
        if (BdFixWarn.IsVisible)
            BdFixText.Text = _vm.BdFixText;

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
                },
                onModChange: (vm, mod) =>
                {
                    // A switch is a confirmed, monitor-paused wipe-then-update — not an instant
                    // config write. BeginModSwitch shows the confirm box and only commits on success.
                    var inst = MacAppState.Config.Installs.FirstOrDefault(i => i.Path == vm.Path);
                    if (inst != null && inst.ClientMod != mod)
                        BeginModSwitch(new[] { inst }, mod);
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
                // Confirmed, monitor-paused wipe-then-update for all enabled installs; commits on success.
                if (_vm.ClientMod != capturedMod)
                    BeginModSwitch(MacAppState.Config.Installs, capturedMod);
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

        // U1: Replace the brush VALUES of the 14 DynamicResource keys so every
        // ControlTheme consumer + static text with {DynamicResource ...} repaints
        // automatically without any per-control code.
        SetBrush("Bg",          p.Bg);
        SetBrush("Card",        p.Card);
        SetBrush("Card2",       p.Card2);
        SetBrush("Border",      p.Border);
        SetBrush("Text",        p.Text);
        SetBrush("Sub",         p.Sub);
        SetBrush("Accent",      p.Accent);
        SetBrush("AccentHover", p.AccentHover);
        SetBrush("OnAccent",    p.OnAccent);
        SetBrush("Ghost",       p.Ghost);
        SetBrush("GhostHover",  p.GhostHover);
        SetBrush("On",          p.On);
        SetBrush("OnText",      p.OnText);
        SetBrush("Scroll",      p.Scroll);

        // Apply window background (the root Border is not a DynamicResource consumer
        // — it pre-dates this slice and we keep it imperative so the window bg is
        // always correct even before the first Styles tick).
        if (Content is Border border)
        {
            border.Background = MacTheme.Brush(p.Bg);
            border.BorderBrush = MacTheme.Brush(p.Border);
            border.BorderThickness = new Thickness(1);
        }
    }

    /// <summary>
    /// Mutate the Color of the named SolidColorBrush in the global resource chain
    /// so DynamicResource consumers repaint automatically.
    /// TryFindResource walks Styles.Resources (where the brushes are seeded) as well as
    /// Application.Current.Resources, so we find the brush on the first call and mutate
    /// it in place — no new brush objects, no re-registration needed.
    /// </summary>
    private static void SetBrush(string key, string hex)
    {
        if (Application.Current?.TryFindResource(key, out var existing) == true
            && existing is SolidColorBrush brush)
        {
            brush.Color = MacTheme.ParseColor(hex);
        }
        else
        {
            // Fallback: add directly to Application.Current.Resources so the key
            // is found on subsequent calls.
            if (Application.Current != null)
                Application.Current.Resources[key] = new SolidColorBrush(MacTheme.ParseColor(hex));
        }
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

        // U1 uitest: verify palette brush keys are present in Application resources
        // and BtnToggle carries the expected ControlTheme key.
        RunU1UiTests();

        // U4/U5 uitest: verify first-paint row population (no Refresh) and palette brush wiring.
        RunU4U5UiTests();

        // B3c/B3.4 uitest: exercise tray actions, alert, and live theme switching.
        RunB3UiTests();
    }

    private void RunU1UiTests()
    {
        // U1: verify 14 palette brush keys are findable (Styles.Resources walks via TryFindResource).
        // After ApplyPalette() they are mutated in place, so they reflect the active theme.
        var brushKeys = new[] { "Bg", "Card", "Card2", "Border", "Text", "Sub",
                                "Accent", "AccentHover", "OnAccent", "Ghost",
                                "GhostHover", "On", "OnText", "Scroll" };
        int missing = 0;
        foreach (var key in brushKeys)
        {
            bool found = Application.Current?.TryFindResource(key, out var v) == true
                         && v is SolidColorBrush;
            if (!found) { Console.WriteLine($"[uitest] U1 MISSING brush key: {key}"); missing++; }
        }
        Console.WriteLine($"[uitest] U1 Palette brushes: {brushKeys.Length - missing}/{brushKeys.Length} present");

        // U1: BtnToggle.Theme should be set (non-null) — AccentPill when monitoring is on.
        bool toggleOk = BtnToggle.Theme != null;
        Console.WriteLine($"[uitest] U1 BtnToggle.Theme set: {toggleOk}");

        // U1.1: SpaceGrotesk FontFamily resource present.
        bool fontOk = Application.Current?.TryFindResource("SpaceGrotesk", out var fontVal) == true
                      && fontVal is FontFamily;
        Console.WriteLine($"[uitest] U1.1 SpaceGrotesk font resource: {fontOk}");

        bool u1Pass = missing == 0 && toggleOk && fontOk;
        Console.WriteLine($"[uitest] U1 PASS: {u1Pass}");
    }

    private void RunU4U5UiTests()
    {
        if (_vm == null) return;

        // ── U6.1 — first-paint rows populated, no Refresh ────────────────────
        // NOTE: RefreshInstallRows()/BtnRefresh.Click was NOT invoked on this path.
        // RenderInitial() calls BuildInstallRows() before ReportUiTestInfo(), so
        // the visual tree is already populated here purely from the Opened hook.

        int installRowCount = 0;
        bool placeholderPresent = false;
        foreach (var child in InstallList.Children)
        {
            if (child is InstallRow)
                installRowCount++;
            else if (child is TextBlock)
                placeholderPresent = true;
        }

        bool hasChildren = InstallList.Children.Count > 0;
        bool u61Pass;
        if (_vm.Installs.Count > 0)
            // VM has installs → expect rendered InstallRow controls (not just a placeholder).
            u61Pass = _firstRenderDone && installRowCount > 0;
        else
            // VM has no installs → expect the "no installs" TextBlock placeholder.
            u61Pass = _firstRenderDone && placeholderPresent;

        Console.WriteLine($"[uitest] U6.1 first-paint rows (no Refresh): {installRowCount} InstallRow(s), firstRenderDone={_firstRenderDone}");
        Console.WriteLine($"[uitest] U6.1 PASS: {u61Pass}");

        // ── U6.2 — controls resolve palette brushes, not Fluent defaults ─────
        var p = CurrentPalette();

        // BtnToggle.Background: AccentPill → Accent hex; OnPill → On hex.
        bool on = _vm.MonitoringEnabled;
        string expectedToggleHex = on ? p.Accent : p.On;
        Color expectedToggleColor = MacTheme.ParseColor(expectedToggleHex);

        bool toggleBrushOk = false;
        string toggleActualStr = "(not SolidColorBrush)";
        if (BtnToggle.Background is SolidColorBrush toggleBrush)
        {
            toggleActualStr = toggleBrush.Color.ToString();
            toggleBrushOk = toggleBrush.Color == expectedToggleColor;
        }
        else
        {
            Console.WriteLine($"[uitest] U6.2 BtnToggle.Background is not a SolidColorBrush — type={BtnToggle.Background?.GetType().Name ?? "null"}");
        }
        Console.WriteLine($"[uitest] U6.2 BtnToggle.Background={toggleActualStr} expected={expectedToggleHex} match={toggleBrushOk}");

        // StatusSub.Foreground: should equal palette Sub (set via {DynamicResource Sub} in XAML).
        string expectedSubHex = p.Sub;
        Color expectedSubColor = MacTheme.ParseColor(expectedSubHex);

        bool subBrushOk = false;
        string subActualStr = "(not SolidColorBrush)";
        if (StatusSub.Foreground is SolidColorBrush subBrush)
        {
            subActualStr = subBrush.Color.ToString();
            subBrushOk = subBrush.Color == expectedSubColor;
        }
        else
        {
            Console.WriteLine($"[uitest] U6.2 StatusSub.Foreground is not a SolidColorBrush — type={StatusSub.Foreground?.GetType().Name ?? "null"}");
        }
        Console.WriteLine($"[uitest] U6.2 StatusSub.Foreground={subActualStr} expected={expectedSubHex} match={subBrushOk}");

        bool u62Pass = toggleBrushOk && subBrushOk;
        Console.WriteLine($"[uitest] U6.2 PASS: {u62Pass}");
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
