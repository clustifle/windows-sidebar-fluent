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
        var originalMaterial = state.Material;
        var originalChildren = GadgetStack.Children.ToArray();
        var modes = Enumerable.Range(0, 4).SelectMany(_ => new[] { "Light", "Dark", "System", "Dark", "Light", "System" }).ToArray();
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
                if (index > 0)
                {
                    var expected = Root.ActualTheme == ElementTheme.Dark ? Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Dark : Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Light;
                    if (((SidebarBackdrop)SystemBackdrop).AppliedTheme != expected || ((SidebarBackdrop)preferencesWindow.SystemBackdrop).AppliedTheme != expected) throw new Exception("Backdrop disagrees with content theme");
                    if (((FrameworkElement)preferencesWindow.Content).ActualTheme != Root.ActualTheme) throw new Exception("Settings/content theme mismatch");
                    var surface = ((SolidColorBrush)((Panel)preferencesWindow.Content).Background).Color;
                    if ((surface.R < 128) != (Root.ActualTheme == ElementTheme.Dark)) throw new Exception("Settings surface contrast mismatch");
                }
                if (index == modes.Length)
                {
                    state.Material = originalMaterial; ApplyState();
                    selector.SelectedItem = original;
                    System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "theme-test-result.txt"), "PASS: 24 settings changes across Acrylic/Mica; sidebar/settings content, both backdrop configurations, and surface contrast verified; dispatcher responsive; gadgets retained.");
                    check.Stop(); Close(); return;
                }
                if (index % 6 == 0) { state.Material = index % 12 == 0 ? "Acrylic" : "Mica"; ApplyState(); }
                selector.SelectedItem = modes[index++];
            }
            catch (Exception ex)
            {
                state.Theme = original; state.Material = originalMaterial; ApplyState(); Save();
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "theme-test-result.txt"), ex.ToString());
                check.Stop(); Close();
            }
        };
        check.Start();
    }
}
#endif
