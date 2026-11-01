#if THEME_REGRESSION
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    internal async void RunV2Regression()
    {
        var original = state; string report = Path.Combine(AppContext.BaseDirectory, "v2-test-result.txt"); File.WriteAllText(report, "");
        try
        {
            state = System.Text.Json.JsonSerializer.Deserialize<SidebarState>(System.Text.Json.JsonSerializer.Serialize(original))!;
            state.AutoHide = false; state.OperatingMode = "Launcher";
            foreach (var preset in new[] { "Fluent", "Vista", "Longhorn 4074" })
            foreach (var theme in new[] { "Light", "Dark" })
            {
                state.AppearancePreset = preset; state.LonghornMode = preset == "Longhorn 4074"; state.Theme = theme; styledLonghorn = null;
                await UpdateLonghornAsync(); await Task.Delay(200);
                if (LauncherEntry.Visibility != Visibility.Visible || LonghornSearch.Visibility != Visibility.Visible || Root.ActualTheme != (theme == "Light" ? ElementTheme.Light : ElementTheme.Dark)) throw new Exception("Preset/mode/theme mismatch");
                if (state.LonghornHideTaskbar || state.LonghornHideSystemTray) throw new Exception("Companion mode changed taskbar ownership");
                await CaptureRailPreviewAsync($"v2-{preset.Replace(' ', '-')}-{theme}.png", Root);
            }
            File.AppendAllText(report, "PASS: three appearance presets, both themes, independent launcher mode and unchanged taskbar ownership.\n");
            OpenLauncher_Click(this, new RoutedEventArgs()); await Task.Delay(500);
            for (int i = 0; i < 50 && V2Find<GridView>((DependencyObject)preferencesWindow!.Content).First().Items.Count == 0; i++) await Task.Delay(100);
            var launcherRoot = (DependencyObject)preferencesWindow!.Content;
            var grid = V2Find<GridView>(launcherRoot).First();
            if (grid.Items.Count == 0) throw new Exception("Launcher app catalog did not load");
            var search = V2Find<TextBox>(launcherRoot).Single(box => box.PlaceholderText == "Search apps and pinned websites"); search.Text = "no-such-installed-app-9b2d"; await Task.Delay(200);
            for (int i = 0; i < 100 && grid.Items.Count != 0; i++) await Task.Delay(100);
            if (grid.Items.Count != 0) throw new Exception($"Launcher search did not filter: query={search.Text}, grids={V2Find<GridView>(launcherRoot).Count()}, items={grid.Items.Count}");
            search.Text = ""; await Task.Delay(200);
            if (grid.Items.Count == 0) throw new Exception("Launcher search did not reset");
            await CaptureRailPreviewAsync("v2-launcher.png", (UIElement)preferencesWindow.Content);
            var pivot = V2Find<Pivot>(launcherRoot).First(); pivot.SelectedIndex = 1; await Task.Delay(500);
            if (V2Find<ListView>(launcherRoot).FirstOrDefault() is null) throw new Exception("Running-window tab missing");
            File.AppendAllText(report, "PASS: virtualized app grid, catalog load, empty search/reset, native running-window tab.\n");
            preferencesWindow.Close(); await Task.Delay(200); ShowGadgetGallery(); await Task.Delay(300);
            var galleryRoot = (DependencyObject)preferencesWindow!.Content; var gallery = V2Find<GridView>(galleryRoot).First();
            if (gallery.Items.Count != 10) throw new Exception("Gallery catalog incomplete");
            var gallerySearch = V2Find<TextBox>(galleryRoot).Single(box => box.PlaceholderText == "Search gadgets"); gallerySearch.Text = "puzzle"; await Task.Delay(200);
            if (gallery.Items.Count != 1) throw new Exception("Gallery search failed"); gallerySearch.Text = ""; await Task.Delay(200);
            await CaptureRailPreviewAsync("v2-gallery.png", (UIElement)preferencesWindow.Content);
            File.AppendAllText(report, "PASS: all ten gadgets appear in searchable categorized gallery.\n");
        }
        catch (Exception ex) { File.AppendAllText(report, "FAIL: " + ex); }
        finally { preferencesWindow?.Close(); state = original; styledLonghorn = null; await UpdateLonghornAsync(); Save(); Close(); }
    }
}
#endif
