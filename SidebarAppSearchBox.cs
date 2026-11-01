using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Sidebar;

internal static class SidebarAppSearchBox
{
    private sealed class Placement(AutoSuggestBox box) { public AutoSuggestBox Box { get; } = box; public bool Left; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Popup, Placement> placements = new();
    public static Popup? Configure(AutoSuggestBox box, bool left)
    {
        Popup? suggestions = null;
        void Visit(DependencyObject node)
        {
            if (node is Popup popup) { popup.ShouldConstrainToRootBounds = false; if (popup.Child is FrameworkElement child) child.RequestedTheme = box.ActualTheme; }
            if (node is FrameworkElement element && element.Name is "SuggestionsList" or "SuggestionsContainer") { element.MinWidth = 304; element.MaxWidth = 400; element.RequestedTheme = box.ActualTheme; }
            if (node is ListView list && list.Name == "SuggestionsList" && list.ItemContainerStyle is null)
            {
                var style = new Style { TargetType = typeof(ListViewItem) };
                style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
                list.ItemContainerStyle = style;
            }
            for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); i++) Visit(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i));
        }
        Visit(box);
        if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(box) > 0 && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(box, 0) is FrameworkElement templateRoot && templateRoot.FindName("SuggestionsPopup") is Popup popup)
        {
            suggestions = popup; popup.ShouldConstrainToRootBounds = false;
            if (!placements.TryGetValue(popup, out var placement))
            {
                placement = new Placement(box); placements.Add(popup, placement);
                popup.Opened += (_, _) =>
                {
                    Position(popup, placement);
                    box.DispatcherQueue.TryEnqueue(() => { if (popup.IsOpen) Position(popup, placement); });
                };
            }
            placement.Left = left;
            if (popup.Child is FrameworkElement child) { child.RequestedTheme = box.ActualTheme; Visit(child); child.Width = child.MinWidth = child.MaxWidth = 320; }
            Position(popup, placement);
            if (popup.IsOpen) box.DispatcherQueue.TryEnqueue(() => { if (popup.IsOpen) Position(popup, placement); });
        }
        return suggestions;
    }
    private static void Position(Popup popup, Placement placement)
    {
        var box = placement.Box;
        if (box.XamlRoot?.Content is not FrameworkElement root || !popup.IsOpen) return;
        if (popup.Child is FrameworkElement child) child.Width = child.MinWidth = child.MaxWidth = 320;
        // Keep AutoSuggestBox's native keyboard behavior, but place its list inward from the rail.
        var origin = box.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        double x = placement.Left ? origin.X + box.ActualWidth + 6 : origin.X - 326;
        var host = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(box.XamlRoot.ContentIslandEnvironment.AppWindowId);
        var screen = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(host.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).OuterBounds;
        double scale = box.XamlRoot.RasterizationScale;
        double minimum = (screen.X - host.Position.X) / scale + 6;
        double maximum = (screen.X + screen.Width - host.Position.X) / scale - 326;
        popup.HorizontalOffset = maximum >= minimum ? Math.Clamp(x, minimum, maximum) : minimum;
        popup.VerticalOffset = origin.Y;
    }
}
