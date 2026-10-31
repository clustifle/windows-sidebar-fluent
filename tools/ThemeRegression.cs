#if THEME_REGRESSION
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace Sidebar;
public partial class MainWindow
{
    internal void RunThemeRegression()
    {
        var original = state.Theme;
        var originalChildren = GadgetStack.Children.ToArray();
        var modes = new[] { "Light", "Dark", "System", "Dark", "Light", "System" };
        int index = 0;
        var check = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        Settings_Click(this, new RoutedEventArgs());
        ComboBox? Find(DependencyObject node)
        {
            if (node is ComboBox combo && combo.Header as string == "Theme") return combo;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                if (Find(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
            return null;
        }
        check.Tick += (_, _) =>
        {
            try
            {
                var selector = Find((DependencyObject)preferencesWindow!.Content) ?? throw new Exception("Theme selector missing");
                if (!GadgetStack.Children.SequenceEqual(originalChildren)) throw new Exception("Theme selection rebuilt gadgets");
                if (index > 0 && modes[index - 1] != "System" && Root.ActualTheme.ToString() != modes[index - 1]) throw new Exception("Theme did not apply");
                if (index == modes.Length)
                {
                    selector.SelectedItem = original;
                    System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "theme-test-result.txt"), "PASS: 6 settings-selector changes, actual themes verified; dispatcher remained responsive; gadgets retained.");
                    check.Stop(); Close(); return;
                }
                selector.SelectedItem = modes[index++];
            }
            catch (Exception ex)
            {
                state.Theme = original; Save();
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "theme-test-result.txt"), ex.ToString());
                check.Stop(); Close();
            }
        };
        check.Start();
    }
}
#endif
