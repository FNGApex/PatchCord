using Avalonia.Controls;
using Avalonia.Media;

namespace PatchCord;

/// <summary>
/// UserControl for one install row in the Status tab.
/// Populated imperatively by MainWindow (mirrors the Windows shell's BuildInstallRows).
/// </summary>
public sealed partial class InstallRow : UserControl
{
    public InstallRow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Populate the row from a view-model + palette.
    /// Called from MainWindow after the VM is bound.
    /// </summary>
    internal void Bind(InstallRowViewModel vm, MacPalette p, Action<InstallRowViewModel> onToggle, Action<InstallRowViewModel> onRemove,
                       Action<InstallRowViewModel, string> onModChange)
    {
        RowName.Text = vm.Name;
        RowName.Foreground = MacTheme.Brush(p.Text);
        RowPath.Text = vm.Path;
        RowPath.Foreground = MacTheme.Brush(p.Sub);

        // Primary badge
        if (vm.PrimaryBadgeVisible)
        {
            RowBadge.IsVisible = true;
            var badgeColor = vm.PrimaryBadgeText switch
            {
                "Vencord" or "Equicord" or "BetterDiscord" => p.On,
                "Other mod" => MacTheme.BadgeNeutral,
                _ when vm.IsRunning && vm.Enabled && vm.ClientMod != "none" => MacTheme.BadgeError,
                _ => MacTheme.BadgeNeutral,
            };
            RowBadge.Background = MacTheme.Brush(badgeColor);
            RowStatus.Text = vm.PrimaryBadgeText;
            // Known-mod badges sit on the theme's On color → use OnText (theme-aware).
            // Neutral/error badges have fixed semantic backgrounds → always white text.
            RowStatus.Foreground = MacTheme.Brush(
                vm.PrimaryBadgeText is "Vencord" or "Equicord" or "BetterDiscord"
                    ? p.OnText : MacTheme.BadgeText);
        }
        else
        {
            RowBadge.IsVisible = false;
        }

        // OpenAsar badge
        if (vm.OpenAsarBadgeVisible)
        {
            RowOpenAsarBadge.IsVisible = true;
            RowOpenAsarBadge.Background = MacTheme.Brush(p.On);
            RowOpenAsarStatus.Text = vm.OpenAsarBadgeText;
            RowOpenAsarStatus.Foreground = MacTheme.Brush(p.OnText);
        }
        else
        {
            RowOpenAsarBadge.IsVisible = false;
        }

        // Mod button — Content only; Background/Foreground are owned by the ModButton
        // ControlTheme (defaults: GhostHover idle, Text text). Setting them locally would
        // override the theme's :pointerover/:pressed setters, breaking hover/press.
        RowModBtn.Content = vm.ModLabel + "  ▾";

        // Per-install mod picker (B1): a MenuFlyout opens on click; selecting routes
        // through onModChange (sets the install's mod, saves, re-arms patching, refreshes).
        var flyout = new MenuFlyout();
        foreach (var (mod, title, _) in MainViewModel.ClientModItems)
        {
            var capturedMod = mod;
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => onModChange(vm, capturedMod);
            flyout.Items.Add(item);
        }
        RowModBtn.Flyout = flyout;

        // Toggle button
        RowToggle.Content = vm.ToggleLabel;
        RowToggle.Background = MacTheme.Brush(vm.Enabled ? p.On : p.GhostHover);
        RowToggle.Foreground = MacTheme.Brush(vm.Enabled ? p.OnText : p.Text);
        RowToggle.Click += (_, _) => onToggle(vm);

        // Remove button
        RowRemove.Foreground = MacTheme.Brush(p.Sub);
        if (vm.IsCustom)
        {
            RowRemove.IsVisible = true;
            RowRemove.Click += (_, _) => onRemove(vm);
        }
        else
        {
            RowRemove.IsVisible = false;
        }
    }
}
