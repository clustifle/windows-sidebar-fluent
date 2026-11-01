using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Sidebar;

internal static class SidebarAppSearchBox
{
    public static Popup? Configure(AutoSuggestBox box)
    {
        Popup? suggestions = null;
        void Visit(DependencyObject node)
        {
            if (node is Popup popup) { popup.ShouldConstrainToRootBounds = false; if (popup.Child is FrameworkElement child) child.RequestedTheme = box.ActualTheme; }
            if (node is FrameworkElement element && element.Name is "SuggestionsList" or "SuggestionsContainer") { element.MinWidth = 304; element.MaxWidth = 400; element.RequestedTheme = box.ActualTheme; }
            for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); i++) Visit(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i));
        }
        Visit(box);
        if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(box) > 0 && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(box, 0) is FrameworkElement templateRoot && templateRoot.FindName("SuggestionsPopup") is Popup popup)
        {
            suggestions = popup; popup.ShouldConstrainToRootBounds = false;
            if (popup.Child is FrameworkElement child) { child.RequestedTheme = box.ActualTheme; child.MinWidth = 304; child.MaxWidth = 400; Visit(child); }
        }
        return suggestions;
    }
}
