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
    private sealed record ShellSnapshot(bool ready, bool hidden = false, bool clean = false, bool cleanSupported = true);
    private void InitializeLonghorn()
    {
        if (!state.LonghornMode) ShellBridge.Restore();
        string[] labels = ["Quick Settings", "Notifications", "Switch input language"];
        string[] glyphs = ["\uE713", "\uE7F4", "\uE8C1"];
        LonghornSystemButtons.ColumnDefinitions.Clear();
        foreach (string label in labels) LonghornSystemButtons.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < labels.Length; i++)
        {
            int action = i;
            IconElement icon = i < 2 ? CreateFluentShellIcon(i == 0 ? "FluentOptions.svg" : "FluentAlert.svg") : new FontIcon { Glyph = glyphs[i], FontSize = 16 };
            var button = new Button { Content = icon, Padding = new Thickness(6), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch };
            ToolTipService.SetToolTip(button, labels[i]); AutomationProperties.SetName(button, labels[i]);
            button.Click += (_, _) =>
            {
                if (action == 0) ShowQuickSettings(button);
                else if (action == 1) ShowNotifications(button);
                else ShowInputLanguages(button);
            };
            Grid.SetColumn(button, i); LonghornSystemButtons.Children.Add(button);
        }
        AppWindow.Closing += async (_, args) =>
        {
            if (shellShutdownPrepared) return;
            args.Cancel = true; shellShutdownPrepared = true; closing = true; shellGeneration++;
            currentShellFlyout?.Hide();
            if (shellBridge is { } bridge) { shellBridge = null; await bridge.DisposeAsync(); }
            // Disconnect native callbacks while the WinUI window and dispatcher still exist.
            trayIcon?.Dispose(); appBar?.Dispose(); SystemBackdrop = null;
            DispatcherQueue.TryEnqueue(() => Close());
        };
        _ = UpdateLonghornAsync();
    }
    private static PathIcon CreateFluentShellIcon(string asset)
    {
        var document = System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", asset));
        string data = document.Descendants().First(element => element.Name.LocalName == "path").Attribute("d")!.Value;
        var icon = (PathIcon)Microsoft.UI.Xaml.Markup.XamlReader.Load("<PathIcon xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='16' Height='16' Data='" + data + "' Foreground='{ThemeResource TextFillColorPrimaryBrush}'/>");
        icon.Data.Transform = new ScaleTransform { ScaleX = 2d / 3, ScaleY = 2d / 3 };
        return icon;
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
            ApplyState(); RefreshQuickLaunch();
            if (!state.LonghornMode || closing) return;
            shellBridge = new ShellBridge(json => DispatcherQueue.TryEnqueue(() =>
            {
                if (generation != shellGeneration || closing) return;
                try
                {
                    var snapshot = JsonSerializer.Deserialize<ShellSnapshot>(json);
                    if (snapshot is null) return;
                    SetTaskbarReplacement(snapshot.hidden && state.LonghornMode);
                    if (state.LonghornHideSystemTray && !state.LonghornHideTaskbar && !snapshot.cleanSupported)
                    { PanelStatus.Text = "Windows taskbar tray could not be identified. Its original layout is kept."; PanelStatus.Visibility = Visibility.Visible; }
                    else if (snapshot.clean && PanelStatus.Text.StartsWith("Windows taskbar tray", StringComparison.Ordinal)) PanelStatus.Visibility = Visibility.Collapsed;
                }
                catch (JsonException) { }
            }), () => DispatcherQueue.TryEnqueue(() =>
            {
                if (generation != shellGeneration || closing) return;
                SetTaskbarReplacement(false); state.LonghornMode = false; ApplyState(); Save();
                PanelStatus.Text = "Longhorn services stopped. Windows taskbar restored."; PanelStatus.Visibility = Visibility.Visible;
            }));
            await UpdateTaskbarOptionsAsync();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            SetTaskbarReplacement(false); state.LonghornMode = false; ApplyState(); Save();
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
        return new SolidColorBrush(theme == ElementTheme.Dark ? Windows.UI.Color.FromArgb(124, 12, 24, 29) : Windows.UI.Color.FromArgb(132, 207, 220, 216));
    }
    private void ApplyLonghornVisuals()
    {
        bool contrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        if (styledLonghorn == state.LonghornMode && styledTheme == Root.RequestedTheme && styledHighContrast == contrast) return;
        styledLonghorn = state.LonghornMode; styledTheme = Root.RequestedTheme; styledHighContrast = contrast;
        LonghornHeader.Visibility = LonghornBottom.Visibility = LonghornPanel.Visibility = state.LonghornMode ? Visibility.Visible : Visibility.Collapsed;
        Root.Padding = state.LonghornMode ? new Thickness(5, 3, 5, 4) : new Thickness(10, 8, 10, 10);
        Root.RowSpacing = state.LonghornMode ? 3 : 8;
        GadgetStack.Spacing = state.LonghornMode ? 0 : 12;
        GadgetScroller.Padding = state.LonghornMode ? new Thickness(0) : new Thickness(0, 0, 4, 0);
        MonthText.FontSize = state.LonghornMode ? 13 : 16;
        DateText.FontSize = state.LonghornMode ? 11 : 12;
        TimeText.FontSize = state.LonghornMode ? 14 : 24;
        ApplyLonghornSections();
        Root.Background = RailSurfaceBrush(Root.RequestedTheme);
        bool dark = Root.RequestedTheme == ElementTheme.Dark;
        foreach (var tile in new[] { ClockGadget, CalendarGadget, NotesGadget, SystemGadget, TimerCategory, StorageCategory, LaunchCategory, SlideShowGadget, PuzzleGadget, FeedsGadget })
        {
            if (state.LonghornMode && !contrast)
            {
                tile.CornerRadius = new CornerRadius(0); tile.Padding = new Thickness(4, 8, 4, 8);
                tile.Background = new SolidColorBrush(Colors.Transparent);
                tile.BorderBrush = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(42, 224, 240, 248) : Windows.UI.Color.FromArgb(38, 30, 50, 60));
                tile.BorderThickness = new Thickness(0, 1, 0, 0);
            }
            else
            {
                tile.ClearValue(Border.CornerRadiusProperty); tile.ClearValue(Border.PaddingProperty);
                tile.ClearValue(Border.BackgroundProperty); tile.ClearValue(Border.BorderBrushProperty); tile.ClearValue(Border.BorderThicknessProperty);
            }
        }
        if (ready) { RenderCalendar(); clockFaceDirty = true; UpdateGadgets(); }
    }

    private void ApplyLonghornSections()
    {
        LonghornLaunchButtons.Visibility = state.LonghornHideTaskbar ? Visibility.Visible : Visibility.Collapsed;
        LonghornPinsSection.Visibility = state.LonghornShowPins && (state.Shortcuts?.Count ?? 0) > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private int railSearchGeneration;
    private async void LonghornSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        int generation = ++railSearchGeneration;
        string query = sender.Text.Trim();
        if (query.Length == 0) { sender.ItemsSource = null; return; }
        try
        {
            var apps = await InstalledApps.GetAsync();
            if (generation != railSearchGeneration || closing || !state.LonghornMode) return;
            sender.ItemsSource = apps.Where(app => app.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).Take(8).ToArray();
        }
        catch (Exception) { if (generation == railSearchGeneration) sender.ItemsSource = null; }
    }
    private void LonghornSearch_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    { if (args.SelectedItem is InstalledApp app) sender.Text = app.Name; }
    private async void LonghornSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is InstalledApp app)
            await OpenShortcut(new LaunchShortcut { Name = app.Name, Target = app.Target });
        else if (!string.IsNullOrWhiteSpace(args.QueryText))
        {
            try { await Windows.System.Launcher.LaunchUriAsync(new Uri("search-ms:query=" + Uri.EscapeDataString(args.QueryText.Trim()))); }
            catch (Exception ex) when (ex is COMException or ArgumentException) { PanelStatus.Text = "Windows Search could not open."; PanelStatus.Visibility = Visibility.Visible; }
        }
        sender.IsSuggestionListOpen = false;
    }

    private void LonghornStart_Click(object sender, RoutedEventArgs e) => SendWindowsKey(0);
    private async void LonghornApps_Click(object sender, RoutedEventArgs e)
    {
        if (dialogOpen) { ActivatePreferences(); return; }
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
        state.LonghornHideTaskbar = false; ApplyLonghornSections(); Save();
        if (shellBridge is not null) await UpdateTaskbarOptionsAsync();
        else { ShellBridge.Restore(); SetTaskbarReplacement(false); }
    }
    private Task UpdateTaskbarOptionsAsync() => shellBridge?.SendAsync(new { action = "replace", enabled = state.LonghornHideTaskbar, clean = state.LonghornHideSystemTray }) ?? Task.CompletedTask;
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct ShellKeyInput { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public ushort Key; [FieldOffset(12)] public uint Flags; }
    private void SendWindowsKey(ushort key)
    {
        var keys = key == 0 ? new[] { new ShellKeyInput { Type = 1, Key = 0x5B }, new ShellKeyInput { Type = 1, Key = 0x5B, Flags = 2 } } : new[] { new ShellKeyInput { Type = 1, Key = 0x5B }, new ShellKeyInput { Type = 1, Key = key }, new ShellKeyInput { Type = 1, Key = key, Flags = 2 }, new ShellKeyInput { Type = 1, Key = 0x5B, Flags = 2 } };
        if (SendInput((uint)keys.Length, keys, Marshal.SizeOf<ShellKeyInput>()) != keys.Length) { PanelStatus.Text = "Windows could not open that panel."; PanelStatus.Visibility = Visibility.Visible; }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, ShellKeyInput[] inputs, int size);
}
