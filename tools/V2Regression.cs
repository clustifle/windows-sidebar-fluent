#if THEME_REGRESSION
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
namespace Sidebar;
public partial class MainWindow
{
    private static IEnumerable<T> V2Find<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in V2Find<T>(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    internal void RunV2Regression() => RunLauncherFlyoutRegression();
}
#endif