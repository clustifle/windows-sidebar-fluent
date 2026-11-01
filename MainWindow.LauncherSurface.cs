using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Sidebar;

public partial class MainWindow
{
    private async void OpenLauncher_Click(object sender, RoutedEventArgs args)
    {
        if (dialogOpen) { ActivatePreferences(); return; }
        var search = new TextBox { PlaceholderText = "Search apps and pinned websites" };
        var filter = new ComboBox { ItemsSource = new[] { "All apps", "Pinned" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var status = new TextBlock { Text = "Loading apps…", FontSize = 12 };
        var grid = new GridView { IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.Single, ItemTemplate = (DataTemplate)Root.Resources["LauncherTileTemplate"] };
        IReadOnlyList<InstalledApp> apps = []; bool closed = false;
        void Filter()
        {
            var source = filter.SelectedIndex == 1 ? state.Shortcuts.Select(s => new InstalledApp(s.Name, s.Target)) : apps.Concat(state.Shortcuts.Where(s => IsWebsite(s.Target)).Select(s => new InstalledApp(s.Name, s.Target))).DistinctBy(a => a.Target, StringComparer.OrdinalIgnoreCase);
            string query = search.Text.Trim();
            var results = source.Where(a => a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(a => a.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            grid.ItemsSource = results; status.Text = results.Length == 0 ? "No matching apps" : $"{results.Length} apps · Right-click to pin or unpin";
#if THEME_REGRESSION
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "launcher-filter-check.txt"), $"queryLength={query.Length}; matches={results.Length}; items={grid.Items.Count}\n");
#endif
        }
        async Task Open(InstalledApp app)
        { await OpenShortcut(new LaunchShortcut { Name = app.Name, Target = app.Target }); if (PanelStatus.Visibility != Visibility.Visible) preferencesWindow?.Close(); else status.Text = PanelStatus.Text; }
        search.TextChanged += (_, _) => Filter(); filter.SelectionChanged += (_, _) => Filter();
        search.KeyDown += async (_, e) => { if (e.Key == VirtualKey.Enter && grid.ItemsSource is IEnumerable<InstalledApp> matches && matches.FirstOrDefault() is { } first) { e.Handled = true; await Open(first); } else if (e.Key == VirtualKey.Down) { grid.Focus(FocusState.Keyboard); e.Handled = true; } };
        grid.ItemClick += async (_, e) => { if (e.ClickedItem is InstalledApp app) await Open(app); };
        grid.RightTapped += (_, e) =>
        {
            for (var node = e.OriginalSource as DependencyObject; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            {
                if (node is not FrameworkElement { DataContext: InstalledApp app }) continue;
                var pinned = state.Shortcuts.FirstOrDefault(s => s.Target.Equals(app.Target, StringComparison.OrdinalIgnoreCase));
                var menu = new MenuFlyout(); var pin = new MenuFlyoutItem { Text = pinned is null ? "Pin to Quick launch" : "Unpin from Quick launch" };
                pin.Click += (_, _) => { if (pinned is null) state.Shortcuts.Add(new LaunchShortcut { Name = app.Name, Target = app.Target }); else state.Shortcuts.Remove(pinned); RefreshQuickLaunch(); Save(); Filter(); };
                menu.Items.Add(pin); menu.ShowAt((FrameworkElement)node); e.Handled = true; break;
            }
        };
        var panel = SurfaceLayout(2, search, filter, grid, status);
        var pivot = new Pivot(); pivot.Items.Add(new PivotItem { Header = "Apps", Content = panel });
        var tasks = new ListView { IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.Single, ItemTemplate = (DataTemplate)Root.Resources["AppSearchResultTemplate"] };
        var taskStatus = new TextBlock { Text = "Select a window to switch to it.", FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var refresh = new Button { Content = "Refresh windows" }; bool refreshing = false;
        async Task RefreshWindows()
        {
            if (refreshing) return; refreshing = true; refresh.IsEnabled = false;
            try { var windows = await WindowCatalog.GetAsync(); if (!closed && !closing) { tasks.ItemsSource = windows.Select(w => new InstalledApp(w.Name, "window:" + w.Handle.ToInt64()) { IconTarget = w.IconPath }).ToArray(); taskStatus.Text = windows.Count == 0 ? "No other app windows are open." : "Select a window to switch to it."; } }
            finally { refreshing = false; if (!closed) refresh.IsEnabled = true; }
        }
        refresh.Click += async (_, _) => await RefreshWindows();
        pivot.SelectionChanged += async (_, _) => { if (pivot.SelectedIndex == 1) await RefreshWindows(); };
        tasks.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is InstalledApp app && app.Target.StartsWith("window:") && long.TryParse(app.Target[7..], out long handle))
            { if (WindowCatalog.Activate(new IntPtr(handle))) preferencesWindow?.Close(); else taskStatus.Text = "This window closed or Windows prevented activation. Refresh to try again."; }
        };
        var taskPanel = SurfaceLayout(1, refresh, tasks, taskStatus);
        pivot.Items.Add(new PivotItem { Header = "Windows", Content = taskPanel });
        panel.Loaded += async (_, _) =>
        {
            search.Focus(FocusState.Programmatic);
            try { apps = await InstalledApps.GetAsync(); if (!closed && !closing) Filter(); }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { if (!closed) status.Text = "Could not list apps. Your pinned items are still available."; }
        };
        panel.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { e.Handled = true; preferencesWindow?.Close(); } };
        await ShowDialog("Launcher", pivot); closed = true;
    }
}
