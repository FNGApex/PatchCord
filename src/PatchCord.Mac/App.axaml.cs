using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PatchCord;

/// <summary>
/// Avalonia Application for PatchCord.Mac.
/// B3a: bootstraps Fluent theme and a tray icon with a Quit item.
/// Real UI (B3b), monitor loop (B3c), and lifecycle (B3d) are added in subsequent slices.
/// </summary>
public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        WireQuitMenuItem();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }

    // ── Tray menu wiring ──────────────────────────────────────────────────────

    private void WireQuitMenuItem()
    {
        // Walk the tray icon's NativeMenu to find the Quit item and attach a click handler.
        var trayIcons = TrayIcon.GetIcons(this);
        if (trayIcons == null) return;

        foreach (var icon in trayIcons)
        {
            if (icon.Menu == null) continue;
            foreach (var item in icon.Menu.Items)
            {
                if (item is NativeMenuItem mi && mi.Header == "Quit")
                {
                    mi.Click += (_, _) => Quit();
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Exits the application cleanly from any context (tray Quit or auto-close smoke path).
    /// </summary>
    internal static void Quit()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
            lt.Shutdown();
    }
}
