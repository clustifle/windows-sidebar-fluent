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
    private readonly HashSet<LaunchShortcut> loadingShortcutIcons = [];

    private void RefreshQuickLaunch()
    {
        state.Shortcuts ??= [];
        LaunchItems.Children.Clear();
        foreach (var shortcut in state.Shortcuts)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            bool website = IsWebsite(shortcut.Target);
            var fallback = new FontIcon { Glyph = website ? "\uE774" : "\uE8A5", FontSize = 20 };
            row.Children.Add(fallback);
            var image = new Image { Width = 24, Height = 24, Visibility = Visibility.Collapsed };
            row.Children.Add(image);
            var label = new TextBlock { Text = shortcut.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1); row.Children.Add(label);
            var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Style = (Style)Root.Resources["RailButton"] };
            ToolTipService.SetToolTip(button, shortcut.Name);
            button.Click += async (_, _) => await OpenShortcut(shortcut);
            LaunchItems.Children.Add(button);
            if (!website) LoadShortcutIcon(shortcut, image, fallback);
        }
        if (state.Shortcuts.Count == 0) LaunchItems.Children.Add(new TextBlock { Text = "Choose Edit to add apps or websites.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
    }

    private static bool IsWebsite(string target) => Uri.TryCreate(target, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https");

    private async void LoadShortcutIcon(LaunchShortcut shortcut, Image image, FontIcon fallback)
    {
        try
        {
            if (!shortcutImages.TryGetValue(shortcut.Target, out var bitmap))
            {
                if (string.IsNullOrEmpty(shortcut.IconData) || shortcut.IconVersion < 2)
                {
                    if (!loadingShortcutIcons.Add(shortcut)) return;
                    try { shortcut.IconData = await ShortcutIcons.GetEmbeddedAsync(shortcut.Target); shortcut.IconVersion = 2; }
                    finally { loadingShortcutIcons.Remove(shortcut); }
                    if (string.IsNullOrEmpty(shortcut.IconData)) return;
                    Save();
                }
                using var stream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                { writer.WriteBytes(Convert.FromBase64String(shortcut.IconData)); await writer.StoreAsync(); }
                stream.Seek(0);
                bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
                shortcutImages[shortcut.Target] = bitmap;
            }
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
        if (dialogOpen) { preferencesWindow?.Activate(); return; }
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Choose an installed app", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var search = new TextBox { PlaceholderText = "Search installed apps" };
        var appsList = new ListView { Height = 180, SelectionMode = ListViewSelectionMode.Single };
        var addApp = new Button { Content = "Add selected app", IsEnabled = false };
        panel.Children.Add(search); panel.Children.Add(appsList); panel.Children.Add(addApp);
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
        panel.Children.Add(nameBox); panel.Children.Add(urlBox); panel.Children.Add(addWebsite);
        panel.Children.Add(new TextBlock { Text = "Your shortcuts", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var list = new StackPanel { Spacing = 12 }; panel.Children.Add(list);
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
                var up = new Button { Content = "Up", IsEnabled = position > 0 };
                var down = new Button { Content = "Down", IsEnabled = position < state.Shortcuts.Count - 1 };
                var remove = new Button { Content = "Remove" };
                up.Click += (_, _) => { (state.Shortcuts[position - 1], state.Shortcuts[position]) = (state.Shortcuts[position], state.Shortcuts[position - 1]); Persist(); Render(); };
                down.Click += (_, _) => { (state.Shortcuts[position + 1], state.Shortcuts[position]) = (state.Shortcuts[position], state.Shortcuts[position + 1]); Persist(); Render(); };
                remove.Click += (_, _) => { state.Shortcuts.Remove(shortcut); Persist(); Render(); };
                buttons.Children.Add(up); buttons.Children.Add(down); buttons.Children.Add(remove); entry.Children.Add(buttons); list.Children.Add(entry);
            }
        }
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
