using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace PatchCord;

/// <summary>
/// Themed modal confirm box shown before a client-mod switch is applied. Occupies the same role the
/// download/onboarding box did: the user confirms the wipe-then-update before PatchCord stops Discord
/// and rewrites the bundle. Confirm runs <paramref name="onConfirm"/>; Cancel just closes.
/// Mirrors <see cref="FdaOnboarding"/>'s window styling so the two boxes look consistent.
/// </summary>
public static class ModSwitchConfirm
{
    private static Window? _current;

    /// <summary>
    /// Show the confirm box. Marshals to the UI thread. <paramref name="onConfirm"/> fires only when
    /// the user clicks Confirm; nothing fires on Cancel/close.
    /// </summary>
    public static void Show(AppConfig cfg, string title, string body, string confirmLabel, Action onConfirm)
    {
        Dispatcher.UIThread.InvokeAsync(() => ShowWindow(cfg, title, body, confirmLabel, onConfirm));
    }

    private static void ShowWindow(AppConfig cfg, string title, string body, string confirmLabel, Action onConfirm)
    {
        try { _current?.Close(); } catch { }
        _current = null;

        var p = MacTheme.Resolve(cfg.Ui.Theme);
        bool isHC = cfg.Ui.Theme == "HighContrast";

        var sp = new StackPanel { Spacing = 12, Margin = new Avalonia.Thickness(20) };

        sp.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Foreground = MacTheme.Brush(p.Text),
            TextWrapping = TextWrapping.Wrap,
        });

        sp.Children.Add(new TextBlock
        {
            Text = body,
            FontSize = 13,
            Foreground = MacTheme.Brush(p.Sub),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
        });

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Avalonia.Thickness(0, 4, 0, 0) };

        var btnConfirm = MakeButton(confirmLabel, p.Accent, p.OnAccent);
        btnConfirm.Click += (_, _) =>
        {
            try { _current?.Close(); } catch { }
            _current = null;
            try { onConfirm(); } catch (Exception ex) { Log.Write($"Mod-switch confirm handler threw: {ex.Message}", "ERROR"); }
        };

        var btnCancel = MakeButton("Cancel", p.GhostHover, p.Sub);
        btnCancel.Click += (_, _) => { try { _current?.Close(); } catch { } _current = null; };

        btnRow.Children.Add(btnConfirm);
        btnRow.Children.Add(btnCancel);
        sp.Children.Add(btnRow);

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
            Title         = "PatchCord — Confirm",
            Content       = outer,
            Width         = 500,
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
}
