using Avalonia.Controls;
using Avalonia.Threading;

namespace PatchCord;

/// <summary>
/// Minimal placeholder window for B3a (Avalonia bootstrap).
/// Real UI (status + options tabs, install rows) is ported in B3b.
/// </summary>
public sealed partial class MainWindow : Window
{
    // When non-zero, the window auto-closes after this many milliseconds.
    // Set by Program.Main when --mac-uitest is passed, to keep the smoke run non-blocking.
    internal static int AutoCloseAfterMs { get; set; }

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
}
