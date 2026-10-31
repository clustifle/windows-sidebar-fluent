using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace Sidebar;
public partial class MainWindow
{
    private ShellBridge? shellBridge;
    private bool changingLonghorn;
    private bool taskbarReplacementActive;
    private bool? styledLonghorn;
    private ElementTheme styledTheme;
    private bool styledHighContrast;
    private int shellGeneration;
    private bool shellShutdownPrepared;
    private sealed record ShellEntry(string id, string title, string icon, bool active = false);
    private sealed record ShellSnapshot(ShellEntry[] windows, ShellEntry[] tray, bool ready, bool hidden = false);
    private void InitializeLonghorn()
    {
        if (!state.LonghornMode) ShellBridge.Restore();
        string[] labels = ["Quick Settings", "Notifications", "Switch input language", "Windows Settings"];
        string[] glyphs = ["\uE713", "\uE7F4", "\uE8C1", "\uE115"];
        for (int i = 0; i < labels.Length; i++)
        {
            int action = i;
            var button = new Button { Content = new FontIcon { Glyph = glyphs[i], FontSize = 14 }, Padding = new Thickness(6), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch };
            ToolTipService.SetToolTip(button, labels[i]); AutomationProperties.SetName(button, labels[i]);
            button.Click += async (_, _) =>
            {
                if (action == 3) await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:"));
                else SendWindowsKey(action == 0 ? (ushort)0x41 : action == 1 ? (ushort)0x4E : (ushort)0x20);
            };
            Grid.SetColumn(button, i); LonghornSystemButtons.Children.Add(button);
        }
        AppWindow.Closing += async (_, args) =>
        {
            if (shellShutdownPrepared) return;
            args.Cancel = true; shellShutdownPrepared = true; closing = true; shellGeneration++;
            if (shellBridge is { } bridge) { shellBridge = null; await bridge.DisposeAsync(); }
            // Disconnect native callbacks while the WinUI window and dispatcher still exist.
            trayIcon?.Dispose(); appBar?.Dispose(); SystemBackdrop = null;
            DispatcherQueue.TryEnqueue(() => Close());
        };
        _ = UpdateLonghornAsync();
    }
    private async Task UpdateLonghornAsync()
    {
        if (changingLonghorn) return;
        changingLonghorn = true;
        try
        {
            int generation = ++shellGeneration;
            if (shellBridge is { } previous) { shellBridge = null; await previous.DisposeAsync(); }
            SetTaskbarReplacement(false);
            ApplyLonghornVisuals();
            LonghornPanel.Visibility = state.LonghornMode ? Visibility.Visible : Visibility.Collapsed;
            ApplyGadgetPage(); RefreshQuickLaunch();
            if (!state.LonghornMode || closing) return;
            shellBridge = new ShellBridge(json => DispatcherQueue.TryEnqueue(() =>
            {
                if (generation != shellGeneration || closing) return;
                try
                {
                    var snapshot = JsonSerializer.Deserialize<ShellSnapshot>(json);
                    if (snapshot is null) return;
                    SetTaskbarReplacement(snapshot.hidden && state.LonghornMode);
                    RenderShellEntries(RunningItems, snapshot.windows, false);
                    RenderShellEntries(ShellTrayItems, snapshot.tray, true);
                    if (state.LonghornHideTaskbar && !snapshot.hidden && shellBridge is { } current) _ = current.SendAsync(new { action = "replace", enabled = true });
                }
                catch (JsonException) { }
            }), () => DispatcherQueue.TryEnqueue(() =>
            {
                if (generation != shellGeneration || closing) return;
                SetTaskbarReplacement(false); state.LonghornMode = false; ApplyLonghornVisuals(); LonghornPanel.Visibility = Visibility.Collapsed; ApplyGadgetPage(); Save();
                PanelStatus.Text = "Longhorn services stopped. Windows taskbar restored."; PanelStatus.Visibility = Visibility.Visible;
            }));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            SetTaskbarReplacement(false); state.LonghornMode = false; ApplyLonghornVisuals(); LonghornPanel.Visibility = Visibility.Collapsed; ApplyGadgetPage(); Save();
            PanelStatus.Text = "Longhorn services could not start. Windows taskbar is available."; PanelStatus.Visibility = Visibility.Visible;
        }
        finally { changingLonghorn = false; }
    }
    private void SetTaskbarReplacement(bool active)
    {
        if (taskbarReplacementActive == active) return;
        taskbarReplacementActive = active;
        PositionSidebar();
    }
    private Brush RailSurfaceBrush(ElementTheme theme)
    {
        if (!state.LonghornMode || new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) return SurfaceBrush(theme, 128);
        return new SolidColorBrush(theme == ElementTheme.Dark ? Windows.UI.Color.FromArgb(184, 24, 37, 46) : Windows.UI.Color.FromArgb(168, 222, 232, 233));
    }
    private void ApplyLonghornVisuals()
    {
        bool contrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        if (styledLonghorn == state.LonghornMode && styledTheme == Root.RequestedTheme && styledHighContrast == contrast) return;
        if (!state.LonghornMode) ClearShellEntries();
        styledLonghorn = state.LonghornMode; styledTheme = Root.RequestedTheme; styledHighContrast = contrast;
        Root.Padding = state.LonghornMode ? new Thickness(4, 4, 4, 4) : new Thickness(10, 8, 10, 10);
        Root.RowSpacing = state.LonghornMode ? 4 : 8;
        GadgetStack.Spacing = state.LonghornMode ? 3 : 12;
        MonthText.FontSize = state.LonghornMode ? 13 : 16;
        DateText.FontSize = state.LonghornMode ? 11 : 12;
        Root.Background = RailSurfaceBrush(Root.RequestedTheme);
        bool dark = Root.RequestedTheme == ElementTheme.Dark;
        foreach (var tile in new[] { ClockGadget, CalendarGadget, NotesGadget, SystemGadget, TimerCategory, StorageCategory, LaunchCategory, SlideShowGadget, PuzzleGadget, FeedsGadget })
        {
            if (state.LonghornMode && !contrast)
            {
                tile.CornerRadius = new CornerRadius(0); tile.Padding = new Thickness(6);
                tile.Background = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(18, 224, 240, 248) : Windows.UI.Color.FromArgb(76, 255, 255, 255));
                tile.BorderBrush = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(30, 224, 240, 248) : Windows.UI.Color.FromArgb(26, 30, 50, 60));
                tile.BorderThickness = new Thickness(0, 0, 0, 1);
            }
            else
            {
                tile.ClearValue(Border.CornerRadiusProperty); tile.ClearValue(Border.PaddingProperty);
                tile.ClearValue(Border.BackgroundProperty); tile.ClearValue(Border.BorderBrushProperty); tile.ClearValue(Border.BorderThicknessProperty);
            }
        }
        if (ready) { RenderCalendar(); clockFaceDirty = true; UpdateGadgets(); }
    }

    private void ClearShellEntries()
    {
        RunningItems.Children.Clear(); RunningItems.RowDefinitions.Clear();
        ShellTrayItems.Children.Clear(); ShellTrayItems.RowDefinitions.Clear();
        shellIconViews.Clear();
        foreach (string key in shortcutImages.Keys.Where(key => key.StartsWith("shell-icon:", StringComparison.Ordinal)).ToArray()) shortcutImages.Remove(key);
    }

    private sealed class ShellIconView
    {
        public required ShellEntry Item;
        public required Button Button;
        public required Image Image;
        public required FontIcon Fallback;
    }
    private readonly Dictionary<Grid, Dictionary<string, ShellIconView>> shellIconViews = new();
    private void RenderShellEntries(Grid grid, ShellEntry[] items, bool tray)
    {
        if (!shellIconViews.TryGetValue(grid, out var views)) shellIconViews[grid] = views = new(StringComparer.Ordinal);
        var keys = items.Select(item => item.id).ToHashSet(StringComparer.Ordinal);
        foreach (string removed in views.Keys.Where(key => !keys.Contains(key)).ToArray())
        { grid.Children.Remove(views[removed].Button); views.Remove(removed); }
        int columns = tray ? 8 : 5;
        int rows = (items.Length + columns - 1) / columns;
        while (grid.RowDefinitions.Count > rows) grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);
        while (grid.RowDefinitions.Count < rows) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int index = 0; index < items.Length; index++)
        {
            var item = items[index];
            bool created = !views.TryGetValue(item.id, out var view);
            if (created)
            {
                var content = new Grid { Width = tray ? 14 : 20, Height = tray ? 14 : 20 };
                var fallback = new FontIcon { Glyph = tray ? "\uE9CE" : "\uE737", FontSize = 16 }; content.Children.Add(fallback);
                var image = new Image { Width = tray ? 14 : 20, Height = tray ? 14 : 20, Visibility = Visibility.Collapsed }; content.Children.Add(image);
                var button = new Button { Content = content, Padding = new Thickness(tray ? 3 : 4), HorizontalAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(Colors.Transparent), BorderBrush = CpuBar.Foreground };
                view = new ShellIconView { Item = item, Button = button, Image = image, Fallback = fallback }; views.Add(item.id, view);
                button.Click += async (_, _) => await ShellEntryCommand(view.Item, tray ? "tray" : "window");
                if (tray) button.RightTapped += async (_, args) => { args.Handled = true; await ShellEntryCommand(view.Item, "trayMenu"); };
                else
                {
                    var menu = new MenuFlyout(); var close = new MenuFlyoutItem { Text = "Close window" };
                    close.Click += async (_, _) => await ShellEntryCommand(view.Item, "close"); menu.Items.Add(close); button.ContextFlyout = menu;
                }
                grid.Children.Add(button);
            }
            if (created || view!.Item.title != item.title)
            { ToolTipService.SetToolTip(view!.Button, item.title); AutomationProperties.SetName(view.Button, string.IsNullOrWhiteSpace(item.title) ? "Tray icon" : item.title); }
            if (created || view!.Item.active != item.active) view!.Button.BorderThickness = item.active ? new Thickness(0, 0, 0, 2) : new Thickness(0);
            if ((created || view!.Item.icon != item.icon) && item.icon.Length > 0)
                LoadShortcutIcon(new LaunchShortcut { Target = "shell-icon:" + item.id + item.icon.GetHashCode(), IconData = item.icon, IconVersion = 2 }, view!.Image, view.Fallback, false);
            view!.Item = item; view.Button.TabIndex = index;
            Grid.SetColumn(view.Button, index % columns); Grid.SetRow(view.Button, index / columns);
        }
        if (shortcutImages.Count > 256) shortcutImages.Clear();
    }

    private async Task ShellEntryCommand(ShellEntry entry, string action)
    {
        if (shellBridge is null) return;
        GetCursorPos(out var point);
        await shellBridge.SendAsync(new { action, id = entry.id, x = point.X, y = point.Y });
    }
    private void LonghornStart_Click(object sender, RoutedEventArgs e) => SendWindowsKey(0);
    private async void LonghornApps_Click(object sender, RoutedEventArgs e)
    {
        if (dialogOpen) { preferencesWindow?.Activate(); return; }
        var panel = new StackPanel { Spacing = 12 };
        var search = new TextBox { PlaceholderText = "Search installed apps" }; panel.Children.Add(search);
        var list = new GridView { Height = 380, SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true };
        list.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsWrapGrid Orientation='Horizontal' ItemWidth='92' ItemHeight='96'/></ItemsPanelTemplate>");
        list.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><StackPanel Width='80' Padding='4' Spacing='6'><Grid Width='32' Height='32'><FontIcon x:Name='AppFallback' Glyph='&#xE8A5;' FontSize='26'/><Image x:Name='AppIcon' Width='32' Height='32'/></Grid><TextBlock x:Name='AppName' Text='{Binding Name}' FontSize='12' TextAlignment='Center' TextWrapping='Wrap' MaxLines='2' TextTrimming='CharacterEllipsis'/></StackPanel></DataTemplate>");
        void Realize(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer.ContentTemplateRoot is not FrameworkElement root) { if (!args.InRecycleQueue) args.RegisterUpdateCallback(Realize); return; }
            if (root.FindName("AppIcon") is not Image image || root.FindName("AppFallback") is not FontIcon fallback) return;
            if (args.InRecycleQueue) { image.Tag = null; image.Source = null; return; }
            if (args.Item is not InstalledApp app) return;
            AutomationProperties.SetName(args.ItemContainer, app.Name);
            image.Source = null; image.Visibility = Visibility.Collapsed; fallback.Visibility = Visibility.Visible;
            LoadShortcutIcon(new LaunchShortcut { Name = app.Name, Target = app.Target }, image, fallback, false);
        }
        list.ContainerContentChanging += Realize;
        panel.Children.Add(list);
        IReadOnlyList<InstalledApp> apps = [];
        bool loaded = false;
        var status = new TextBlock { Text = "Loading apps…", FontSize = 12, Opacity = 0.65 };
        panel.Children.Insert(1, status);
        void Filter()
        {
            var matches = apps.Where(a => a.Name.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
            list.ItemsSource = matches;
            if (loaded) { status.Text = "No matching apps"; status.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed; }
        }
        async Task LoadApps()
        {
            try { apps = await InstalledApps.GetAsync(); loaded = true; Filter(); }
            catch (Exception) { status.Text = "Windows could not list installed apps."; }
        }
        search.TextChanged += (_, _) => Filter(); Filter();
        list.ItemClick += async (_, args) =>
        {
            if (args.ClickedItem is not InstalledApp app) return;
            await OpenShortcut(new LaunchShortcut { Name = app.Name, Target = app.Target }); preferencesWindow?.Close();
        };
        _ = LoadApps();
        await ShowDialog("Apps", panel);
    }
    private async void RestoreTaskbar_Click(object sender, RoutedEventArgs e)
    {
        state.LonghornMode = false; state.LonghornHideTaskbar = false; Save(); await UpdateLonghornAsync(); ShellBridge.Restore();
    }
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct ShellKeyInput { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public ushort Key; [FieldOffset(12)] public uint Flags; }
    private void SendWindowsKey(ushort key)
    {
        var keys = key == 0 ? new[] { new ShellKeyInput { Type = 1, Key = 0x5B }, new ShellKeyInput { Type = 1, Key = 0x5B, Flags = 2 } } : new[] { new ShellKeyInput { Type = 1, Key = 0x5B }, new ShellKeyInput { Type = 1, Key = key }, new ShellKeyInput { Type = 1, Key = key, Flags = 2 }, new ShellKeyInput { Type = 1, Key = 0x5B, Flags = 2 } };
        if (SendInput((uint)keys.Length, keys, Marshal.SizeOf<ShellKeyInput>()) != keys.Length) { PanelStatus.Text = "Windows could not open that panel."; PanelStatus.Visibility = Visibility.Visible; }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, ShellKeyInput[] inputs, int size);
}
