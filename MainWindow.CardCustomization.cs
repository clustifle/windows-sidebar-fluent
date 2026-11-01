using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Sidebar;

public sealed class GadgetCardStyle
{
    public int Height { get; set; }
    public int Padding { get; set; } = -1;
    public int CornerRadius { get; set; } = -1;
    public int BackgroundOpacity { get; set; } = -1;
    public string Title { get; set; } = "";
    public bool ShowTitle { get; set; } = true;
}

public partial class MainWindow
{
    private readonly Dictionary<string, TextBlock> gadgetHeadings = new();
    private readonly Dictionary<string, string> originalHeadings = new();
    private GadgetCardStyle CardStyle(string id)
    {
        state.CardStyles ??= new();
        if (!state.CardStyles.TryGetValue(id, out var style) || style is null) state.CardStyles[id] = style = new();
        return style;
    }
    private void ApplyCardCustomizations()
    {
        foreach (string id in state.GadgetOrder)
        {
            var style = CardStyle(id); var card = (Border)GadgetElement(id);
            if (style.Height > 0) card.Height = Math.Clamp(style.Height, 80, 600);
            if (style.Padding >= 0) card.Padding = new Thickness(Math.Clamp(style.Padding, 0, 24));
            else card.Padding = state.LonghornMode ? new Thickness(4, 8, 4, 8) : new Thickness(12);
            card.CornerRadius = new CornerRadius(style.CornerRadius >= 0 ? Math.Clamp(style.CornerRadius, 0, 24) : state.LonghornMode ? 0 : 8);
            if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) card.Background = SurfaceBrush(Root.RequestedTheme, 255);
            else if (style.BackgroundOpacity >= 0) card.Background = SurfaceBrush(Root.RequestedTheme, (byte)(Math.Clamp(style.BackgroundOpacity, 0, 100) * 255 / 100));
            else if (state.LonghornMode) card.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            else card.ClearValue(Border.BackgroundProperty);
            if (gadgetHeadings.TryGetValue(id, out var heading))
            { heading.Text = string.IsNullOrWhiteSpace(style.Title) ? originalHeadings[id] : style.Title; heading.Visibility = style.ShowTitle ? Visibility.Visible : Visibility.Collapsed; }
        }
        NotesBox.FontSize = Math.Clamp(state.NotesFontSize, 10, 24);
        SlideImage.Stretch = state.SlideFit == "Fit" ? Stretch.Uniform : state.SlideFit == "Stretch" ? Stretch.Fill : Stretch.UniformToFill;
        SlideName.Visibility = state.SlideShowName ? Visibility.Visible : Visibility.Collapsed;
        PuzzleStatus.Visibility = state.PuzzleShowMoves ? Visibility.Visible : Visibility.Collapsed;
        CalendarTodayButton.Visibility = state.CalendarTodayButton ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RegisterGadgetHeading(string id, UIElement content)
    {
        if (id is "Clock" or "Calendar" || content is not StackPanel stack || stack.Children.Count == 0) return;
        TextBlock? heading = stack.Children[0] as TextBlock;
        if (stack.Children[0] is Grid grid) heading = grid.Children.OfType<TextBlock>().FirstOrDefault();
        if (heading is not null) { gadgetHeadings[id] = heading; originalHeadings[id] = heading.Text; }
    }
    private void OpenGadgetOptions(Action action)
    { preferencesWindow?.Close(); DispatcherQueue.TryEnqueue(() => action()); }

    private StackPanel BuildCardEditor(string id)
    {
        var panel = new StackPanel { Spacing = 12 };
        var style = CardStyle(id);
        AddToggle(panel, "Show gadget", GadgetEnabled(id), value => SetGadgetEnabled(id, value));
        void Number(string label, double value, int minimum, int maximum, Action<int> set)
        {
            var input = new NumberBox { Header = label, Value = value, Minimum = minimum, Maximum = maximum, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            int applied = Math.Clamp((int)value, minimum, maximum);
            input.ValueChanged += (_, args) => { if (double.IsFinite(args.NewValue)) { int next = Math.Clamp((int)args.NewValue, minimum, maximum); if (next == applied) return; applied = next; set(next); ApplyState(); RefreshQuickLaunch(); nextPerformanceUpdate = nextStatusUpdate = nextFeedRefresh = DateTime.MinValue; Save(); } };
            panel.Children.Add(input);
        }
        void Choice(string label, string[] choices, string value, Action<string> set)
        {
            var input = new ComboBox { Header = label, ItemsSource = choices, SelectedItem = value, HorizontalAlignment = HorizontalAlignment.Stretch };
            input.SelectionChanged += (_, _) => { if (input.SelectedItem is string selected) { set(selected); ApplyState(); RenderCalendar(); Save(); } }; panel.Children.Add(input);
        }
        void ActionButton(string label, Action action)
        { var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch }; button.Click += (_, _) => action(); panel.Children.Add(button); }
        Number("Locked height (0 = default)", style.Height, 0, 600, value => style.Height = value == 0 ? 0 : Math.Max(80, value));
        Number("Padding (-1 = default)", style.Padding, -1, 24, value => style.Padding = value);
        Number("Corner radius (-1 = default)", style.CornerRadius, -1, 24, value => style.CornerRadius = value);
        Number("Background opacity (-1 = theme, %)", style.BackgroundOpacity, -1, 100, value => style.BackgroundOpacity = value);
        if (gadgetHeadings.ContainsKey(id))
        {
            var title = new TextBox { Header = "Card title", Text = style.Title, PlaceholderText = originalHeadings[id], MaxLength = 48 };
            title.TextChanged += (_, _) => { style.Title = title.Text.Trim(); ApplyCardCustomizations(); saveTimer.Stop(); saveTimer.Start(); }; panel.Children.Add(title);
            AddToggle(panel, "Show card title", style.ShowTitle, value => style.ShowTitle = value);
        }
        switch (id)
        {
            case "Clock": ActionButton("Clock face, time zone, and display…", () => OpenGadgetOptions(() => CustomizeClock_Click(this, new RoutedEventArgs()))); break;
            case "Calendar":
                Choice("First day of week", ["System", "Sunday", "Monday"], state.CalendarFirstDay, value => state.CalendarFirstDay = value);
                AddToggle(panel, "Show adjacent-month dates", state.CalendarAdjacentDays, value => state.CalendarAdjacentDays = value);
                AddToggle(panel, "Show Today button", state.CalendarTodayButton, value => state.CalendarTodayButton = value); break;
            case "Notes": Number("Text size", state.NotesFontSize, 10, 24, value => state.NotesFontSize = value); Number("Writing area height", state.NoteHeight, 72, 240, value => state.NoteHeight = value); break;
            case "Performance": Number("Refresh interval (seconds)", state.PerformanceInterval, 1, 10, value => state.PerformanceInterval = value); break;
            case "Timer":
                Number("Duration (minutes)", state.TimerDuration, 1, 180, value => { state.TimerDuration = value; TimerMinutes.Value = value; if (!timerDeadline.HasValue) TimerReset_Click(this, new RoutedEventArgs()); });
                AddToggle(panel, "Play completion sound", state.TimerSound, value => state.TimerSound = value); break;
            case "Storage":
                Choice("Drive", new[] { "System drive" }.Concat(System.IO.DriveInfo.GetDrives().Select(drive => drive.Name)).ToArray(), string.IsNullOrEmpty(state.StorageDrive) ? "System drive" : state.StorageDrive, value => { state.StorageDrive = value == "System drive" ? "" : value; nextStatusUpdate = DateTime.MinValue; }); break;
            case "Slide show":
                Number("Picture interval (seconds)", state.SlideInterval, 5, 120, value => state.SlideInterval = value);
                Choice("Picture fit", ["Fill", "Fit", "Stretch"], state.SlideFit, value => state.SlideFit = value);
                AddToggle(panel, "Show file name", state.SlideShowName, value => state.SlideShowName = value);
                ActionButton("Choose picture folder…", () => ChooseSlideFolder_Click(this, new RoutedEventArgs())); break;
            case "Picture puzzle":
                AddToggle(panel, "Show move count", state.PuzzleShowMoves, value => state.PuzzleShowMoves = value);
                ActionButton("Choose picture…", () => ChoosePuzzlePicture_Click(this, new RoutedEventArgs()));
                ActionButton("Use numbered tiles", () => { state.PuzzlePicture = ""; puzzlePicture = null; RenderPuzzle(); Save(); });
                ActionButton("Shuffle", () => PuzzleShuffle_Click(this, new RoutedEventArgs())); break;
            case "Feed headlines":
                Number("Headlines", state.FeedCount, 1, 10, value => state.FeedCount = value);
                Number("Refresh interval (minutes)", state.FeedRefreshMinutes, 5, 120, value => state.FeedRefreshMinutes = value);
                ActionButton("Feed address…", () => OpenGadgetOptions(() => ConfigureFeed_Click(this, new RoutedEventArgs()))); break;
            case "Quick launch":
                Number("Columns", state.LaunchColumns, 2, 6, value => state.LaunchColumns = value);
                Number("Icon size", state.LaunchIconSize, 16, 40, value => state.LaunchIconSize = value);
                ActionButton("Apps and websites…", () => OpenGadgetOptions(() => CustomizeLaunch_Click(this, new RoutedEventArgs()))); break;
        }
        ActionButton("Reset this card's appearance", () => { state.CardStyles[id] = new(); ApplyState(); Save(); OpenGadgetOptions(() => CustomizeCard(id)); });
        return panel;
    }
    private async void CustomizeCard(string id)
    { if (dialogOpen) return; await ShowDialog($"Customize {id}", BuildCardEditor(id)); }
}
