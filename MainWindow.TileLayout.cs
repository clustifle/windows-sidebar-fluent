using Microsoft.UI.Xaml;

namespace Sidebar;

public partial class MainWindow
{
    // Sizes change only with an explicit layout/clock preference, never with live data.
    private void ApplyGadgetSizes()
    {
        bool rail = state.LonghornMode;
        CalendarGadget.Height = rail ? 230 : 300;
        NotesGadget.Height = rail ? 180 : Math.Clamp(state.NoteHeight, 72, 240) + 86;
        SystemGadget.Height = rail ? 152 : 168;
        TimerCategory.Height = rail ? 214 : 230;
        StorageCategory.Height = rail ? 124 : 136;
        SlideShowGadget.Height = rail ? 238 : 250;
        PuzzleGadget.Height = Math.Clamp(SidebarWidth - (rail ? 24 : 48), 100, 260) + (rail ? 120 : 132);
        FeedsGadget.Height = rail ? 290 : 310;
        LaunchCategory.Height = rail ? 136 : 180;
        double dial = state.ClockStyle == "Digital" ? 0 : ClockView.Height + 4;
        ClockGadget.Height = dial + (state.ClockStyle == "Analog" ? 0 : rail ? 24 : 36) + (state.ClockShowDate ? 36 : 0) + (string.IsNullOrWhiteSpace(state.ClockLabel) ? 0 : 32) + (rail ? 20 : 28);
    }
}
