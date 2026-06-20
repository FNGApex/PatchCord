using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace PatchCord;

/// <summary>
/// FDA (Full Disk Access / App Management) onboarding UX for PatchCord.Mac.
/// Design §15: when Layer-A bundle writes fail with a permission error (EPERM /
/// UnauthorizedAccessException), the user must grant Full Disk Access (or App
/// Management) once in System Settings.
/// <para>
/// Call <see cref="IsPermissionError"/> to detect the error type, then
/// <see cref="ShowOnboarding"/> to surface the onboarding banner/window.
/// </para>
/// <para>
/// The two relevant System Settings deep-links:
/// <list type="bullet">
///   <item>Full Disk Access: <c>x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles</c></item>
///   <item>App Management:   <c>x-apple.systempreferences:com.apple.preference.security?Privacy_AppBundles</c></item>
/// </list>
/// We surface both; either grant unblocks bundle writes.
/// </para>
/// </summary>
public static class FdaOnboarding
{
    /// <summary>
    /// Deep-link URL for the Full Disk Access System Settings pane.
    /// </summary>
    public const string FdaDeepLink =
        "x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles";

    /// <summary>
    /// Deep-link URL for the App Management System Settings pane.
    /// </summary>
    public const string AppMgmtDeepLink =
        "x-apple.systempreferences:com.apple.preference.security?Privacy_AppBundles";

    /// <summary>
    /// Returns true when <paramref name="ex"/> is a permission/TCC error that indicates
    /// the process lacks the macOS App-Management (or Full Disk Access) TCC grant needed
    /// to write inside a signed .app bundle.
    /// <para>
    /// Matches:
    /// <list type="bullet">
    ///   <item><see cref="UnauthorizedAccessException"/> (POSIX EACCES or EPERM from .NET)</item>
    ///   <item><see cref="IOException"/> whose message contains "Operation not permitted" or "EPERM"
    ///         (the exact string macOS surfaces via TCC for bundle writes)</item>
    /// </list>
    /// </para>
    /// </summary>
    public static bool IsPermissionError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => true,
        IOException io when
            io.Message.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase) ||
            io.Message.Contains("EPERM",                   StringComparison.OrdinalIgnoreCase) ||
            io.Message.Contains("permission",              StringComparison.OrdinalIgnoreCase) => true,
        _ => false,
    };

    /// <summary>
    /// Show the FDA onboarding banner for a permission-blocked patch.
    /// Must be called from the UI thread (or marshals there via Dispatcher.UIThread).
    /// </summary>
    /// <param name="install">The install that failed.</param>
    /// <param name="monitor">MonitorService — used to call ClearFailed on retry.</param>
    /// <param name="cfg">App config (for theme/palette).</param>
    public static void ShowOnboarding(Install install, MonitorService monitor, AppConfig cfg)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
            ShowOnboardingWindow(install, monitor, cfg));
    }

    /// <summary>
    /// Build the Mac-shell <see cref="Action{Install, Exception}"/> that detects permission
    /// errors and shows the onboarding banner.  Pass this as <c>onPatchError</c> to
    /// <see cref="MonitorService.RunOnce"/>.
    /// </summary>
    public static Action<Install, Exception> MakeHandler(MonitorService monitor, AppConfig cfg)
    {
        return (install, ex) =>
        {
            if (!IsPermissionError(ex)) return;
            Log.Write(
                $"FDA onboarding triggered for {install.Name}: {ex.Message}",
                "WARN");
            ShowOnboarding(install, monitor, cfg);
        };
    }

    // ── "Check permissions" affordance ───────────────────────────────────────

    /// <summary>
    /// Show the FDA guidance window without a specific failed install context.
    /// Intended for the user-initiated "Check permissions" button in Options/Status.
    /// Does NOT auto-probe the bundle on startup — we must not trip the system prompt.
    /// </summary>
    public static void ShowGuidance(AppConfig cfg)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
            ShowOnboardingWindow(null, null, cfg));
    }

    // ── Proactive launch gate ─────────────────────────────────────────────────

    /// <summary>
    /// True when a client mod patches the app.asar INSIDE the Discord bundle and so needs
    /// the App-Management TCC grant. Vencord/Equicord are Layer-A bundle writes; BetterDiscord
    /// (Layer-B, app-support index.js) and "none"/"other" are NOT bundle writes.
    /// OpenAsar (also a bundle write) is handled at the call site via <c>cfg.OpenAsar</c>.
    /// </summary>
    public static bool ModNeedsBundleWrite(string? mod) =>
        mod is "vencord" or "equicord";

    // Show the launch gate at most once per process launch.
    private static bool _gateShownThisLaunch;

    /// <summary>
    /// On launch, surface the App-Management gate when an enabled install is ready to patch
    /// a bundle-write mod (Vencord/Equicord/OpenAsar) but isn't injected yet — i.e. the grant
    /// is the likely blocker. Silent for BetterDiscord/none, for already-patched installs, and
    /// for mods that aren't installed yet (the mod-missing CTA owns that case). Fires once.
    /// </summary>
    public static void ShowGateIfNeeded(AppConfig cfg, MonitorService monitor)
    {
        if (_gateShownThisLaunch) return;

        foreach (var inst in cfg.Installs)
        {
            if (!inst.Enabled) continue;

            bool needsBundle = ModNeedsBundleWrite(inst.ClientMod) || cfg.OpenAsar;
            if (!needsBundle) continue;

            // Don't double-warn when the mod simply isn't installed yet — that's the
            // mod-missing CTA's job. (OpenAsar has no installer prerequisite.)
            if (ModNeedsBundleWrite(inst.ClientMod) && !MacAppState.ModInstalled(inst.ClientMod))
                continue;

            InstallState state;
            try { state = MacAppState.GetInstallState(inst, cfg.OpenAsar); }
            catch { continue; }

            // Already satisfied → the grant is working; no nag.
            bool asarSatisfied = !ModNeedsBundleWrite(inst.ClientMod)
                                 || state.InjectedMod == inst.ClientMod;
            bool openAsarSatisfied = !cfg.OpenAsar || state.OpenAsarPresent;
            if (asarSatisfied && openAsarSatisfied) continue;

            _gateShownThisLaunch = true;
            Log.Write(
                $"App-Management launch gate shown for {inst.Name} " +
                $"(mod={inst.ClientMod}, openAsar={cfg.OpenAsar}).",
                "INFO");
            ShowOnboarding(inst, monitor, cfg);
            return;
        }
    }

    // ── Internal window builder ───────────────────────────────────────────────

    private static Window? _current;

    private static void ShowOnboardingWindow(Install? install, MonitorService? monitor, AppConfig cfg)
    {
        // Close any existing onboarding window first.
        try { _current?.Close(); } catch { }
        _current = null;

        var p = MacTheme.Resolve(cfg.Ui.Theme);
        bool isHC = cfg.Ui.Theme == "HighContrast";

        // ── Content ──────────────────────────────────────────────────────────

        var sp = new StackPanel { Spacing = 12, Margin = new Avalonia.Thickness(20) };

        // Title
        sp.Children.Add(new TextBlock
        {
            Text = "PatchCord needs App Management to patch Discord",
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Foreground = MacTheme.Brush(p.Text),
            TextWrapping = TextWrapping.Wrap,
        });

        // Explanation. macOS gates writes inside Discord.app behind the App Management TCC
        // service. There is no in-app Allow prompt — macOS only blocks the write and shows a
        // brief notification — so the grant must be enabled once in System Settings. Because
        // this build is stably signed, that grant then persists across PatchCord updates.
        var bodyText = install != null
            ? $"macOS blocked PatchCord from patching {install.Name} and showed a notification " +
              "instead of a prompt. Discord's app bundle is protected by App Management. " +
              "Enable PatchCord under System Settings → Privacy & Security → App Management — " +
              "you only need to do this once; the grant persists across updates."
            : "To patch Discord's bundle (Vencord, Equicord, or OpenAsar), enable PatchCord under " +
              "System Settings → Privacy & Security → App Management. macOS shows no in-app prompt " +
              "for this — grant it once and PatchCord keeps Discord patched across updates. " +
              "(BetterDiscord needs no permission.)";

        sp.Children.Add(new TextBlock
        {
            Text = bodyText,
            FontSize = 13,
            Foreground = MacTheme.Brush(p.Sub),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480,
        });

        // Primary action: open the App Management pane (the correct, narrowest grant).
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        var btnAppMgmt = MakeButton("Open App Management", p.Accent, p.OnAccent);
        btnAppMgmt.Click += (_, _) => OpenUrl(AppMgmtDeepLink);
        btnRow.Children.Add(btnAppMgmt);

        sp.Children.Add(btnRow);

        // Secondary / advanced: Full Disk Access also unblocks bundle writes but is a much
        // heavier grant, so it's demoted to a small text link rather than a primary button.
        var btnFda = new Button
        {
            Content    = "Advanced: use Full Disk Access instead",
            Background  = Avalonia.Media.Brushes.Transparent,
            Foreground  = MacTheme.Brush(p.Sub),
            Padding     = new Avalonia.Thickness(0, 2),
            FontSize    = 12,
            BorderThickness = new Avalonia.Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        btnFda.Click += (_, _) => OpenUrl(FdaDeepLink);
        sp.Children.Add(btnFda);

        // Re-check / retry button (only shown when we have a failed install + monitor)
        if (install != null && monitor != null)
        {
            var btnRetry = MakeButton("Re-check / retry", p.On, p.OnText);
            btnRetry.Click += (_, _) =>
            {
                monitor.ClearFailed(install.Path);
                Log.Write($"FDA onboarding: cleared back-off for {install.Name}; monitor will retry.", "INFO");
                MacAlert.Show(cfg,
                    $"Re-armed {install.Name}. PatchCord will retry patching on the next monitor tick.",
                    force: true);
                try { _current?.Close(); } catch { }
                _current = null;
            };

            sp.Children.Add(new TextBlock
            {
                Text = "After granting access, click \"Re-check / retry\" and PatchCord will try again.",
                FontSize = 12,
                Foreground = MacTheme.Brush(p.Sub),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 480,
            });
            sp.Children.Add(btnRetry);
        }

        // Dismiss button
        var btnClose = MakeButton("Dismiss", p.GhostHover, p.Sub);
        btnClose.Click += (_, _) => { try { _current?.Close(); } catch { } _current = null; };
        sp.Children.Add(btnClose);

        // ── Window ───────────────────────────────────────────────────────────

        var outer = new Border
        {
            Background      = MacTheme.Brush(p.Bg),
            BorderBrush     = MacTheme.Brush(p.Border),
            BorderThickness = new Avalonia.Thickness(isHC ? 3 : 1),
            CornerRadius    = new Avalonia.CornerRadius(10),
            Padding         = new Avalonia.Thickness(8),
            Child           = sp,
        };

        var w = new Window
        {
            Title         = "PatchCord — Permission Required",
            Content       = outer,
            Width         = 540,
            SizeToContent = SizeToContent.Height,
            CanResize     = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost       = true,
        };
        w.Closed += (_, _) => { if (ReferenceEquals(_current, w)) _current = null; };
        _current = w;
        w.Show();
    }

    private static Button MakeButton(string label, string bg, string fg) => new()
    {
        Content          = label,
        Background       = MacTheme.Brush(bg),
        Foreground       = MacTheme.Brush(fg),
        Padding          = new Avalonia.Thickness(16, 8),
        FontSize         = 13,
        FontWeight       = FontWeight.SemiBold,
        CornerRadius     = new Avalonia.CornerRadius(8),
        HorizontalContentAlignment = HorizontalAlignment.Center,
    };

    // ── URL open helper ───────────────────────────────────────────────────────

    /// <summary>
    /// Opens a URL (including x-apple.systempreferences: deep-links) via the shell.
    /// </summary>
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = "open",
                Arguments       = url,
                UseShellExecute = false,
            });
            Log.Write($"Opened URL: {url}", "INFO");
        }
        catch (Exception ex)
        {
            Log.Write($"Failed to open URL '{url}': {ex.Message}", "WARN");
        }
    }
}
