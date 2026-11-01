using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Sidebar;

public partial class MainWindow
{
    private Pivot BuildSettingsContent()
    {
        var tabs = new Pivot { IsHeaderItemsCarouselEnabled = false };
        tabs.Resources["PivotHeaderItemFontSize"] = 14d;
        StackPanel Section(string title)
        { var content = new StackPanel { Spacing = 14, Padding = new Thickness(0, 8, 0, 12) }; tabs.Items.Add(new PivotItem { Header = title, Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } }); return content; }
        void Number(StackPanel panel, string label, int minimum, int maximum, int value, Action<int> change)
        {
            var input = new NumberBox { Header = label, Minimum = minimum, Maximum = maximum, Value = value, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            int applied = Math.Clamp(value, minimum, maximum);
            input.ValueChanged += (_, args) => { if (!double.IsFinite(args.NewValue)) return; int next = Math.Clamp((int)args.NewValue, minimum, maximum); if (next == applied) return; applied = next; change(next); ApplyState(); UpdateClassicGadgets(); Save(); }; panel.Children.Add(input);
        }
        var appearance = Section("Appearance");
        var theme = new ComboBox { Header = "Theme", ItemsSource = new[] { "System", "Light", "Dark" }, SelectedItem = state.Theme, HorizontalAlignment = HorizontalAlignment.Stretch };
        theme.SelectionChanged += (_, _) => { if (theme.SelectedItem is not string selected || state.Theme == selected) return; state.Theme = selected; SynchronizeAppearance(); QueueAppearanceRefresh(); Save(); }; appearance.Children.Add(theme);
        var material = new ComboBox { Header = "Material", ItemsSource = new[] { "Acrylic", "Mica" }, SelectedItem = state.Material, HorizontalAlignment = HorizontalAlignment.Stretch };
        material.SelectionChanged += (_, _) => { if (material.SelectedItem is not string selected || state.Material == selected) return; state.Material = selected; ApplyState(); Save(); }; appearance.Children.Add(material);
        Number(appearance, "Normal sidebar width", 220, 360, state.Width, value => state.Width = value);
        var gadgets = Section("Gadgets");
        gadgets.Children.Add(new TextBlock { Text = "Expand a gadget to customize its content and locked card appearance. Changes save automatically.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        Number(gadgets, "Gadgets per page (normal mode)", 1, 3, state.GadgetsPerPage, value => state.GadgetsPerPage = value);
        foreach (string id in ActiveGadgetOrder)
        {
            var expander = new Expander { Header = id, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            string gadgetId = id;
            expander.Expanding += (_, _) => { expander.Content = BuildCardEditor(gadgetId); };
            gadgets.Children.Add(expander);
        }
        var longhornPanel = Section("Longhorn");
        var longhorn = new ToggleSwitch { Header = "Longhorn mode", IsOn = state.LonghornMode };
        longhorn.Toggled += async (_, _) => { if (longhorn.IsOn == state.LonghornMode) return; longhorn.IsEnabled = false; try { state.LonghornMode = longhorn.IsOn; await UpdateLonghornAsync(); Save(); } finally { longhorn.IsEnabled = true; } }; longhornPanel.Children.Add(longhorn);
        Number(longhornPanel, "Rail width", 156, 280, state.LonghornWidth, value => state.LonghornWidth = value);
        AddToggle(longhornPanel, "Dock clock at the bottom", state.LonghornBottomClock, value => state.LonghornBottomClock = value);
        AddToggle(longhornPanel, "Show pinned launchers", state.LonghornShowPins, value => { state.LonghornShowPins = value; ApplyLonghornSections(); });
        var keep = new ToggleSwitch { Header = "Keep Windows taskbar visible", IsOn = !state.LonghornHideTaskbar };
        keep.Toggled += async (_, _) => { if (state.LonghornHideTaskbar == !keep.IsOn) return; state.LonghornHideTaskbar = !keep.IsOn; ApplyLonghornSections(); Save(); await UpdateTaskbarOptionsAsync(); }; longhornPanel.Children.Add(keep);
        var hideTray = new ToggleSwitch { Header = "Hide Windows taskbar tray and clock", IsOn = state.LonghornHideSystemTray };
        hideTray.Toggled += async (_, _) => { if (state.LonghornHideSystemTray == hideTray.IsOn) return; state.LonghornHideSystemTray = hideTray.IsOn; Save(); await UpdateTaskbarOptionsAsync(); }; longhornPanel.Children.Add(hideTray);
        longhornPanel.Children.Add(new TextBlock { Text = "Compatibility option: keeps Start and app buttons, hides the original system area, and restores the taskbar on exit. If the system area cannot be identified, its original layout is kept.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        longhornPanel.Children.Add(new TextBlock { Text = "With the taskbar visible, Start and Apps stay on Windows' taskbar. Running-app and background-app tray strips are hidden in the rail. Explorer continues to provide the desktop and Start menu.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var behavior = Section("Behavior");
        AddToggle(behavior, "Auto-hide at screen edge", state.AutoHide, value => state.AutoHide = value);
        behavior.Children.Add(new TextBlock { Text = "Auto-hide keeps the rail visible on the desktop. Switching to an app resumes edge auto-hide.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var side = new ToggleSwitch { Header = "Place on the left", IsOn = state.Left };
        side.Toggled += async (_, _) => { if (side.IsOn != state.Left) await MoveSidebarAsync(side.IsOn); }; behavior.Children.Add(side);
        AddToggle(behavior, "Keep above other windows", state.AlwaysOnTop, value => state.AlwaysOnTop = value);
        AddToggle(behavior, "Start with Windows", StartupPreference.Enabled, SetStartup);
        behavior.Children.Add(new TextBlock { Text = "FluentSidebar 1.2.0 preview. Preferences and notes stay on this device. Closing the app restores reserved desktop space and the Windows taskbar.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        return tabs;
    }
    private void ActivatePreferences()
    {
        if (preferencesWindow is null) return;
        if (preferencesWindow.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized) presenter.Restore();
        preferencesWindow.Activate();
    }
}
