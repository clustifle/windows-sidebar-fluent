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
        if (state.ClockFaceStyle == "Minimal") return;
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
        void Choice(string header, string[] choices, string selected, Action<string> change)
        {
            var combo = new ComboBox { Header = header, ItemsSource = choices, SelectedItem = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string value) { change(value); ApplyState(); Save(); } };
            panel.Children.Add(combo);
        }
        Choice("Display", ["Analog + digital", "Analog", "Digital"], state.ClockStyle, v => state.ClockStyle = v);
        Choice("Clock face", ["Minimal", "Classic", "Roman"], state.ClockFaceStyle, v => state.ClockFaceStyle = v);
        Choice("Time format", ["System", "12-hour", "24-hour"], state.ClockTimeFormat, v => state.ClockTimeFormat = v);
        var zones = TimeZoneInfo.GetSystemTimeZones().ToList();
        var zone = new ComboBox { Header = "Time zone", ItemsSource = new[] { "Local time" }.Concat(zones.Select(z => z.DisplayName)).ToArray(), SelectedIndex = state.ClockTimeZone == "Local" ? 0 : Math.Max(0, zones.FindIndex(z => z.Id == state.ClockTimeZone) + 1), HorizontalAlignment = HorizontalAlignment.Stretch };
        zone.SelectionChanged += (_, _) => { if (zone.SelectedIndex >= 0) { state.ClockTimeZone = zone.SelectedIndex == 0 ? "Local" : zones[zone.SelectedIndex - 1].Id; ApplyState(); Save(); } };
        panel.Children.Add(zone);
        var size = new NumberBox { Header = "Clock size", Minimum = 64, Maximum = 160, Value = state.ClockSize > 0 ? state.ClockSize : state.CompactClock ? 80 : 120, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        size.ValueChanged += (_, args) => { if (!double.IsNaN(args.NewValue)) { state.ClockSize = (int)args.NewValue; ApplyState(); Save(); } };
        panel.Children.Add(size);
        AddToggle(panel, "Show second hand", state.ClockSecondHand, v => state.ClockSecondHand = v);
        AddToggle(panel, "Show digital seconds", state.ShowSeconds, v => state.ShowSeconds = v);
        AddToggle(panel, "Show date", state.ClockShowDate, v => state.ClockShowDate = v);
        var label = new TextBox { Header = "Clock label", Text = state.ClockLabel, PlaceholderText = "Optional label", MaxLength = 40 };
        label.LostFocus += (_, _) => { state.ClockLabel = label.Text.Trim(); ApplyState(); Save(); };
        panel.Children.Add(label);
        await ShowDialog("Clock", panel);
    }
}
