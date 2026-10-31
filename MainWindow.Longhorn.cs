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
    private int shellGeneration;
    private bool shellShutdownPrepared;
    private sealed record ShellEntry(string id, string title, string icon, bool active = false);
    private sealed record ShellSnapshot(ShellEntry[] windows, ShellEntry[] tray, bool ready);
    private void InitializeLonghorn()
    {
        if (!state.LonghornMode) ShellBridge.Restore();
        string[] labels = ["Quick Settings", "Notifications", "Switch input language", "Windows Settings"];
        string[] glyphs = ["\uE713", "\uE7F4", "\uE8C1", "\uE115"];
        for (int i = 0; i < labels.Length; i++)
        {
            int action = i;
            var button = new Button { Content = new FontIcon { Glyph = glyphs[i], FontSize = 16 }, Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Stretch };
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
                    RenderShellEntries(RunningItems, snapshot.windows, false);
                    RenderShellEntries(ShellTrayItems, snapshot.tray, true);
                    if (state.LonghornHideTaskbar && shellBridge is { } current) _ = current.SendAsync(new { action = "replace", enabled = true });
                }
                catch (JsonException) { }
            }), () => DispatcherQueue.TryEnqueue(() =>
            {
                if (generation != shellGeneration || closing) return;
                state.LonghornMode = false; LonghornPanel.Visibility = Visibility.Collapsed; ApplyGadgetPage(); Save();
                PanelStatus.Text = "Longhorn services stopped. Windows taskbar restored."; PanelStatus.Visibility = Visibility.Visible;
            }));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            state.LonghornMode = false; LonghornPanel.Visibility = Visibility.Collapsed; ApplyGadgetPage(); Save();
            PanelStatus.Text = "Longhorn services could not start. Windows taskbar is available."; PanelStatus.Visibility = Visibility.Visible;
        }
        finally { changingLonghorn = false; }
    }
    private void RenderShellEntries(Grid grid, ShellEntry[] items, bool tray)
    {
        grid.Children.Clear(); grid.RowDefinitions.Clear();
        int columns = tray ? 6 : 4;
        foreach (var item in items)
        {
            int index = grid.Children.Count;
            if (index % columns == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var content = new Grid { Width = tray ? 18 : 24, Height = tray ? 18 : 24 };
            var fallback = new FontIcon { Glyph = tray ? "\uE9CE" : "\uE737", FontSize = 16 }; content.Children.Add(fallback);
            var image = new Image { Width = tray ? 18 : 24, Height = tray ? 18 : 24, Visibility = Visibility.Collapsed }; content.Children.Add(image);
            var button = new Button { Content = content, Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = item.active ? new Thickness(0, 0, 0, 2) : new Thickness(0), BorderBrush = CpuBar.Foreground };
            ToolTipService.SetToolTip(button, item.title); AutomationProperties.SetName(button, string.IsNullOrWhiteSpace(item.title) ? "Tray icon" : item.title);
            button.Click += async (_, _) => await ShellEntryCommand(item, tray ? "tray" : "window");
            if (tray) button.RightTapped += async (_, args) => { args.Handled = true; await ShellEntryCommand(item, "trayMenu"); };
            else
            {
                var menu = new MenuFlyout(); var close = new MenuFlyoutItem { Text = "Close window" };
                close.Click += async (_, _) => await ShellEntryCommand(item, "close"); menu.Items.Add(close); button.ContextFlyout = menu;
            }
            Grid.SetColumn(button, index % columns); Grid.SetRow(button, index / columns); grid.Children.Add(button);
            if (item.icon.Length > 0) LoadShortcutIcon(new LaunchShortcut { Target = "shell-icon:" + item.id + item.icon.GetHashCode(), IconData = item.icon, IconVersion = 2 }, image, fallback);
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
        var list = new ListView { Height = 360 }; panel.Children.Add(list);
        var apps = await InstalledApps.GetAsync();
        List<InstalledApp> visible = [];
        void Filter() { visible = apps.Where(a => a.Name.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList(); list.ItemsSource = visible.Select(a => a.Name).ToArray(); }
        search.TextChanged += (_, _) => Filter(); Filter();
        list.ItemClick += async (_, args) => { int index = Array.IndexOf(visible.Select(a => a.Name).ToArray(), args.ClickedItem as string); if (index >= 0 && index < visible.Count) { var app = visible[index]; await OpenShortcut(new LaunchShortcut { Name = app.Name, Target = app.Target }); preferencesWindow?.Close(); } };
        list.IsItemClickEnabled = true;
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
