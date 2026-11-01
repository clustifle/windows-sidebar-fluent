using Microsoft.UI.Xaml;

namespace Sidebar;

public partial class MainWindow
{
    private bool IsGadgetOnScreen(FrameworkElement gadget)
    {
        if (gadget.Visibility != Visibility.Visible || (state.AutoHide && autoHidden)) return false;
        if (!ready || gadget.ActualHeight <= 0) return true;
        try
        {
            FrameworkElement viewport = gadget.Parent == GadgetStack ? GadgetScroller : Root;
            var bounds = gadget.TransformToVisual(viewport).TransformBounds(new Windows.Foundation.Rect(0, 0, gadget.ActualWidth, gadget.ActualHeight));
            return bounds.Bottom > 0 && bounds.Top < viewport.ActualHeight && bounds.Right > 0 && bounds.Left < viewport.ActualWidth;
        }
        catch (ArgumentException) { return false; }
    }
}
