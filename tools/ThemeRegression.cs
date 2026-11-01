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
                if (index == 0) _ = CaptureRailPreviewAsync("settings-layout.png", (UIElement)preferencesWindow.Content);
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
        bool originalSide = state.Left, originalLonghorn = state.LonghornMode, originalAutoHide = state.AutoHide;
        var originalMonth = month;
        int[] originalPuzzle = picturePuzzle.Tiles.ToArray(); int originalMoves = picturePuzzle.Moves;
        try
        {
            foreach (bool left in new[] { !originalSide, originalSide })
            {
                await MoveSidebarAsync(left);
                GetWindowRect(WinRT.Interop.WindowNative.GetWindowHandle(this), out var bounds);
                var monitor = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).OuterBounds;
                if (state.Left != left || sideMoveRunning || (left ? bounds.Left != monitor.X : bounds.Right != monitor.X + monitor.Width)) throw new Exception("Side transition missed the screen edge");
            }
            await Task.WhenAll(MoveSidebarAsync(!originalSide), MoveSidebarAsync(originalSide));
            if (state.Left != originalSide) throw new Exception("Rapid side change lost the final choice");
            foreach (var date in new[] { new DateTime(2028, 2, 1), new DateTime(2027, 2, 1), new DateTime(2026, 8, 1) })
            { month = date; RenderCalendar(); if (CalendarGrid.Children.Count != 49 || CalendarGrid.RowDefinitions.Count != 7) throw new Exception("Calendar must retain six complete weeks"); }
            preferencesWindow?.Close(); CustomizeClock_Click(this, new RoutedEventArgs()); await Task.Delay(200);
            if (preferencesWindow is null) throw new Exception("Clock customization failed");
            preferencesWindow.Close();
            state.LonghornMode = true; state.LonghornHideTaskbar = true; state.LonghornHideSystemTray = true;
            await UpdateLonghornAsync(); await Task.Delay(1500);
            if (state.LonghornHideTaskbar || state.LonghornHideSystemTray || Root.FindName("LonghornLaunchButtons") is not null || Root.FindName("LonghornSystemButtons") is not null) throw new Exception("Removed shell controls or legacy taskbar preferences remain");
            if (System.Diagnostics.Process.GetProcessesByName("FluentSidebar.ShellHost").Length != 0) throw new Exception("Longhorn mode retained a background shell helper");
            if (!RegressionTaskbarVisible(RegressionFindTaskbar("Shell_TrayWnd", null))) throw new Exception("Windows taskbar is hidden");
            if (LonghornPins.Children.Count != state.Shortcuts.Count) throw new Exception("Pinned launcher count incorrect");
            bool bottomClock = state.LonghornBottomClock;
            try
            {
                state.LonghornBottomClock = true; ApplyState();
                if (!LonghornClockDock.Children.Contains(ClockGadget)) throw new Exception("Clock docking failed");
                state.LonghornBottomClock = false; ApplyState();
                if (!GadgetStack.Children.Contains(ClockGadget)) throw new Exception("Clock undocking failed");
            }
            finally { state.LonghornBottomClock = bottomClock; ApplyState(); }
            foreach (string id in state.GadgetOrder)
            {
                int originalHeight = CardStyle(id).Height;
                try
                {
                    var height = BuildCardEditor(id).Children.OfType<NumberBox>().First(box => box.Header as string == "Locked height (0 = default)");
                    height.Value = 240;
                    if (((Border)GadgetElement(id)).Height != 240) throw new Exception(id + " card customization failed");
                }
                finally { CardStyle(id).Height = originalHeight; }
            }
            ApplyState(); month = originalMonth; RenderCalendar(); RenderPuzzle();
            if (PuzzleBoard.Width != PuzzleBoard.Height || PuzzleBoard.Width > SidebarWidth - 20) throw new Exception("Puzzle escaped its card bounds");
            await CaptureRailPreviewAsync();
            state.AutoHide = true; ApplyState(); desktopActive = true; RevealSidebar(); HideSidebar(); await Task.Delay(350);
            if (autoHidden || currentReveal != 1) throw new Exception("Desktop must keep the sidebar visible");
            desktopActive = false; HideSidebar(); await Task.Delay(350);
            if (!autoHidden || currentReveal != 0) throw new Exception("Auto-hide did not finish");
            if (!timerDeadline.HasValue && timer.Interval.TotalSeconds < 30) throw new Exception("Hidden sidebar kept a high-frequency timer");
            RevealSidebar(); await Task.Delay(80); double midway = currentReveal; HideSidebar();
            if (currentReveal != midway) throw new Exception("Auto-hide reversal snapped");
            await Task.Delay(350); desktopActive = true; RevealSidebar(); await Task.Delay(350);
            state.AutoHide = false; ApplyState(); Activate(); LonghornSearch.Focus(FocusState.Programmatic);
            LonghornSearch.ItemsSource = (await InstalledApps.GetAsync()).Take(3).ToArray();
            var popup = SidebarAppSearchBox.Configure(LonghornSearch); LonghornSearch.IsSuggestionListOpen = true; await Task.Delay(600);
            if (popup is null || !popup.IsOpen || popup.ShouldConstrainToRootBounds || popup.Child is not FrameworkElement results || results.ActualWidth < 280 || LonghornSearch.ItemTemplate is null) throw new Exception("App search suggestions are clipped");
            LonghornSearch.IsSuggestionListOpen = false; LonghornSearch.ItemsSource = null;
            state.LonghornMode = false; await UpdateLonghornAsync();
            if (LonghornPanel.Visibility != Visibility.Collapsed || !GadgetStack.Children.Contains(ClockGadget)) throw new Exception("Normal sidebar layout did not return");
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "theme-test-result.txt"), "\\nPASS: Longhorn rail without shell buttons; legacy taskbar hiding reset; original taskbar visible; no background helper; pinned launchers; clock docking; all card heights; six-week calendar; square puzzle; desktop auto-hide, reversal and low-frequency hidden updates; icon search; side transitions.");
        }
        catch (Exception ex) { System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "theme-test-result.txt"), "\\nFAIL: " + ex); }
        finally
        {
            LonghornSearch.IsSuggestionListOpen = false;
            picturePuzzle.Restore(originalPuzzle, originalMoves); state.AutoHide = originalAutoHide; state.LonghornMode = originalLonghorn;
            await UpdateLonghornAsync(); await MoveSidebarAsync(originalSide); month = originalMonth; Close();
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr RegressionFindTaskbar(string name, string? title);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    private static extern bool RegressionTaskbarVisible(IntPtr window);

    private async Task CaptureRailPreviewAsync(string filename = "longhorn-layout.png", UIElement? element = null)
    {
        await Task.Delay(150);
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(element ?? Root);
        if ((bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) && element is FlyoutPresenter { Content: UIElement child }) await bitmap.RenderAsync(child);
        if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) return;
        var buffer = await bitmap.GetPixelsAsync();
        using var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer);
        byte[] pixels = new byte[buffer.Length]; reader.ReadBytes(pixels);
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync(); stream.Seek(0);
        using var result = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
        await result.LoadAsync((uint)stream.Size); byte[] png = new byte[stream.Size]; result.ReadBytes(png);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, filename), png);
    }
    private static UIElement ShellPanelPresenter(UIElement content)
    {
        for (DependencyObject? node = content; node is not null; node = VisualTreeHelper.GetParent(node)) if (node is FlyoutPresenter presenter) return presenter;
        return content;
    }
}
#endif
