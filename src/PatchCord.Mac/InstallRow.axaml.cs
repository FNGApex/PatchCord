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
    internal void Bind(InstallRowViewModel vm, MacPalette p, Action<InstallRowViewModel> onToggle, Action<InstallRowViewModel> onRemove)
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
                "Other mod" => "#80848E",
                _ when vm.IsRunning && vm.Enabled && vm.ClientMod != "none" => "#F23F43",
                _ => "#80848E",
            };
            RowBadge.Background = MacTheme.Brush(badgeColor);
            RowStatus.Text = vm.PrimaryBadgeText;
            RowStatus.Foreground = MacTheme.Brush(
                vm.PrimaryBadgeText is "Vencord" or "Equicord" or "BetterDiscord" ? p.OnText : "#FFFFFF");
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

        // Mod button
        RowModBtn.Content = vm.ModLabel + "  ▾";
        RowModBtn.Background = MacTheme.Brush(p.GhostHover);
        RowModBtn.Foreground = MacTheme.Brush(p.Text);

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
