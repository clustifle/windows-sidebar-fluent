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
        bool originalSide = state.Left;
        var originalMonth = month;
        bool originalLonghorn = state.LonghornMode, originalHide = state.LonghornHideTaskbar;
        bool originalAutoHide = state.AutoHide;
        string panelTheme = state.Theme;
        int[] originalPuzzle = picturePuzzle.Tiles.ToArray(); int originalMoves = picturePuzzle.Moves;
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
                if (CalendarGrid.Children.Count != 49 || CalendarGrid.RowDefinitions.Count != 7) throw new Exception("Calendar must retain six complete weeks");
            }
            preferencesWindow?.Close(); CustomizeClock_Click(this, new RoutedEventArgs());
            await Task.Delay(200);
            if (preferencesWindow is null) throw new Exception("Clock cards failed to open");
            state.LonghornMode = true; state.LonghornHideTaskbar = false;
            await UpdateLonghornAsync();
            await Task.Delay(1000);
            if (!state.LonghornMode || shellBridge is null || LonghornSystemButtons.Children.Count != 3 || LonghornLaunchButtons.Visibility != Visibility.Collapsed) throw new Exception("Longhorn coexistence controls did not apply");
            if (LonghornPins.Children.Count != state.Shortcuts.Count) throw new Exception("Pinned icon grid count incorrect");
            foreach (string id in state.GadgetOrder)
            {
                var editor = BuildCardEditor(id);
                var height = editor.Children.OfType<NumberBox>().First(box => box.Header as string == "Locked height (0 = default)");
                int originalHeight = CardStyle(id).Height;
                height.Value = 240;
                if (((Border)GadgetElement(id)).Height != 240) throw new Exception($"{id} height customization failed");
                CardStyle(id).Height = originalHeight;
            }
            ApplyState();
            ShowQuickSettings((FrameworkElement)LonghornSystemButtons.Children[0]); await Task.Delay(1000);
            if (currentShellFlyout is null || !currentShellFlyout.IsOpen) throw new Exception("Own Quick Settings failed to open");
            if (currentShellFlyout.IsConstrainedToRootBounds || currentShellFlyout.Content is not FrameworkElement quickRoot || quickRoot.ActualWidth < 280) throw new Exception("Quick Settings is clipped to the rail");
            await CaptureRailPreviewAsync("quick-settings-layout.png", ShellPanelPresenter(quickRoot));
            currentShellFlyout.Hide();
            foreach (string popupTheme in new[] { "Light", "Dark" })
            {
                state.Theme = popupTheme; SynchronizeAppearance();
                ShowNotifications((FrameworkElement)LonghornSystemButtons.Children[1]); await Task.Delay(500);
                if (currentShellFlyout is null || !currentShellFlyout.IsOpen || currentShellFlyout.IsConstrainedToRootBounds || currentShellFlyout.Content is not FrameworkElement notificationRoot || notificationRoot.ActualWidth < 280 || notificationRoot.ActualTheme.ToString() != popupTheme) throw new Exception("Notification geometry or theme incorrect");
                await CaptureRailPreviewAsync($"notifications-{popupTheme.ToLowerInvariant()}.png", ShellPanelPresenter(notificationRoot));
                currentShellFlyout.Hide();
            }
            state.Theme = panelTheme; SynchronizeAppearance();
            ShowInputLanguages((FrameworkElement)LonghornSystemButtons.Children[2]); await Task.Delay(200);
            if (currentShellFlyout is null || !currentShellFlyout.IsOpen) throw new Exception("Own input switcher failed to open");
            currentShellFlyout.Hide();
            if (LonghornHeader.Visibility != Visibility.Visible || Grid.GetRow(LonghornPanel) != 1 || SidebarWidth != Math.Clamp(state.LonghornWidth, 156, 280)) throw new Exception("4074 rail layout did not apply");
            bool bottomClock = state.LonghornBottomClock;
            var normalOrder = state.GadgetOrder.ToArray();
            state.LonghornBottomClock = true; ApplyState();
            if (!LonghornClockDock.Children.Contains(ClockGadget) || GadgetStack.Children.Contains(ClockGadget) || ClockGadget.Visibility != (state.Clock ? Visibility.Visible : Visibility.Collapsed)) throw new Exception("Bottom clock docking failed");
            state.LonghornBottomClock = false; ApplyState();
            if (!GadgetStack.Children.Contains(ClockGadget) || LonghornClockDock.Children.Count != 0 || !state.GadgetOrder.SequenceEqual(normalOrder)) throw new Exception("Undocking clock changed normal layout");
            state.LonghornBottomClock = bottomClock; ApplyState();
            double calendarHeight = CalendarGadget.Height, puzzleHeight = PuzzleGadget.Height;
            foreach (var date in new[] { new DateTime(2028, 2, 1), new DateTime(2026, 8, 1) }) { month = date; RenderCalendar(); if (CalendarGadget.Height != calendarHeight || CalendarGrid.RowDefinitions.Count != 7) throw new Exception("Calendar resized between months"); }
            RenderPuzzle(); picturePuzzle.Shuffle(); RenderPuzzle();
            if (PuzzleGadget.Height != puzzleHeight || PuzzleBoard.Width != PuzzleBoard.Height || PuzzleBoard.Width > SidebarWidth - 20) throw new Exception("Puzzle escaped stable card bounds");
            month = originalMonth; RenderCalendar();
            await CaptureRailPreviewAsync();
            bool puzzleEnabled = state.PicturePuzzle; string[] railOrder = state.LonghornGadgetOrder.ToArray();
            try
            {
                state.PicturePuzzle = true; state.LonghornGadgetOrder.Remove("Picture puzzle"); state.LonghornGadgetOrder.Insert(0, "Picture puzzle"); ApplyState(); GadgetScroller.ChangeView(null, 0, null);
                var tileButtons = puzzleButtons.Values.ToArray(); int gap = Array.IndexOf(picturePuzzle.Tiles, 0);
                int neighbor = Enumerable.Range(0, 9).First(index => Math.Abs(index / 3 - gap / 3) + Math.Abs(index % 3 - gap % 3) == 1);
                picturePuzzle.Move(neighbor); RenderPuzzle();
                if (!puzzleButtons.Values.SequenceEqual(tileButtons)) throw new Exception("Puzzle moves rebuilt its buttons");
                await CaptureRailPreviewAsync("longhorn-puzzle-layout.png");
            }
            finally { state.PicturePuzzle = puzzleEnabled; state.LonghornGadgetOrder = railOrder.ToList(); ApplyState(); }
            state.LonghornHideTaskbar = true;
            await shellBridge!.SendAsync(new { action = "replace", enabled = true });
            for (int attempt = 0; attempt < 30 && !taskbarReplacementActive; attempt++) await Task.Delay(100);
            GetWindowRect(WinRT.Interop.WindowNative.GetWindowHandle(this), out var full);
            var screen = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).OuterBounds;
            if (!taskbarReplacementActive || full.Top != screen.Y || full.Bottom != screen.Y + screen.Height) throw new Exception($"Replacement height: active={taskbarReplacementActive}, bounds={full.Top}..{full.Bottom}, expected={screen.Y}..{screen.Y + screen.Height}, mode={state.LonghornMode}, error={shellBridge?.LastError}");
            state.AutoHide = true; ApplyState(); desktopActive = true; RevealSidebar(); HideSidebar();
            await Task.Delay(350);
            if (autoHidden || currentReveal != 1) throw new Exception("Desktop visibility must prevent auto-hide");
            desktopActive = false; HideSidebar(); await Task.Delay(350);
            GetWindowRect(WinRT.Interop.WindowNative.GetWindowHandle(this), out var strip);
            int expectedStrip = Math.Max(3, (int)Math.Round(3 * GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d));
            if (!autoHidden || currentReveal != 0 || strip.Right - strip.Left != expectedStrip) throw new Exception("Auto-hide failed to reach edge strip");
            RevealSidebar(); await Task.Delay(80); double midway = currentReveal;
            HideSidebar(); if (currentReveal != midway) throw new Exception("Auto-hide reversal snapped");
            await Task.Delay(350); desktopActive = true; RevealSidebar(); await Task.Delay(350);
            if (autoHidden || currentReveal != 1) throw new Exception("Desktop did not reveal sidebar");
            state.AutoHide = originalAutoHide; ApplyState();
            RestoreTaskbar_Click(this, new RoutedEventArgs());
            for (int attempt = 0; attempt < 30 && taskbarReplacementActive; attempt++) await Task.Delay(100);
            if (!state.LonghornMode || state.LonghornHideTaskbar || taskbarReplacementActive) throw new Exception("Restoring taskbar must keep Longhorn mode enabled");
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
            if (!GadgetStack.Children.Contains(ClockGadget) || LonghornClockDock.Children.Count != 0 || SidebarWidth != Math.Clamp(state.Width, 220, 360)) throw new Exception("Normal sidebar layout was not restored");
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "theme-test-result.txt"), "\nPASS: 4074 top rail, independent width/order, clock docking/undocking, fixed six-week calendar and square puzzle bounds, desktop auto-hide hold and reversal, full monitor height with hidden taskbar, Apps icon grid, hidden app tray, pinned icons, opt-out restoration; side transitions, rapid reversal, screen edges, clock customization.");
        }
        catch (Exception ex) { System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "theme-test-result.txt"), "\nFAIL: " + ex); }
        finally { currentShellFlyout?.Hide(); await Task.Delay(50); picturePuzzle.Restore(originalPuzzle, originalMoves); state.Theme = panelTheme; state.AutoHide = originalAutoHide; state.LonghornMode = originalLonghorn; state.LonghornHideTaskbar = originalHide; await UpdateLonghornAsync(); await MoveSidebarAsync(originalSide); month = originalMonth; Close(); }
    }

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
