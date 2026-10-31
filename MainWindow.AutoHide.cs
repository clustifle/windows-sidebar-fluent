using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Sidebar;

public partial class MainWindow
{
    private readonly DispatcherTimer hideTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool autoHideInitialized, autoHidden, sidebarMenuOpen, sidebarClosed;
    private int revealGeneration;

    private void InitializeAutoHide()
    {
        ElementCompositionPreview.SetIsTranslationEnabled(Root, true);
        autoHideInitialized = true;
        Root.PointerEntered += (_, _) => RevealSidebar();
        Root.PointerExited += (_, _) => QueueAutoHide();
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            if (state.AutoHide && !dialogOpen && !classicPickerOpen && !sidebarMenuOpen && draggingGadget is null && !CursorInsideSidebar()) HideSidebar();
        };
        Closed += (_, _) => { sidebarClosed = true; revealGeneration++; hideTimer.Stop(); };
    }

    private void ApplyAutoHidePreference()
    {
        if (!autoHideInitialized) return;
        Root.Width = state.AutoHide ? Math.Clamp(state.Width, 220, 360) : double.NaN;
        if (!state.AutoHide) RevealSidebar();
        else QueueAutoHide();
    }

    private void QueueAutoHide()
    {
        if (!autoHideInitialized || sidebarClosed || !state.AutoHide || dialogOpen || classicPickerOpen || sidebarMenuOpen || draggingGadget is not null) return;
        hideTimer.Stop(); hideTimer.Start();
    }

    private void RevealSidebar()
    {
        if (!autoHideInitialized || sidebarClosed) return;
        hideTimer.Stop(); revealGeneration++;
        var visual = ElementCompositionPreview.GetElementVisual(Root);
        visual.StopAnimation("Translation"); visual.StopAnimation("Opacity");
        bool animate = autoHidden && windowsAppearance.AnimationsEnabled;
        autoHidden = false;
        appBar?.SetRevealed(true);
        if (animate)
        {
            var slide = visual.Compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(0, new Vector3(state.Left ? -20 : 20, 0, 0)); slide.InsertKeyFrame(1, Vector3.Zero); slide.Duration = TimeSpan.FromMilliseconds(180);
            var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 0); fade.InsertKeyFrame(1, 1); fade.Duration = slide.Duration;
            visual.StartAnimation("Translation", slide); visual.StartAnimation("Opacity", fade);
        }
        else { visual.Properties.InsertVector3("Translation", Vector3.Zero); visual.Opacity = 1; }
    }

    private async void HideSidebar()
    {
        if (autoHidden || sidebarClosed || !state.AutoHide) return;
        int generation = ++revealGeneration;
        var visual = ElementCompositionPreview.GetElementVisual(Root);
        if (windowsAppearance.AnimationsEnabled)
        {
            var slide = visual.Compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(0, Vector3.Zero); slide.InsertKeyFrame(1, new Vector3(state.Left ? -20 : 20, 0, 0)); slide.Duration = TimeSpan.FromMilliseconds(160);
            var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 1); fade.InsertKeyFrame(1, 0); fade.Duration = slide.Duration;
            visual.StartAnimation("Translation", slide); visual.StartAnimation("Opacity", fade);
            await Task.Delay(180);
        }
        if (sidebarClosed || generation != revealGeneration || !state.AutoHide) return;
        visual.StopAnimation("Translation"); visual.StopAnimation("Opacity");
        visual.Properties.InsertVector3("Translation", Vector3.Zero); visual.Opacity = 0;
        autoHidden = true; appBar?.SetRevealed(false);
    }

    private bool CursorInsideSidebar()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        return GetCursorPos(out var point) && GetWindowRect(hwnd, out var rect) && point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PointerPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out PointerPoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
}
