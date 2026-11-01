using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
namespace Sidebar;

public partial class MainWindow
{
    private static Microsoft.UI.Xaml.Controls.Grid SurfaceLayout(int fillRow, params FrameworkElement[] elements)
    {
        var panel = new Microsoft.UI.Xaml.Controls.Grid { RowSpacing = 12 };
        for (int row = 0; row < elements.Length; row++)
        { panel.RowDefinitions.Add(new Microsoft.UI.Xaml.Controls.RowDefinition { Height = row == fillRow ? new GridLength(1, GridUnitType.Star) : GridLength.Auto }); Microsoft.UI.Xaml.Controls.Grid.SetRow(elements[row], row); panel.Children.Add(elements[row]); }
        return panel;
    }
    private void NormalizeV2Preferences()
    {
        if (state.AppearancePreset is not ("Fluent" or "Vista" or "Longhorn 4074")) state.AppearancePreset = state.LonghornMode ? "Longhorn 4074" : "Fluent";
        state.LonghornMode = state.AppearancePreset == "Longhorn 4074";
        if (state.OperatingMode is not ("Desktop Sidebar" or "Launcher")) state.OperatingMode = "Desktop Sidebar";
    }

    private void ApplyLauncherEntry()
    {
        bool launcher = state.OperatingMode == "Launcher";
        LauncherEntry.Visibility = launcher ? Visibility.Visible : Visibility.Collapsed;
        LonghornHeader.Visibility = launcher || state.LonghornMode ? Visibility.Visible : Visibility.Collapsed;
        LonghornSearch.Visibility = launcher || state.LonghornMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyV2Appearance()
    {
        ApplyLauncherEntry();
        if (state.AppearancePreset != "Vista" || state.LonghornMode || new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) return;
        foreach (string id in state.GadgetOrder)
        {
            if (GadgetElement(id) is not Microsoft.UI.Xaml.Controls.Border card) continue;
            var custom = CardStyle(id);
            if (custom.BackgroundOpacity < 0) card.Background = id is "Clock" or "Performance" or "Slide show" ? new SolidColorBrush(Colors.Transparent) : SurfaceBrush(Root.RequestedTheme, 90);
            card.BorderThickness = new Thickness(0);
            if (custom.CornerRadius < 0) card.CornerRadius = new CornerRadius(3);
        }
    }
}
