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
            Text = "macOS blocked PatchCord from modifying Discord",
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Foreground = MacTheme.Brush(p.Text),
            TextWrapping = TextWrapping.Wrap,
        });

        // Explanation
        var bodyText = install != null
            ? $"macOS prevented PatchCord from patching {install.Name}. " +
              "The bundle is protected by macOS App Management — you need to grant either " +
              "\"App Management\" or \"Full Disk Access\" to PatchCord once in System Settings."
            : "To patch Discord's bundle, PatchCord needs \"App Management\" (or \"Full Disk Access\") " +
              "in System Settings → Privacy & Security. Grant it once and PatchCord can keep Discord patched.";

        sp.Children.Add(new TextBlock
        {
            Text = bodyText,
            FontSize = 13,
            Foreground = MacTheme.Brush(p.Sub),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480,
        });

        // Open System Settings buttons
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        var btnAppMgmt = MakeButton("Open App Management", p.Accent, p.OnAccent);
        btnAppMgmt.Click += (_, _) => OpenUrl(AppMgmtDeepLink);
        btnRow.Children.Add(btnAppMgmt);

        var btnFda = MakeButton("Open Full Disk Access", p.GhostHover, p.Text);
        btnFda.Click += (_, _) => OpenUrl(FdaDeepLink);
        btnRow.Children.Add(btnFda);

        sp.Children.Add(btnRow);

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
