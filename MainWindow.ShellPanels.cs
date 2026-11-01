using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Sidebar;

public partial class MainWindow
{
    private bool shellPanelOpen;
    private Flyout? currentShellFlyout;
    private IntPtr lastExternalWindow;
    private RadioAccessStatus? radioAccess;
    private readonly List<(DateTime Time, string Title, string Text)> localNotifications = new();

    private Flyout NewShellPanel(FrameworkElement anchor, string title, out StackPanel panel)
    {
        currentShellFlyout?.Hide();
        panel = new StackPanel { Width = 280, Spacing = 12, RequestedTheme = Root.RequestedTheme };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var flyout = new Flyout { Content = new ScrollViewer { Content = panel, MaxHeight = 500, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, Placement = state.Left ? FlyoutPlacementMode.Right : FlyoutPlacementMode.Left, ShouldConstrainToRootBounds = false, SystemBackdrop = new SidebarBackdrop(false) };
        flyout.Opened += (_, _) => { shellPanelOpen = true; hideTimer.Stop(); SynchronizeShellPanelTheme(); };
        currentShellFlyout = flyout;
        flyout.Closed += (_, _) => { if (ReferenceEquals(currentShellFlyout, flyout)) { currentShellFlyout = null; shellPanelOpen = false; QueueAutoHide(); } };
        SynchronizeShellPanelTheme(); flyout.ShowAt(anchor); return flyout;
    }
    private void SynchronizeShellPanelTheme()
    {
        if (currentShellFlyout is not { } flyout) return;
        var theme = Root.RequestedTheme;
        var ink = new Microsoft.UI.Xaml.Media.SolidColorBrush(theme == ElementTheme.Dark ? Microsoft.UI.Colors.White : Windows.UI.Color.FromArgb(255, 28, 28, 28));
        var style = new Style { TargetType = typeof(FlyoutPresenter) };
        style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, theme));
        style.Setters.Add(new Setter(Control.ForegroundProperty, ink));
        style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceBrush(theme, 240)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 312d));
        style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 312d));
        flyout.FlyoutPresenterStyle = style;
        if (flyout.SystemBackdrop is SidebarBackdrop backdrop) backdrop.SetTheme(theme);
        if (flyout.Content is FrameworkElement content)
        {
            content.RequestedTheme = theme;
            if (content is ScrollViewer scroll) scroll.Background = SurfaceBrush(theme, 240);
            void SetText(DependencyObject node)
            {
                if (node is TextBlock text) text.Foreground = ink;
                for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); i++) SetText(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i));
            }
            SetText(content);
        }
    }
    private async void ShowQuickSettings(FrameworkElement anchor)
    {
        var flyout = NewShellPanel(anchor, "Quick Settings", out var panel);
        bool closed = false; flyout.Closed += (_, _) => closed = true;
        var status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(status);
        try
        {
            var connection = NetworkInformation.GetInternetConnectionProfile();
            status.Text = connection is null ? "Not connected" : connection.IsWlanConnectionProfile ? connection.WlanConnectionProfileDetails.GetConnectedSsid() : connection.ProfileName;
        }
        catch (Exception) { status.Text = "Network status unavailable"; }
        try
        {
            var audio = new AudioEndpoint(); bool updating = false;
            var volume = new Slider { Header = "Volume", Minimum = 0, Maximum = 100, Value = audio.Volume, StepFrequency = 1 };
            var mute = new ToggleSwitch { Header = "Mute", IsOn = audio.Muted };
            volume.ValueChanged += (_, args) => { if (updating || closed) return; try { if (Math.Abs(audio.Volume - args.NewValue) > 0.5) audio.Volume = args.NewValue; } catch (COMException) { status.Text = "Audio device changed. Reopen Quick Settings."; } };
            mute.Toggled += (_, _) => { if (updating || closed) return; try { if (audio.Muted != mute.IsOn) audio.Muted = mute.IsOn; } catch (COMException) { status.Text = "Audio device unavailable"; } };
            panel.Children.Add(volume); panel.Children.Add(mute);
            var refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
            refresh.Tick += (_, _) => { if (closed) { refresh.Stop(); return; } try { updating = true; volume.Value = audio.Volume; mute.IsOn = audio.Muted; } catch (Exception ex) when (ex is COMException or ObjectDisposedException) { refresh.Stop(); volume.IsEnabled = mute.IsEnabled = false; } finally { updating = false; } };
            refresh.Start(); flyout.Closed += (_, _) => { refresh.Stop(); audio.Dispose(); };
        }
        catch (COMException) { panel.Children.Add(new TextBlock { Text = "No audio output device", FontSize = 12 }); }
        try
        {
            var radios = await Radio.GetRadiosAsync(); if (closed) return;
            foreach (var radio in radios.Where(item => item.Kind is RadioKind.WiFi or RadioKind.Bluetooth))
            {
                bool synchronizing = false;
                var toggle = new ToggleSwitch { Header = radio.Kind == RadioKind.WiFi ? "Wi-Fi" : "Bluetooth", IsOn = radio.State == RadioState.On, IsEnabled = radio.State != RadioState.Disabled };
                Windows.Foundation.TypedEventHandler<Radio, object> changed = (_, _) => DispatcherQueue.TryEnqueue(() => { if (closed) return; synchronizing = true; toggle.IsOn = radio.State == RadioState.On; toggle.IsEnabled = radio.State != RadioState.Disabled; synchronizing = false; });
                radio.StateChanged += changed; flyout.Closed += (_, _) => radio.StateChanged -= changed;
                toggle.Toggled += async (_, _) =>
                {
                    if (synchronizing || closed || toggle.IsOn == (radio.State == RadioState.On)) return;
                    toggle.IsEnabled = false;
                    try
                    {
                        radioAccess ??= await Radio.RequestAccessAsync();
                        if (radioAccess == RadioAccessStatus.Allowed)
                        { var result = await radio.SetStateAsync(toggle.IsOn ? RadioState.On : RadioState.Off); if (result != RadioAccessStatus.Allowed) status.Text = "Windows did not allow that radio change."; }
                        else status.Text = "Windows has not allowed radio control for FluentSidebar.";
                    }
                    catch (Exception ex) when (ex is COMException or UnauthorizedAccessException) { status.Text = "Radio control is unavailable on this device."; }
                    finally { if (!closed) { synchronizing = true; toggle.IsOn = radio.State == RadioState.On; toggle.IsEnabled = radio.State != RadioState.Disabled; synchronizing = false; } }
                };
                panel.Children.Add(toggle);
            }
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException) { panel.Children.Add(new TextBlock { Text = "Wireless controls unavailable", FontSize = 12 }); }
    }

    private async void ShowNotifications(FrameworkElement anchor)
    {
        var flyout = NewShellPanel(anchor, "Notifications", out var panel);
        bool closed = false; flyout.Closed += (_, _) => closed = true;
        var status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(status);
        var items = new StackPanel { Spacing = 10 }; panel.Children.Add(items);
        void Card(string title, string text, string time)
        { var content = new StackPanel { Spacing = 4 }; content.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }); content.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxLines = 5 }); content.Children.Add(new TextBlock { Text = time, FontSize = 11, Opacity = 0.6 }); items.Children.Add(new Border { Child = content, Padding = new Thickness(10), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Root.Resources.LookupOrApplication("CardStrokeColorDefaultBrush") }); }
        foreach (var item in localNotifications.AsEnumerable().Reverse()) Card(item.Title, item.Text, item.Time.ToString("t"));
        var request = new Button { Content = "Allow Windows notifications", HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(request);
        uint packageNameLength = 0;
        if (GetCurrentPackageFullName(ref packageNameLength, IntPtr.Zero) == 15700)
        {
            status.Text = localNotifications.Count == 0 ? "No notifications yet. Timer alerts will appear here. Windows notifications aren't available in this build." : "FluentSidebar alerts. Windows notifications aren't available in this build.";
            request.Visibility = Visibility.Collapsed; SynchronizeShellPanelTheme(); return;
        }
        async Task Load(bool ask)
        {
            request.IsEnabled = false;
            try
            {
                var listener = UserNotificationListener.Current;
                var access = ask ? await listener.RequestAccessAsync() : listener.GetAccessStatus();
                if (closed) return;
                if (access != UserNotificationListenerAccessStatus.Allowed) { status.Text = localNotifications.Count == 0 ? "No FluentSidebar alerts. Allow access to also show Windows notifications." : "FluentSidebar alerts. Windows notification access is not enabled."; return; }
                var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
                if (closed) return;
                request.Visibility = Visibility.Collapsed;
                foreach (var notification in notifications.OrderByDescending(item => item.CreationTime).Take(30))
                { var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric); string text = binding is null ? "" : string.Join("\n", binding.GetTextElements().Select(element => element.Text)); Card(notification.AppInfo.DisplayInfo.DisplayName, text, notification.CreationTime.LocalDateTime.ToString("t")); }
                status.Text = items.Children.Count == 0 ? "You're all caught up." : "Recent notifications";
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or NotSupportedException)
            { if (!closed) status.Text = "Windows-wide notifications require a packaged build with notification-listener permission. FluentSidebar alerts appear here."; }
            finally { if (!closed) { request.IsEnabled = true; SynchronizeShellPanelTheme(); } }
        }
        request.Click += async (_, _) => await Load(true);
        await Load(false);
    }
    private void AddLocalNotification(string title, string text)
    { localNotifications.Add((DateTime.Now, title, text)); if (localNotifications.Count > 30) localNotifications.RemoveAt(0); }

    private void ShowInputLanguages(FrameworkElement anchor)
    {
        var flyout = NewShellPanel(anchor, "Input language", out var panel);
        IntPtr target = lastExternalWindow;
        int count = GetKeyboardLayoutList(0, null);
        if (count <= 0) { panel.Children.Add(new TextBlock { Text = "No keyboard layouts available" }); return; }
        var layouts = new IntPtr[count]; GetKeyboardLayoutList(layouts.Length, layouts);
        IntPtr active = GetKeyboardLayout(target == IntPtr.Zero ? 0 : GetWindowThreadProcessId(target, out _));
        foreach (var layout in layouts)
        {
            string label;
            try { label = System.Globalization.CultureInfo.GetCultureInfo((int)(layout.ToInt64() & 0xffff)).DisplayName; } catch (System.Globalization.CultureNotFoundException) { label = "Keyboard layout"; }
            string identifier = unchecked((uint)layout.ToInt64()).ToString("X8");
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts\" + identifier))
                if (key?.GetValue("Layout Text") is string text) label = text;
            var button = new Button { Content = (layout == active ? "✓  " : "     ") + label, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => { if (target != IntPtr.Zero && IsWindow(target)) { PostMessage(target, 0x0050, IntPtr.Zero, layout); SetForegroundWindow(target); } else ActivateKeyboardLayout(layout, 0); flyout.Hide(); };
            panel.Children.Add(button);
        }
        SynchronizeShellPanelTheme();
    }
    [DllImport("user32.dll")] private static extern int GetKeyboardLayoutList(int count, [Out] IntPtr[]? layouts);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern IntPtr ActivateKeyboardLayout(IntPtr layout, uint flags);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
}
