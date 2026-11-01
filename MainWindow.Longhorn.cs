using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace Sidebar;
public partial class MainWindow
{
    private bool? styledLonghorn;
    private ElementTheme styledTheme;
    private bool styledHighContrast;
    private bool shellShutdownPrepared;
    private void InitializeLonghorn()
    {
        LonghornSearch.Loaded += (_, _) => SidebarAppSearchBox.Configure(LonghornSearch, state.Left);
        LonghornSearch.ActualThemeChanged += (_, _) => SidebarAppSearchBox.Configure(LonghornSearch, state.Left);
        SidebarAppSearchBox.Configure(LonghornSearch, state.Left);
        LonghornSearch.RegisterPropertyChangedCallback(AutoSuggestBox.IsSuggestionListOpenProperty, (_, _) =>
        { if (LonghornSearch.IsSuggestionListOpen) hideTimer.Stop(); else QueueAutoHide(); });
        ShellBridge.Restore();
        AppWindow.Closing += (_, args) =>
        {
            if (shellShutdownPrepared) return;
            args.Cancel = true; shellShutdownPrepared = true; closing = true;
            trayIcon?.Dispose(); appBar?.Dispose(); SystemBackdrop = null;
            DispatcherQueue.TryEnqueue(() => Close());
        };
        _ = UpdateLonghornAsync();
    }
    private Task UpdateLonghornAsync()
    {
        // Migrate old replacement settings: the Windows taskbar now stays intact.
        state.LonghornHideTaskbar = false;
        state.LonghornHideSystemTray = false;
        ApplyLonghornVisuals();
        ApplyState(); RefreshQuickLaunch();
        return Task.CompletedTask;
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
        LonghornBottom.Visibility = LonghornPanel.Visibility = state.LonghornMode ? Visibility.Visible : Visibility.Collapsed;
        ApplyLauncherEntry();
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
        LonghornPinsSection.Visibility = state.LonghornShowPins ? Visibility.Visible : Visibility.Collapsed;
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
            if (generation != railSearchGeneration || closing || !(state.LonghornMode || state.OperatingMode == "Launcher")) return;
            var matches = apps.Where(app => app.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(app => app.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)).Take(8).ToArray(); sender.ItemsSource = matches.Length == 0 ? new[] { new InstalledApp("No matching apps", "") } : matches;
            SidebarAppSearchBox.Configure(sender, state.Left);
        }
        catch (Exception) { if (generation == railSearchGeneration) sender.ItemsSource = null; }
    }
    private void LonghornSearch_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    { if (args.SelectedItem is InstalledApp app) sender.Text = app.Name; }
    private async void LonghornSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is InstalledApp app && !string.IsNullOrWhiteSpace(app.Target))
            await OpenShortcut(new LaunchShortcut { Name = app.Name, Target = app.Target });
        else if (!string.IsNullOrWhiteSpace(args.QueryText))
        {
            try { await Windows.System.Launcher.LaunchUriAsync(new Uri("search-ms:query=" + Uri.EscapeDataString(args.QueryText.Trim()))); }
            catch (Exception ex) when (ex is COMException or ArgumentException) { PanelStatus.Text = "Windows Search could not open."; PanelStatus.Visibility = Visibility.Visible; }
        }
        sender.IsSuggestionListOpen = false;
    }

}
