#if THEME_REGRESSION
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
namespace Sidebar;
public partial class MainWindow
{
    internal async void RunLauncherFlyoutRegression()
    {
        var original = state;
        string report = Path.Combine(AppContext.BaseDirectory, "launcher-flyout-test-result.txt"); File.WriteAllText(report, "");
        try
        {
            state = System.Text.Json.JsonSerializer.Deserialize<SidebarState>(System.Text.Json.JsonSerializer.Serialize(original))!;
            state.AutoHide = false; state.OperatingMode = "Launcher"; state.LonghornMode = true; state.AppearancePreset = "Longhorn 4074";
            state.LonghornShowPins = true; state.LonghornPinRows = 2; state.LonghornPinIconSize = 22;
            state.Shortcuts = Enumerable.Range(1, 14).Select(i => new LaunchShortcut { Name = "Pinned " + i, Target = "https://example.test/" + i }).ToList();
            foreach (int width in new[] { 156, 168, 228, 280 })
            {
                state.LonghornWidth = width; await UpdateLonghornAsync(); await Task.Delay(150);
                int columns = Math.Clamp((SidebarWidth - 12) / 34, 1, 8), capacity = columns * 2;
                int count = Math.Min(state.Shortcuts.Count, capacity);
                if (LonghornPins.Children.Count != count || LonghornPins.RowDefinitions.Count > 2) throw new Exception("Rail overflow is not bounded");
                foreach (var button in LonghornPins.Children.OfType<Button>())
                    if (button.Width < 30 || button.Height < 30) throw new Exception("Rail hit target is too small");
                if (LonghornPins.ActualWidth > LonghornPanel.ActualWidth + 1 || LonghornPins.ActualHeight > 71) throw new Exception("Rail pins overflow their area");
            }
            File.AppendAllText(report, "PASS: Longhorn widths 156/168/228/280, bounded pin rows, compact hit targets and overflow.\n");
            state.LonghornWidth = 168;
            foreach (bool left in new[] { false, true })
            foreach (string theme in new[] { "Light", "Dark" })
            {
                state.Left = left; state.Theme = theme; styledLonghorn = null; await UpdateLonghornAsync(); await Task.Delay(200);
                ShowLauncherFlyout(0); await Task.Delay(250);
                if (launcherFlyout?.IsOpen != true || launcherSurface is null || preferencesWindow is not null || dialogOpen) throw new Exception("Launcher is not a standalone flyout");
                var surface = launcherSurface;
                if (launcherFlyout.ShouldConstrainToRootBounds || surface.ActualWidth <= SidebarWidth || surface.RequestedTheme != Root.RequestedTheme) throw new Exception("Flyout clipping or theme mismatch");
                if (launcherFlyout.Placement != (left ? FlyoutPlacementMode.RightEdgeAlignedTop : FlyoutPlacementMode.LeftEdgeAlignedTop)) throw new Exception("Flyout opens toward screen edge");
                var grid = V2Find<GridView>(surface).Single();
                for (int i = 0; i < 150 && grid.Items.Count == 0; i++) await Task.Delay(100);
                if (grid.Items.Count == 0) throw new Exception("App catalog did not load");
                var search = V2Find<TextBox>(surface).Single(t => t.Name == "LauncherSearch");
                search.Text = "no-installed-app-89cb";
                for (int i = 0; i < 100 && grid.Items.Count != 0; i++) await Task.Delay(100);
                if (grid.Items.Count != 0) throw new Exception("Flyout search did not filter");
                search.Text = "";
                for (int i = 0; i < 100 && grid.Items.Count == 0; i++) await Task.Delay(100);
                selectLauncherView!(1); await Task.Delay(150);
                if (!grid.Items.Cast<InstalledApp>().Select(a => a.Name).SequenceEqual(state.Shortcuts.Select(s => s.Name))) throw new Exception("Pinned view changed user order or lost pins");
                state.AutoHide = true; QueueAutoHide(); HideSidebar();
                if (hideTimer.IsEnabled || autoHidden) throw new Exception("Auto-hide ran while launcher was open"); state.AutoHide = false;
                await CaptureRailPreviewAsync($"launcher-pinned-{theme.ToLowerInvariant()}-{(left ? "left" : "right")}.png", surface);
                selectLauncherView(0);
                // Let visible icons complete before taking the grid preview.
                for (int i = 0; i < 80 && V2Find<Image>(surface).Count(image => image.Visibility == Visibility.Visible && image.Source is not null) < 6; i++) await Task.Delay(100);
                await CaptureRailPreviewAsync($"launcher-apps-{theme.ToLowerInvariant()}-{(left ? "left" : "right")}.png", surface);
                selectLauncherView(2); await Task.Delay(300);
                if (V2Find<ListView>(surface).Single().Visibility != Visibility.Visible) throw new Exception("Window view unavailable");
                launcherFlyout.Hide(); await Task.Delay(150);
                if (launcherFlyout is not null || launcherSurface is not null) throw new Exception("Flyout did not release UI references");
            }
            File.AppendAllText(report, "PASS: native flyout on both edges and themes, out-of-root content, app search, user pin order, window view, auto-hide hold and close cleanup.\n");
            state.Shortcuts.Clear(); RefreshQuickLaunch();
            if (LonghornPinsSection.Visibility != Visibility.Visible || LonghornPins.Children.Count != 1) throw new Exception("Empty rail has no add entry");
            state.Shortcuts = Enumerable.Range(1, 14).Select(i => new LaunchShortcut { Name = "Pinned " + i, Target = "https://example.test/" + i }).ToList(); RefreshQuickLaunch();
            ShowLauncherFlyout(1); await Task.Delay(150); await MoveSidebarAsync(!state.Left); await Task.Delay(100);
            if (launcherFlyout?.IsOpen == true) throw new Exception("Moving rail left a stale flyout");
            await CaptureRailPreviewAsync("longhorn-quick-launch.png", LonghornPanel);
            File.AppendAllText(report, "PASS: empty Quick Launch add entry, overflow keeps all pins, moving the rail closes the flyout.\n");
        }
        catch (Exception ex) { File.AppendAllText(report, "FAIL: " + ex); }
        finally { launcherFlyout?.Hide(); state = original; styledLonghorn = null; await UpdateLonghornAsync(); Save(); Close(); }
    }
}
#endif
