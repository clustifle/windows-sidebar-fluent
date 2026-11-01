using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Sidebar;

public partial class MainWindow
{
    private Flyout? launcherFlyout;
    private Grid? launcherSurface;
    private Action<int>? selectLauncherView;
    private void OpenLauncher_Click(object sender, RoutedEventArgs args) => ShowLauncherFlyout(0);
    private void OpenPinnedLauncher() => ShowLauncherFlyout(1);

    private void ShowLauncherFlyout(int initialView)
    {
        if (closing || sidebarClosed || sideMoveRunning) return;
        if (launcherFlyout?.IsOpen == true) { selectLauncherView?.Invoke(initialView); return; }
        RevealSidebar(); LonghornSearch.IsSuggestionListOpen = false; puzzlePopup?.Hide();
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        double scale = Root.XamlRoot?.RasterizationScale ?? 1;
        double width = Math.Min(392, Math.Max(196, area.Width / scale - SidebarWidth - 32));
        double availableHeight = Math.Min(520, Math.Max(180, area.Height / scale - 32));
        int rows = Math.Clamp((int)Math.Floor((availableHeight - 130) / 104), 1, 4);
        double height = Math.Min(availableHeight, 130 + rows * 104);
        var search = new TextBox { Name = "LauncherSearch", PlaceholderText = "Search apps and pinned websites", MaxLength = 160 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, "Search launcher");
        var status = new TextBlock { Text = "Loading apps…", FontSize = 11, Height = 30, TextWrapping = TextWrapping.Wrap, MaxLines = 2 };
        var grid = new GridView { Name = "LauncherApps", Padding = new Thickness(0), IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.Single, ItemTemplate = (DataTemplate)Root.Resources["LauncherTileTemplate"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(grid, "Apps");
        var itemStyle = new Style { TargetType = typeof(GridViewItem) };
        itemStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty, 88d));
        itemStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 100d));
        itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(2)));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        grid.ItemContainerStyle = itemStyle;
        var tasks = new ListView { IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.Single, ItemTemplate = (DataTemplate)Root.Resources["AppSearchResultTemplate"], Visibility = Visibility.Collapsed };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tasks, "Running windows");
        var content = new Grid(); content.Children.Add(grid); content.Children.Add(tasks);
        var tabs = new Grid { ColumnSpacing = 4 };
        for (int i = 0; i < 3; i++) tabs.ColumnDefinitions.Add(new ColumnDefinition());
        tabs.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var buttons = new List<ToggleButton>();
        var refresh = new Button { Content = new FontIcon { Glyph = "\uE72C", FontSize = 14 }, Width = 30, Height = 30, Padding = new Thickness(4), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
        ToolTipService.SetToolTip(refresh, "Refresh"); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(refresh, "Refresh launcher");
        Grid.SetColumn(refresh, 3); tabs.Children.Add(refresh);
        var panel = SurfaceLayout(2, tabs, search, content, status);
        panel.Width = width; panel.Height = height; panel.RequestedTheme = Root.RequestedTheme;
        launcherSurface = panel;
        var style = new Style { TargetType = typeof(FlyoutPresenter) };
        style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, Root.RequestedTheme));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
        style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, width + 26));
        style.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, height + 26));
        var flyout = new Flyout { Content = panel, FlyoutPresenterStyle = style, ShouldConstrainToRootBounds = false, Placement = state.Left ? FlyoutPlacementMode.RightEdgeAlignedTop : FlyoutPlacementMode.LeftEdgeAlignedTop };
        launcherFlyout = flyout;
        IReadOnlyList<InstalledApp> apps = [], windows = [];
        bool closed = false, loadingApps = true, loadingWindows = false;
        int view = 0, loadGeneration = 0;

        void Filter()
        {
            if (closed) return;
            string query = search.Text.Trim();
            IEnumerable<InstalledApp> source = view switch
            {
                1 => state.Shortcuts.Select(s => new InstalledApp(s.Name, s.Target)),
                2 => windows,
                _ => apps.Concat(state.Shortcuts.Where(s => IsWebsite(s.Target)).Select(s => new InstalledApp(s.Name, s.Target))).DistinctBy(a => a.Target, StringComparer.OrdinalIgnoreCase)
            };
            var matches = source.Where(a => a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
            var ordered = matches.OrderByDescending(a => a.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase));
            var results = (view == 1 ? ordered : ordered.ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)).ToArray();
            if (view == 2) tasks.ItemsSource = results; else grid.ItemsSource = results;
            status.Text = view == 0 && loadingApps ? "Loading apps…" : view == 2 && loadingWindows ? "Loading windows…" : results.Length == 0 ? (query.Length > 0 ? "No matches" : view == 1 ? "No pins yet. Right-click an app in All apps to pin it." : "No windows available") : view == 2 ? $"{results.Length} windows" : $"{results.Length} {(view == 1 ? "pinned" : "apps")} · Right-click to customize";
        }
        async Task LoadApps(bool refreshCatalog = false)
        {
            int generation = ++loadGeneration; loadingApps = true; Filter(); refresh.IsEnabled = false;
            try { var catalog = await InstalledApps.GetAsync(refreshCatalog); if (!closed && generation == loadGeneration) { apps = catalog; loadingApps = false; Filter(); } }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
            { if (!closed && generation == loadGeneration) { loadingApps = false; Filter(); status.Text = "Apps unavailable. Your pinned items are still available."; } }
            finally { if (!closed && generation == loadGeneration) refresh.IsEnabled = true; }
        }
        async Task LoadWindows()
        {
            if (closed || loadingWindows) return; loadingWindows = true; Filter();
            try { var catalog = await WindowCatalog.GetAsync(); if (!closed) windows = catalog.Select(w => new InstalledApp(w.Name, "window:" + w.Handle.ToInt64()) { IconTarget = w.IconPath }).ToArray(); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { if (!closed) status.Text = "Could not list windows. Choose Refresh to retry."; }
            finally { loadingWindows = false; if (!closed) Filter(); }
        }
        void SelectView(int selected)
        {
            view = Math.Clamp(selected, 0, 2);
            for (int i = 0; i < buttons.Count; i++) buttons[i].IsChecked = i == view;
            grid.Visibility = view == 2 ? Visibility.Collapsed : Visibility.Visible;
            tasks.Visibility = view == 2 ? Visibility.Visible : Visibility.Collapsed;
            search.PlaceholderText = view == 2 ? "Search windows" : "Search apps and pinned websites";
            Filter(); if (view == 2) _ = LoadWindows();
        }
        selectLauncherView = SelectView;
        foreach (string label in new[] { "All apps", "Pinned", "Windows" })
        {
            int selected = buttons.Count;
            var button = new ToggleButton { Content = label, FontSize = 12, MinWidth = 0, Padding = new Thickness(6, 4, 6, 4), HorizontalAlignment = HorizontalAlignment.Stretch };
            Grid.SetColumn(button, selected); button.Click += (_, _) => SelectView(selected); tabs.Children.Add(button); buttons.Add(button);
        }
        async Task Open(InstalledApp app)
        {
            if (view == 2)
            { if (app.Target.StartsWith("window:") && long.TryParse(app.Target[7..], out long handle) && WindowCatalog.Activate(new IntPtr(handle))) flyout.Hide(); else status.Text = "This window closed or Windows prevented activation. Refresh to try again."; return; }
            await OpenShortcut(new LaunchShortcut { Name = app.Name, Target = app.Target });
            if (closed) return;
            if (PanelStatus.Visibility != Visibility.Visible) flyout.Hide(); else status.Text = PanelStatus.Text;
        }
        search.TextChanged += (_, _) => Filter();
        search.KeyDown += async (_, e) =>
        {
            var list = view == 2 ? (ListViewBase)tasks : grid;
            if (e.Key == VirtualKey.Enter && list.Items.FirstOrDefault() is InstalledApp first) { e.Handled = true; await Open(first); }
            else if (e.Key == VirtualKey.Down && list.Items.Count > 0) { list.SelectedIndex = 0; list.ScrollIntoView(list.SelectedItem); list.Focus(FocusState.Keyboard); e.Handled = true; }
        };
        grid.ItemClick += async (_, e) => { if (e.ClickedItem is InstalledApp app) await Open(app); };
        tasks.ItemClick += async (_, e) => { if (e.ClickedItem is InstalledApp app) await Open(app); };
        grid.RightTapped += (_, e) =>
        {
            for (var node = e.OriginalSource as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is not FrameworkElement { DataContext: InstalledApp app } target) continue;
                var menu = BuildPinMenu(new LaunchShortcut { Name = app.Name, Target = app.Target }, Filter);
                menu.ShowAt(target); e.Handled = true; break;
            }
        };
        refresh.Click += async (_, _) => { if (view == 2) await LoadWindows(); else await LoadApps(true); };
        panel.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { e.Handled = true; flyout.Hide(); } };
        flyout.Opened += (_, _) => { hideTimer.Stop(); search.Focus(FocusState.Programmatic); };
        flyout.Closed += (_, _) =>
        {
            closed = true;
            if (ReferenceEquals(launcherFlyout, flyout)) { launcherFlyout = null; launcherSurface = null; selectLauncherView = null; }
            QueueAutoHide();
        };
        SelectView(initialView);
        try { flyout.ShowAt(LonghornHeader.Visibility == Visibility.Visible ? LonghornHeader : Root); _ = LoadApps(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        { closed = true; launcherFlyout = null; launcherSurface = null; selectLauncherView = null; PanelStatus.Text = "The launcher could not open. Try again."; PanelStatus.Visibility = Visibility.Visible; }
    }
}
