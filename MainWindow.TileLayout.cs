using Microsoft.UI.Xaml;

namespace Sidebar;

public partial class MainWindow
{
    // Sizes change only with an explicit layout/clock preference, never with live data.
    private void ApplyGadgetSizes()
    {
        bool rail = state.LonghornMode;
        bool vista = !rail && state.AppearancePreset == "Vista";
        CalendarGadget.Height = rail ? 230 : vista ? 360 : 320;
        NotesGadget.Height = rail ? 180 : Math.Clamp(state.NoteHeight, 72, 240) + (vista ? 112 : 94);
        SystemGadget.Height = rail ? 152 : vista ? 198 : 180;
        TimerCategory.Height = rail ? 214 : vista ? 275 : 245;
        StorageCategory.Height = rail ? 124 : vista ? 170 : 148;
        SlideShowGadget.Height = rail ? 238 : vista ? 310 : 270;
        SlideImageFrame.Height = rail ? 130 : vista ? 180 : 144;
        PuzzleGadget.Height = Math.Clamp(SidebarWidth - (rail ? 24 : vista ? 32 : 48), 100, 320) + (rail ? 120 : vista ? 168 : 140);
        FeedsGadget.Height = rail ? 290 : vista ? 380 : 330;
        LaunchCategory.Height = rail ? 136 : vista ? 228 : 190;
        double dial = state.ClockStyle == "Digital" ? 0 : ClockView.Height + 4;
        ClockGadget.Height = dial + (state.ClockStyle == "Analog" ? 0 : rail ? 24 : 36) + (state.ClockShowDate ? 36 : 0) + (string.IsNullOrWhiteSpace(state.ClockLabel) ? 0 : 32) + (rail ? 20 : 28);
    }
}
