#if THEME_REGRESSION
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
namespace Sidebar;
public partial class MainWindow
{
    internal async void RunSearchRegression()
    {
        bool originalSide = state.Left, originalMode = state.LonghornMode, originalHide = state.AutoHide;
        string originalTheme = state.Theme;
        string report = System.IO.Path.Combine(AppContext.BaseDirectory, "search-test-result.txt");
        System.IO.File.WriteAllText(report, "");
        try
        {
            state.LonghornMode = true; state.AutoHide = false; await UpdateLonghornAsync();
            var apps = (await InstalledApps.GetAsync()).Take(8).ToArray();
            foreach (bool left in new[] { false, true })
            foreach (string theme in new[] { "Dark", "Light" })
            {
                LonghornSearch.IsSuggestionListOpen = false;
                await MoveSidebarAsync(left); state.Theme = theme; SynchronizeAppearance();
                Activate(); LonghornSearch.Focus(FocusState.Programmatic); LonghornSearch.Text = "a";
                LonghornSearch.ItemsSource = apps;
                var popup = SidebarAppSearchBox.Configure(LonghornSearch, left)!;
                LonghornSearch.IsSuggestionListOpen = true; await Task.Delay(800);
                if (!popup.IsOpen || popup.Child is not FrameworkElement content || content.ActualWidth < 310) throw new Exception($"Search content did not open at its full width: open={popup.IsOpen}, width={(popup.Child as FrameworkElement)?.ActualWidth}");
                var screen = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).OuterBounds;
                var candidates = new List<WindowRect>();
                IntPtr root = WinRT.Interop.WindowNative.GetWindowHandle(this);
                SearchEnumWindows((window, _) =>
                {
                    GetWindowThreadProcessId(window, out uint process);
                    if (window != root && process == Environment.ProcessId && SearchIsVisible(window) && GetWindowRect(window, out var bounds) && bounds.Right - bounds.Left >= 280 && bounds.Bottom - bounds.Top >= 70) candidates.Add(bounds);
                    return true;
                }, IntPtr.Zero);
                var result = candidates.FirstOrDefault(bounds => bounds.Left >= screen.X - 8 && bounds.Right <= screen.X + screen.Width + 8 && bounds.Top >= screen.Y - 8 && bounds.Bottom <= screen.Y + screen.Height + 8);
                if (result.Right - result.Left < 280) throw new Exception($"Native search popup is clipped: side={left}, placement={popup.ActualPlacement}, candidates={string.Join(';', candidates.Select(b => $"{b.Left},{b.Top}..{b.Right},{b.Bottom}"))}");
                GetWindowRect(root, out var rail);
                int shadow = (int)Math.Ceiling(24 * Root.XamlRoot.RasterizationScale);
                if (left ? result.Left < rail.Right - shadow : result.Right > rail.Left + shadow) throw new Exception("Search popup did not open toward the desktop");
                await CaptureRailPreviewAsync($"search-{(left ? "left" : "right")}-{theme.ToLowerInvariant()}.png", content);
                state.AutoHide = true; ApplyState(); desktopActive = false; QueueAutoHide(); await Task.Delay(1100);
                if (autoHidden || hideTimer.IsEnabled) throw new Exception("Auto-hide interrupted app search");
                state.AutoHide = false; ApplyState();
                System.IO.File.AppendAllText(report, $"PASS: {left}/{theme}, native bounds={result.Left},{result.Top}..{result.Right},{result.Bottom}\n");
            }
        }
        catch (Exception ex) { System.IO.File.AppendAllText(report, "FAIL: " + ex); }
        finally
        {
            LonghornSearch.IsSuggestionListOpen = false; LonghornSearch.Text = ""; LonghornSearch.ItemsSource = null;
            state.LonghornMode = originalMode; state.AutoHide = originalHide; state.Theme = originalTheme;
            await UpdateLonghornAsync(); await MoveSidebarAsync(originalSide); Close();
        }
    }
    private delegate bool SearchWindowCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "EnumWindows")] private static extern bool SearchEnumWindows(SearchWindowCallback callback, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")] private static extern bool SearchIsVisible(IntPtr window);
}
#endif
