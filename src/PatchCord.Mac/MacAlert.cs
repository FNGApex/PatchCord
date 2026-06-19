using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace PatchCord;

/// <summary>
/// Top-left banner notifications for PatchCord.Mac.
/// Ports the WPF Alert.cs to an Avalonia borderless Window.
/// Supports the same 4 styles (bar / solid / minimal / outline) and UiConfig
/// scale + duration. Auto-dismisses via DispatcherTimer.
/// </summary>
public static class MacAlert
{
    private static Window? _current;

    /// <summary>
    /// Show a notification banner. Mirrors WPF Alert.Show(cfg, message, force).
    /// </summary>
    /// <param name="cfg">AppConfig with Ui.NotifyStyle / NotifyScale / NotifyDurationSec / NotificationsEnabled.</param>
    /// <param name="message">Text to display.</param>
    /// <param name="force">If true, show even when NotificationsEnabled=false (used by Test notification).</param>
    public static void Show(AppConfig cfg, string message, bool force = false)
    {
        if (!force && !cfg.Ui.NotificationsEnabled) return;

        // Must run on the UI thread.
        Dispatcher.UIThread.InvokeAsync(() => ShowOnUiThread(cfg, message));
    }

    private static void ShowOnUiThread(AppConfig cfg, string message)
    {
        try
        {
            // Close any existing banner first.
            if (_current != null)
            {
                try { _current.Close(); } catch { }
                _current = null;
            }

            var ui = cfg.Ui;
            double scale = ui.NotifyScale <= 0 ? 1 : ui.NotifyScale;
            double dur   = ui.NotifyDurationSec > 0 ? ui.NotifyDurationSec : 5;
            var style    = AppConfig.NotifyStyles.Contains(ui.NotifyStyle) ? ui.NotifyStyle : "bar";
            var p        = MacTheme.Resolve(ui.Theme);

            var content = BuildVisual(message, style, scale, p, ui.Theme == "HighContrast");

            // Avalonia borderless window positioned at top-left (matches WPF Left=20, Top=20).
            var w = new Window
            {
                WindowDecorations = WindowDecorations.None,
                Background        = Brushes.Transparent,
                Topmost           = true,
                ShowInTaskbar     = false,
                ShowActivated     = false,
                CanResize         = false,
                Opacity           = 0.94,
                Width             = (int)Math.Round(360 * scale),
                SizeToContent     = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position          = new PixelPoint(20, 20),
                Content           = content,
                // Transparent background on the window itself; content provides the card.
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            };

            // Click anywhere on the banner to dismiss early.
            w.PointerPressed += (_, _) => { try { w.Close(); } catch { } };

            _current = w;
            w.Show();

            // Auto-dismiss after the configured duration.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(dur) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try { w.Close(); } catch { }
                if (ReferenceEquals(_current, w)) _current = null;
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            Log.Write($"MacAlert error: {ex.Message}", "WARN");
        }
    }

    // ── Visual builder (4 styles, mirrors WPF Alert.BuildVisual) ─────────────

    private static Border BuildVisual(string message, string style, double scale, MacPalette p, bool isHC)
    {
        int pad        = (int)Math.Round(16 * scale);
        double tsz     = Math.Round(13 * scale, 1);
        double msz     = Math.Round(12.5 * scale, 1);
        double radius  = Math.Round(12 * scale);
        int hcBorder   = isHC ? Math.Max(3, (int)Math.Round(3 * scale)) : 1;

        var outer = new Border
        {
            CornerRadius    = new CornerRadius(radius),
            Padding         = new Thickness(pad),
            BorderBrush     = MacTheme.Brush(p.Border),
            BorderThickness = new Thickness(hcBorder),
            BoxShadow       = new BoxShadows(new BoxShadow
            {
                Blur    = 22,
                OffsetX = 0, OffsetY = 0,
                Color   = Color.FromArgb(0xB3, 0, 0, 0),
                IsInset = false,
            }),
        };

        switch (style)
        {
            case "solid":
            {
                outer.Background = MacTheme.Brush(p.Accent);
                var sp = new StackPanel();
                sp.Children.Add(MakeText("PatchCord", p.OnAccent, tsz, bold: true));
                var m = MakeText(message, p.OnAccent, msz, bold: false);
                m.Margin = new Thickness(0, (int)(5 * scale), 0, 0);
                sp.Children.Add(m);
                outer.Child = sp;
                break;
            }
            case "minimal":
            {
                outer.Background = MacTheme.Brush(p.Card);
                var sp = new StackPanel();
                var dot = new Border
                {
                    Width           = (int)(8 * scale),
                    Height          = (int)(8 * scale),
                    CornerRadius    = new CornerRadius((int)(4 * scale)),
                    Background      = MacTheme.Brush(p.Accent),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin          = new Thickness(0, 0, 0, (int)(6 * scale)),
                };
                sp.Children.Add(dot);
                sp.Children.Add(MakeText(message, p.Text, msz, bold: false));
                outer.Child = sp;
                break;
            }
            case "outline":
            {
                outer.Background      = MacTheme.Brush(p.Card);
                outer.BorderBrush     = MacTheme.Brush(p.Accent);
                outer.BorderThickness = new Thickness(Math.Max(2, (int)Math.Round(2 * scale)));
                var sp = new StackPanel();
                sp.Children.Add(MakeText("PatchCord", p.Accent, tsz, bold: true));
                var m = MakeText(message, p.Sub, msz, bold: false);
                m.Margin = new Thickness(0, (int)(5 * scale), 0, 0);
                sp.Children.Add(m);
                outer.Child = sp;
                break;
            }
            default: // bar
            {
                outer.Background = MacTheme.Brush(p.Card);
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(5, 6 * scale), GridUnitType.Pixel));
                grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

                var bar = new Border
                {
                    Background   = MacTheme.Brush(p.Accent),
                    CornerRadius = new CornerRadius(3),
                    Margin       = new Thickness(0, 0, (int)(12 * scale), 0),
                };
                Grid.SetColumn(bar, 0);

                var sp = new StackPanel();
                Grid.SetColumn(sp, 1);
                sp.Children.Add(MakeText("PatchCord", p.Text, tsz, bold: true));
                var m = MakeText(message, p.Sub, msz, bold: false);
                m.Margin = new Thickness(0, (int)(5 * scale), 0, 0);
                sp.Children.Add(m);

                grid.Children.Add(bar);
                grid.Children.Add(sp);
                outer.Child = grid;
                break;
            }
        }

        return outer;
    }

    private static TextBlock MakeText(string text, string hex, double size, bool bold) => new()
    {
        Text         = text,
        Foreground   = MacTheme.Brush(hex),
        FontSize     = size,
        TextWrapping = TextWrapping.Wrap,
        FontWeight   = bold ? FontWeight.Bold : FontWeight.Normal,
    };
}
