using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Sidebar;

public partial class MainWindow
{
    private TimeZoneInfo clockTimeZone = TimeZoneInfo.Local;

    private void ApplyClockPreferences()
    {
        try { clockTimeZone = state.ClockTimeZone == "Local" ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(state.ClockTimeZone); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { clockTimeZone = TimeZoneInfo.Local; }
        DateText.Visibility = state.ClockShowDate ? Visibility.Visible : Visibility.Collapsed;
        ClockCaption.Text = state.ClockLabel;
        ClockCaption.Visibility = string.IsNullOrWhiteSpace(state.ClockLabel) ? Visibility.Collapsed : Visibility.Visible;
        ClockView.Width = ClockView.Height = state.ClockSize > 0 ? Math.Clamp(state.ClockSize, 64, 160) : state.CompactClock ? 80 : 120;
        clockDate = DateTime.MinValue;
        clockFaceDirty = true;
    }

    private void DrawClockNumerals(Brush ink)
    {
        if (state.ClockFaceStyle is "Minimal" or "Railway") return;
        string[] roman = ["XII", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI"];
        for (int hour = 0; hour < 12; hour++)
        {
            double angle = hour * Math.PI / 6;
            var label = new TextBlock
            {
                Text = state.ClockFaceStyle == "Roman" ? roman[hour] : (hour == 0 ? 12 : hour).ToString(),
                Width = 26, Height = 20, TextAlignment = TextAlignment.Center, FontSize = state.ClockFaceStyle == "Roman" ? 11 : 13,
                Foreground = ink
            };
            Canvas.SetLeft(label, 65 + Math.Sin(angle) * 53);
            Canvas.SetTop(label, 68 - Math.Cos(angle) * 53);
            ClockFace.Children.Insert(0, label);
        }
    }

    private void Clock_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) { e.Handled = true; CustomizeClock_Click(sender, e); }

    private async void CustomizeClock_Click(object sender, RoutedEventArgs e)
    {
        if (dialogOpen) { preferencesWindow?.Activate(); return; }
        var panel = new StackPanel { Spacing = 14 };
        Border Card(UIElement content)
        {
            var card = (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Padding='14' CornerRadius='8' BorderThickness='1' Background='{ThemeResource CardBackgroundFillColorDefaultBrush}' BorderBrush='{ThemeResource CardStrokeColorDefaultBrush}'/>");
            card.Child = content; return card;
        }
        void Choice(string header, string[] choices, string selected, Action<string> change)
        {
            var combo = new ComboBox { Header = header, ItemsSource = choices, SelectedItem = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string value) { change(value); ApplyState(); Save(); } };
            panel.Children.Add(Card(combo));
        }
        Choice("Display", ["Analog + digital", "Analog", "Digital"], state.ClockStyle, v => state.ClockStyle = v);
        var faces = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        faces.ColumnDefinitions.Add(new ColumnDefinition()); faces.ColumnDefinitions.Add(new ColumnDefinition());
        string[] faceNames = ["Minimal", "Classic", "Roman", "Railway", "Midnight", "Ivory"];
        var faceButtons = new List<Button>();
        for (int i = 0; i < faceNames.Length; i++)
        {
            if (i % 2 == 0) faces.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string face = faceNames[i];
            var button = new Button { Content = face, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12), BorderThickness = new Thickness(state.ClockFaceStyle == face ? 2 : 1) };
            button.Click += (_, _) => { state.ClockFaceStyle = face; ApplyClockPreferences(); UpdateGadgets(); Save(); foreach (var item in faceButtons) item.BorderThickness = new Thickness((string)item.Content == face ? 2 : 1); };
            Grid.SetRow(button, i / 2); Grid.SetColumn(button, i % 2); faces.Children.Add(button); faceButtons.Add(button);
        }
        var appearance = new StackPanel { Spacing = 10 };
        appearance.Children.Add(new TextBlock { Text = "Clock face", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        appearance.Children.Add(faces); panel.Children.Add(Card(appearance));
        Choice("Time format", ["System", "12-hour", "24-hour"], state.ClockTimeFormat, v => state.ClockTimeFormat = v);
        var zones = TimeZoneInfo.GetSystemTimeZones().ToList();
        var zone = new ComboBox { Header = "Time zone", ItemsSource = new[] { "Local time" }.Concat(zones.Select(z => z.DisplayName)).ToArray(), SelectedIndex = state.ClockTimeZone == "Local" ? 0 : Math.Max(0, zones.FindIndex(z => z.Id == state.ClockTimeZone) + 1), HorizontalAlignment = HorizontalAlignment.Stretch };
        zone.SelectionChanged += (_, _) => { if (zone.SelectedIndex >= 0) { state.ClockTimeZone = zone.SelectedIndex == 0 ? "Local" : zones[zone.SelectedIndex - 1].Id; ApplyState(); Save(); } };
        panel.Children.Add(Card(zone));
        var size = new NumberBox { Header = "Clock size", Minimum = 64, Maximum = 160, Value = state.ClockSize > 0 ? state.ClockSize : state.CompactClock ? 80 : 120, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        size.ValueChanged += (_, args) => { if (!double.IsNaN(args.NewValue)) { state.ClockSize = (int)args.NewValue; ApplyState(); Save(); } };
        panel.Children.Add(Card(size));
        AddToggle(panel, "Show second hand", state.ClockSecondHand, v => state.ClockSecondHand = v);
        AddToggle(panel, "Show digital seconds", state.ShowSeconds, v => state.ShowSeconds = v);
        AddToggle(panel, "Show date", state.ClockShowDate, v => state.ClockShowDate = v);
        var label = new TextBox { Header = "Clock label", Text = state.ClockLabel, PlaceholderText = "Optional label", MaxLength = 40 };
        label.LostFocus += (_, _) => { state.ClockLabel = label.Text.Trim(); ApplyState(); Save(); };
        panel.Children.Add(Card(label));
        await ShowDialog("Clock", panel);
    }
}
