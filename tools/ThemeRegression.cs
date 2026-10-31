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
                    check.Stop(); FinishMotionAndCalendarRegression(); return;
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
    private async void FinishMotionAndCalendarRegression()
    {
        bool originalSide = state.Left;
        var originalMonth = month;
        bool originalLonghorn = state.LonghornMode, originalHide = state.LonghornHideTaskbar;
        try
        {
            foreach (bool left in new[] { !originalSide, originalSide })
            {
                await MoveSidebarAsync(left);
                if (state.Left != left || sideMoveRunning) throw new Exception("Side transition did not finish");
                GetWindowRect(WinRT.Interop.WindowNative.GetWindowHandle(this), out var rect);
                var monitor = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).OuterBounds;
                if ((left && rect.Left != monitor.X) || (!left && rect.Right != monitor.X + monitor.Width)) throw new Exception("Sidebar missed screen edge");
            }
            await Task.WhenAll(MoveSidebarAsync(!originalSide), MoveSidebarAsync(originalSide));
            if (state.Left != originalSide) throw new Exception("Rapid side change lost final choice");
            foreach (var date in new[] { new DateTime(2028, 2, 1), new DateTime(2027, 2, 1), new DateTime(2026, 8, 1) })
            {
                month = date; RenderCalendar();
                if (CalendarGrid.Children.Count != 7 + DateTime.DaysInMonth(date.Year, date.Month)) throw new Exception("Calendar day count incorrect");
            }
            preferencesWindow?.Close(); CustomizeClock_Click(this, new RoutedEventArgs());
            await Task.Delay(200);
            if (preferencesWindow is null) throw new Exception("Clock cards failed to open");
            state.LonghornMode = true; state.LonghornHideTaskbar = false;
            await UpdateLonghornAsync();
            for (int attempt = 0; attempt < 24 && (RunningItems.Children.Count == 0 || ShellTrayItems.Children.Count == 0); attempt++) await Task.Delay(500);
            if (RunningItems.Children.Count == 0 || ShellTrayItems.Children.Count == 0 || LonghornPanel.Visibility != Visibility.Visible) throw new Exception($"Longhorn UI not ready: mode={state.LonghornMode}, windows={RunningItems.Children.Count}, tray={ShellTrayItems.Children.Count}, error={shellBridge?.LastError}");
            if (LonghornPins.Children.Count != state.Shortcuts.Count) throw new Exception("Pinned icon grid count incorrect");
            var buttons = RunningItems.Children.ToArray();
            var unchanged = shellIconViews[RunningItems].Values.Select(view => view.Item).ToArray();
            RenderShellEntries(RunningItems, unchanged, false);
            if (!RunningItems.Children.SequenceEqual(buttons)) throw new Exception("Unchanged running apps were rebuilt");
            state.LonghornHideTaskbar = true;
            await shellBridge!.SendAsync(new { action = "replace", enabled = true });
            for (int attempt = 0; attempt < 30 && !taskbarReplacementActive; attempt++) await Task.Delay(100);
            GetWindowRect(WinRT.Interop.WindowNative.GetWindowHandle(this), out var full);
            var screen = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).OuterBounds;
            if (!taskbarReplacementActive || full.Top != screen.Y || full.Bottom != screen.Y + screen.Height) throw new Exception($"Replacement height: active={taskbarReplacementActive}, bounds={full.Top}..{full.Bottom}, expected={screen.Y}..{screen.Y + screen.Height}, mode={state.LonghornMode}, error={shellBridge?.LastError}");
            preferencesWindow?.Close(); LonghornApps_Click(this, new RoutedEventArgs());
            await Task.Delay(300);
            GridView? FindApps(DependencyObject node)
            {
                if (node is GridView grid) return grid;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) if (FindApps(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
                return null;
            }
            for (int attempt = 0; attempt < 36; attempt++)
            {
                if (preferencesWindow?.Content is DependencyObject content && FindApps(content) is { } readyGrid && readyGrid.Items.Count > 0) break;
                await Task.Delay(250);
            }
            await Task.Delay(250);
            if (preferencesWindow?.Content is not DependencyObject appsRoot || FindApps(appsRoot) is not { } appGrid || appGrid.Items.Count == 0 || appGrid.ItemTemplate is null) throw new Exception("Apps icon grid did not load");
            if (appGrid.ContainerFromIndex(0) is not GridViewItem firstApp || firstApp.ContentTemplateRoot is not FrameworkElement appTile || appTile.FindName("AppName") is not TextBlock appName || string.IsNullOrWhiteSpace(appName.Text)) throw new Exception("Launcher tile name binding failed");
            state.LonghornMode = false; await UpdateLonghornAsync();
            if (LonghornPanel.Visibility != Visibility.Collapsed || shellBridge is not null) throw new Exception("Longhorn mode did not stop");
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "theme-test-result.txt"), "\nPASS: full monitor height with hidden taskbar, retained shell buttons, Apps icon grid, hosted running apps/tray UI, pinned icon grid, opt-out restoration; side transitions, rapid reversal, screen edges, leap/non-leap calendar months, clock cards opened.");
        }
        catch (Exception ex) { System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "theme-test-result.txt"), "\nFAIL: " + ex); }
        finally { state.LonghornMode = originalLonghorn; state.LonghornHideTaskbar = originalHide; await UpdateLonghornAsync(); await MoveSidebarAsync(originalSide); month = originalMonth; Close(); }
    }
}
#endif
