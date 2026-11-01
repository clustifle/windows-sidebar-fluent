using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace Sidebar;

public partial class MainWindow
{
    private async void ShowGadgetGallery()
    {
        if (dialogOpen) { ActivatePreferences(); return; }
        var search = new TextBox { PlaceholderText = "Search gadgets" };
        var category = new ComboBox { ItemsSource = new[] { "All categories" }.Concat(GadgetCatalog.All.Select(g => g.Category).Distinct()).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var grid = new GridView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true, ItemTemplate = (DataTemplate)Root.Resources["GadgetGalleryTemplate"] };
        var status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        void Filter()
        {
            string query = search.Text.Trim(), selected = category.SelectedItem as string ?? "All categories";
            var visible = GadgetCatalog.All.Where(g => (selected == "All categories" || g.Category == selected) && (g.Id.Contains(query, StringComparison.CurrentCultureIgnoreCase) || g.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToArray();
            grid.ItemsSource = visible.Select(g => new GadgetDefinition(g.Id, g.Category, g.Glyph, g.Description)).ToArray();
            status.Text = visible.Length == 0 ? "No matching gadgets" : "Select a gadget to show or hide it. Right-click its card to customize it.";
        }
        search.TextChanged += (_, _) => Filter(); category.SelectionChanged += (_, _) => Filter();
        grid.ItemClick += (_, args) =>
        {
            if (args.ClickedItem is not GadgetDefinition gadget) return;
            bool enabled = !GadgetEnabled(gadget.Id); SetGadgetEnabled(gadget.Id, enabled);
            if (enabled && !state.LonghornMode)
            {
                var enabledOrder = ActiveGadgetOrder.Where(GadgetEnabled).ToList();
                state.PageIndex = Math.Max(0, enabledOrder.IndexOf(gadget.Id)) / Math.Clamp(state.GadgetsPerPage, 1, 3);
            }
            ApplyState(); Save(); Filter(); status.Text = gadget.Id + (enabled ? " added" : " hidden");
        };
        var arrange = new Button { Content = "Arrange gadget order…" };
        arrange.Click += (_, _) => { preferencesWindow?.Close(); DispatcherQueue.TryEnqueue(() => ArrangeGadgets_Click(this, new RoutedEventArgs())); };
        var panel = SurfaceLayout(2, search, category, grid, status, arrange);
        var pivot = new Pivot(); pivot.Items.Add(new PivotItem { Header = "Gadget gallery", Content = panel }); Filter();
        await ShowDialog("Gadgets", pivot);
    }

    private void GalleryItem_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement root) return;
        void Update()
        {
            if (root.DataContext is not GadgetDefinition gadget) return;
            if (root.FindName("GadgetEnabledLabel") is TextBlock label) label.Text = GadgetEnabled(gadget.Id) ? "Added" : "Add gadget";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(root, gadget.Id + (GadgetEnabled(gadget.Id) ? ", added" : ", not added"));
        }
        if (root.Tag is null) { root.Tag = true; root.DataContextChanged += (_, _) => Update(); } Update();
    }
}
