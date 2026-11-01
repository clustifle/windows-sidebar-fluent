using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Sidebar;

public partial class MainWindow
{
    private void AppSearchResult_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement root) return;
        if (root.Tag is null) { root.Tag = true; root.DataContextChanged += (_, _) => RenderSearchResult(root); }
        RenderSearchResult(root);
    }
    private void RenderSearchResult(FrameworkElement root)
    {
        if (root.DataContext is not InstalledApp app || root.FindName("SearchIcon") is not Image icon || root.FindName("SearchFallback") is not FontIcon fallback) return;
        ToolTipService.SetToolTip(root, app.Name);
        icon.Source = null; icon.Visibility = Visibility.Collapsed; fallback.Visibility = Visibility.Visible; fallback.Glyph = "\uE8A5"; root.IsHitTestVisible = !string.IsNullOrEmpty(app.Target);
        if (string.IsNullOrEmpty(app.Target)) { fallback.Glyph = "\uE721"; return; }
        LoadShortcutIcon(new LaunchShortcut { Name = app.Name, Target = app.Target }, icon, fallback, false);
    }
}
