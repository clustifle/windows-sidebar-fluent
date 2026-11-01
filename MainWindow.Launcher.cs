using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Windows.System;

namespace Sidebar;

public sealed class LaunchShortcut
{
    public string Name { get; set; } = "Shortcut";
    public string Target { get; set; } = "";
    public string IconData { get; set; } = "";
    public int IconVersion { get; set; }
}

public partial class MainWindow
{
    private readonly Dictionary<string, BitmapImage> shortcutImages = new();
    private readonly Dictionary<string, Task<string>> loadingShortcutIcons = new();

    private void RefreshQuickLaunch()
    {
        state.Shortcuts ??= [];
        void Render(Grid grid)
        {
        grid.Children.Clear(); grid.RowDefinitions.Clear(); grid.ColumnDefinitions.Clear();
        bool rail = state.LonghornMode && grid == LonghornPins;
        int columns = Math.Clamp(state.LaunchColumns, 2, 6);
        for (int column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        foreach (var shortcut in state.Shortcuts)
        {
            int available = Math.Max(12, (SidebarWidth - (rail ? 10 : 48)) / columns - 12);
            int iconSize = Math.Min(available, rail ? (int)Math.Round(Math.Clamp(state.LaunchIconSize, 16, 40) * 0.65) : Math.Clamp(state.LaunchIconSize, 16, 40));
            var row = new Grid { Width = iconSize, Height = iconSize };
            bool website = IsWebsite(shortcut.Target);
            var fallback = new FontIcon { Glyph = website ? "\uE774" : "\uE8A5", FontSize = 20 };
            row.Children.Add(fallback);
            var image = new Image { Width = iconSize, Height = iconSize, Visibility = Visibility.Collapsed };
            row.Children.Add(image);
            var button = new Button { Content = row, Width = iconSize + 12, Height = iconSize + 12, Padding = new Thickness(6), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, shortcut.Name);
            ToolTipService.SetToolTip(button, shortcut.Name);
            button.Click += async (_, _) => await OpenShortcut(shortcut);
            int index = grid.Children.Count;
            Grid.SetColumn(button, index % columns); Grid.SetRow(button, index / columns);
            if (index % columns == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(button);
            if (!website) LoadShortcutIcon(shortcut, image, fallback);
        }
        if (state.Shortcuts.Count == 0) grid.Children.Add(new TextBlock { Text = "Choose Edit to add apps or websites.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        }
        Render(LaunchItems);
        if (state.LonghornMode) Render(LonghornPins); else { LonghornPins.Children.Clear(); LonghornPins.RowDefinitions.Clear(); }
        ApplyLonghornSections();
    }

    private static bool IsWebsite(string target) => Uri.TryCreate(target, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https");

    private async void LoadShortcutIcon(LaunchShortcut shortcut, Image image, FontIcon fallback, bool persist = true)
    {
        image.Tag = shortcut.Target;
        try
        {
            if (!shortcutImages.TryGetValue(shortcut.Target, out var bitmap))
            {
                if (string.IsNullOrEmpty(shortcut.IconData) || shortcut.IconVersion < 2)
                {
                    if (!loadingShortcutIcons.TryGetValue(shortcut.Target, out var iconTask))
                        loadingShortcutIcons[shortcut.Target] = iconTask = ShortcutIcons.GetEmbeddedAsync(shortcut.Target);
                    try { shortcut.IconData = await iconTask; shortcut.IconVersion = 2; }
                    finally { loadingShortcutIcons.Remove(shortcut.Target); }
                    if (string.IsNullOrEmpty(shortcut.IconData)) return;
                    if (persist) Save();
                }
                using var stream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                { writer.WriteBytes(Convert.FromBase64String(shortcut.IconData)); await writer.StoreAsync(); }
                stream.Seek(0);
                bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
                if (shortcutImages.Count >= 96)
                {
                    var pinned = state.Shortcuts.Select(item => item.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    string? oldest = shortcutImages.Keys.FirstOrDefault(key => !pinned.Contains(key));
                    oldest ??= shortcutImages.Keys.First();
                    shortcutImages.Remove(oldest);
                }
                shortcutImages[shortcut.Target] = bitmap;
            }
            if (image.Tag as string != shortcut.Target) return;
            image.Source = bitmap; image.Visibility = Visibility.Visible; fallback.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is FormatException or System.Runtime.InteropServices.COMException or System.IO.IOException) { }
    }

    private async Task OpenShortcut(LaunchShortcut shortcut)
    {
        try
        {
            if (shortcut.Target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", shortcut.Target) { UseShellExecute = true });
            else if (System.IO.File.Exists(shortcut.Target) || System.IO.Directory.Exists(shortcut.Target))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(shortcut.Target) { UseShellExecute = true });
            else if (!await Launcher.LaunchUriAsync(new Uri(shortcut.Target))) throw new InvalidOperationException();
            PanelStatus.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        { PanelStatus.Text = $"Could not open {shortcut.Name}. Check Quick launch → Edit."; PanelStatus.Visibility = Visibility.Visible; }
    }

    private async void CustomizeLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (dialogOpen) { ActivatePreferences(); return; }
        var panel = new StackPanel { Spacing = 12 };
        var appPanel = new StackPanel { Spacing = 10 };
        var webPanel = new StackPanel { Spacing = 10 };
        var pinnedPanel = new StackPanel { Spacing = 10 };
        var tabs = new Pivot();
        tabs.Items.Add(new PivotItem { Header = "Pinned", Content = pinnedPanel });
        tabs.Items.Add(new PivotItem { Header = "Apps", Content = appPanel });
        tabs.Items.Add(new PivotItem { Header = "Web", Content = webPanel });
        panel.Children.Add(tabs);
        var search = new TextBox { PlaceholderText = "Search installed apps" };
        var appsList = new ListView { Height = 180, SelectionMode = ListViewSelectionMode.Single };
        var addApp = new Button { Content = "Add selected app", IsEnabled = false };
        appPanel.Children.Add(search); appPanel.Children.Add(appsList); appPanel.Children.Add(addApp);
        var status = new TextBlock { Text = "Loading installed apps…", FontSize = 12, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(status);
        IReadOnlyList<InstalledApp> apps = [];
        List<InstalledApp> visible = [];
        void Filter()
        {
            visible = apps.Where(a => a.Name.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList();
            appsList.ItemsSource = visible.Select(a => a.Name).ToArray();
            addApp.IsEnabled = false;
        }
        search.TextChanged += (_, _) => Filter();
        appsList.SelectionChanged += (_, _) => addApp.IsEnabled = appsList.SelectedIndex >= 0;
        var nameBox = new TextBox { Header = "Website name", PlaceholderText = "Name" };
        var urlBox = new TextBox { Header = "Website address", PlaceholderText = "https://example.com" };
        var addWebsite = new Button { Content = "Add website" };
        webPanel.Children.Add(nameBox); webPanel.Children.Add(urlBox); webPanel.Children.Add(addWebsite);
                pinnedPanel.Children.Add(new TextBlock { Text = "Use the arrows to reorder your icons. Hover an icon on the sidebar to see its name.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var importPins = new Button { Content = "Import taskbar shortcuts" };
        pinnedPanel.Children.Add(importPins);
        var list = new StackPanel { Spacing = 12 }; pinnedPanel.Children.Add(list);
        void Persist()
        {
            if (!state.QuickLaunch && state.Shortcuts.Count > 0) { state.QuickLaunch = true; ApplyState(); }
            RefreshQuickLaunch(); Save();
        }
        void Render()
        {
            list.Children.Clear();
            for (int index = 0; index < state.Shortcuts.Count; index++)
            {
                int position = index;
                var shortcut = state.Shortcuts[index];
                var entry = new StackPanel { Spacing = 6 };
                var name = new TextBox { Text = shortcut.Name, PlaceholderText = "Shortcut name" };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(name, "Shortcut name");
                name.LostFocus += (_, _) => { if (!string.IsNullOrWhiteSpace(name.Text)) { shortcut.Name = name.Text.Trim(); Persist(); } };
                entry.Children.Add(name);
                if (IsWebsite(shortcut.Target))
                {
                    var address = new TextBox { Text = shortcut.Target };
                    address.LostFocus += (_, _) => { if (IsWebsite(address.Text)) { shortcut.Target = address.Text.Trim(); Persist(); } else status.Text = "Enter a complete http or https address."; };
                    entry.Children.Add(address);
                }
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                var up = new Button { Content = new FontIcon { Glyph = "\uE70E", FontSize = 12 }, Padding = new Thickness(8), IsEnabled = position > 0 };
                var down = new Button { Content = new FontIcon { Glyph = "\uE70D", FontSize = 12 }, Padding = new Thickness(8), IsEnabled = position < state.Shortcuts.Count - 1 };
                var remove = new Button { Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 }, Padding = new Thickness(8) };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(up, "Move up"); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(down, "Move down"); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, "Remove shortcut");
                ToolTipService.SetToolTip(up, "Move up"); ToolTipService.SetToolTip(down, "Move down"); ToolTipService.SetToolTip(remove, "Remove shortcut");
                up.Click += (_, _) => { (state.Shortcuts[position - 1], state.Shortcuts[position]) = (state.Shortcuts[position], state.Shortcuts[position - 1]); Persist(); Render(); };
                down.Click += (_, _) => { (state.Shortcuts[position + 1], state.Shortcuts[position]) = (state.Shortcuts[position], state.Shortcuts[position + 1]); Persist(); Render(); };
                remove.Click += (_, _) => { state.Shortcuts.Remove(shortcut); Persist(); Render(); };
                buttons.Children.Add(up); buttons.Children.Add(down); buttons.Children.Add(remove); entry.Children.Add(buttons); list.Children.Add(entry);
            }
        }
        importPins.Click += async (_, _) =>
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
            int count = 0;
            if (Directory.Exists(folder))
                foreach (string file in Directory.EnumerateFiles(folder, "*.lnk").Take(64))
                {
                    if (state.Shortcuts.Any(s => s.Target.Equals(file, StringComparison.OrdinalIgnoreCase))) continue;
                    state.Shortcuts.Add(new LaunchShortcut { Name = Path.GetFileNameWithoutExtension(file), Target = file, IconData = await ShortcutIcons.GetEmbeddedAsync(file), IconVersion = 2 }); count++;
                }
            Persist(); Render(); status.Text = count == 0 ? "No new taskbar shortcut files found. Use Apps to add other pins." : $"Imported {count} shortcuts.";
        };
        addApp.Click += async (_, _) =>
        {
            int selected = appsList.SelectedIndex;
            if (selected < 0 || selected >= visible.Count) return;
            var app = visible[selected];
            if (state.Shortcuts.Any(s => s.Target.Equals(app.Target, StringComparison.OrdinalIgnoreCase))) { status.Text = "This app is already added."; return; }
            addApp.IsEnabled = false;
            try
            {
                string icon = await ShortcutIcons.GetEmbeddedAsync(app.Target);
                state.Shortcuts.Add(new LaunchShortcut { Name = app.Name, Target = app.Target, IconData = icon, IconVersion = 2 });
                Persist(); Render(); status.Text = $"Added {app.Name}";
            }
            finally { addApp.IsEnabled = appsList.SelectedIndex >= 0; }
        };
        addWebsite.Click += (_, _) =>
        {
            if (!IsWebsite(urlBox.Text)) { status.Text = "Enter a complete http or https address."; return; }
            var uri = new Uri(urlBox.Text);
            state.Shortcuts.Add(new LaunchShortcut { Name = string.IsNullOrWhiteSpace(nameBox.Text) ? uri.Host : nameBox.Text.Trim(), Target = uri.AbsoluteUri });
            nameBox.Text = ""; urlBox.Text = ""; status.Text = ""; Persist(); Render();
        };
        async Task LoadApps()
        {
            try { apps = await InstalledApps.GetAsync(); Filter(); status.Text = apps.Count == 0 ? "No installed apps were found." : $"{apps.Count} installed apps"; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); status.Text = "Windows could not list installed apps."; }
        }
        Render(); _ = LoadApps();
        await ShowDialog("Quick launch", panel);
    }
}
