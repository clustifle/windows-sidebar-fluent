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
    private bool sideMoveRunning;
    private bool requestedLeft;
    private bool desktopActive;
    private IntPtr foregroundHook;
    private WinEventCallback? foregroundCallback;
    private readonly DispatcherTimer revealTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly System.Diagnostics.Stopwatch revealClock = new();
    private double currentReveal = 1, revealFrom = 1, revealTo = 1, revealDuration = 260;

    private void InitializeAutoHide()
    {
        ElementCompositionPreview.SetIsTranslationEnabled(Root, true);
        autoHideInitialized = true;
        Root.Loaded += (_, _) => UpdateGadgets();
        GadgetScroller.ViewChanged += (_, args) => { if (!args.IsIntermediate) UpdateGadgets(); };
        Root.PointerEntered += (_, _) => RevealSidebar();
        Root.PointerExited += (_, _) => QueueAutoHide();
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            if (state.AutoHide && !desktopActive && !dialogOpen && !classicPickerOpen && !sidebarMenuOpen && !LonghornSearch.IsSuggestionListOpen && draggingGadget is null && !CursorInsideSidebar()) HideSidebar();
        };
        revealTimer.Tick += (_, _) => AdvanceReveal();
        foregroundCallback = (_, _, _, _, _, _, _) => DispatcherQueue.TryEnqueue(UpdateDesktopVisibility);
        foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, foregroundCallback, 0, 0, 0);
        UpdateDesktopVisibility();
        Closed += (_, _) => { sidebarClosed = true; revealGeneration++; hideTimer.Stop(); revealTimer.Stop(); if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook); };
    }

    private void ApplyAutoHidePreference()
    {
        if (!autoHideInitialized) return;
        Root.Width = state.AutoHide ? SidebarWidth : double.NaN;
        if (!state.AutoHide || desktopActive) RevealSidebar();
        else QueueAutoHide();
    }

    private void QueueAutoHide()
    {
        if (!autoHideInitialized || sidebarClosed || sideMoveRunning || !state.AutoHide || desktopActive || dialogOpen || classicPickerOpen || sidebarMenuOpen || LonghornSearch.IsSuggestionListOpen || draggingGadget is not null) return;
        hideTimer.Stop(); hideTimer.Start();
    }

    private void RevealSidebar()
    {
        if (!autoHideInitialized || sidebarClosed || sideMoveRunning) return;
        hideTimer.Stop();
        autoHidden = false;
        UpdateGadgets();
        BeginReveal(1);
    }

    private void HideSidebar()
    {
        if (autoHidden || sidebarClosed || sideMoveRunning || !state.AutoHide || desktopActive) return;
        autoHidden = true; slideTimer.Stop(); UpdateTimerInterval(); BeginReveal(0);
    }

    private void BeginReveal(double target)
    {
        if (revealTo == target && revealTimer.IsEnabled) return;
        revealTimer.Stop(); revealFrom = currentReveal; revealTo = target;
        revealDuration = Math.Max(90, (target > currentReveal ? 300 : 240) * Math.Abs(target - currentReveal));
        if (!windowsAppearance.AnimationsEnabled || !state.AutoHide || Math.Abs(target - currentReveal) < 0.001)
        { currentReveal = target; ApplyRevealFrame(); return; }
        revealClock.Restart(); revealTimer.Start();
    }

    private void AdvanceReveal()
    {
        double t = Math.Clamp(revealClock.Elapsed.TotalMilliseconds / revealDuration, 0, 1);
        // Smoothstep has zero velocity at both ends and reverses from the current position.
        double eased = t * t * (3 - 2 * t);
        currentReveal = revealFrom + (revealTo - revealFrom) * eased;
        ApplyRevealFrame();
        if (t >= 1) { revealTimer.Stop(); revealClock.Stop(); }
    }

    private void ApplyRevealFrame()
    {
        if (sidebarClosed) return;
        appBar?.SetRevealProgress(currentReveal);
        var visual = ElementCompositionPreview.GetElementVisual(Root);
        visual.StopAnimation("Translation"); visual.StopAnimation("Opacity");
        visual.Properties.InsertVector3("Translation", new Vector3(state.Left && state.AutoHide ? -(float)((SidebarWidth - 3) * (1 - currentReveal)) : 0, 0, 0));
        visual.Opacity = currentReveal <= 0.001 ? 0 : 1;
    }

    private void UpdateDesktopVisibility()
    {
        if (sidebarClosed) return;
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return;
        GetWindowThreadProcessId(foreground, out uint process);
        if (process == Environment.ProcessId) return;
        var name = new System.Text.StringBuilder(128); GetClassName(foreground, name, name.Capacity);
        string windowClass = name.ToString();
        if (windowClass is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return;
        desktopActive = windowClass is "Progman" or "WorkerW" || foreground == GetShellWindow();
        if (desktopActive) RevealSidebar(); else QueueAutoHide();
    }

    private async Task MoveSidebarAsync(bool left)
    {
        requestedLeft = left;
        LonghornSearch.IsSuggestionListOpen = false;
        if (sideMoveRunning || sidebarClosed) return;
        sideMoveRunning = true;
        hideTimer.Stop(); revealTimer.Stop(); currentReveal = revealFrom = revealTo = 1; revealGeneration++;
        try
        {
            while (!sidebarClosed && requestedLeft != state.Left)
            {
                bool destination = requestedLeft;
                var visual = ElementCompositionPreview.GetElementVisual(Root);
                void Animate(float from, float to, float opacityFrom, float opacityTo, int milliseconds)
                {
                    visual.StopAnimation("Translation"); visual.StopAnimation("Opacity");
                    var slide = visual.Compositor.CreateVector3KeyFrameAnimation();
                    var easing = visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0), new Vector2(0, 1));
                    slide.InsertKeyFrame(0, new Vector3(from, 0, 0)); slide.InsertKeyFrame(1, new Vector3(to, 0, 0), easing);
                    slide.Duration = TimeSpan.FromMilliseconds(milliseconds);
                    var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
                    fade.InsertKeyFrame(0, opacityFrom); fade.InsertKeyFrame(1, opacityTo); fade.Duration = slide.Duration;
                    visual.StartAnimation("Translation", slide); visual.StartAnimation("Opacity", fade);
                }
                bool animate = windowsAppearance.AnimationsEnabled;
                autoHidden = false; appBar?.SetRevealed(true);
                if (animate) { Animate(0, state.Left ? -48 : 48, 1, 0, 120); await Task.Delay(130); }
                if (sidebarClosed) return;
                state.Left = destination;
                appBar?.Configure(state.Left, state.AlwaysOnTop, SidebarWidth, state.AutoHide, false);
                if (animate) { Animate(state.Left ? -48 : 48, 0, 0, 1, 200); await Task.Delay(210); }
            }
        }
        finally
        {
            sideMoveRunning = false;
            if (!sidebarClosed)
            {
                var visual = ElementCompositionPreview.GetElementVisual(Root);
                visual.StopAnimation("Translation"); visual.StopAnimation("Opacity");
                visual.Properties.InsertVector3("Translation", Vector3.Zero); visual.Opacity = 1;
                Save(); QueueAutoHide();
            }
        }
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
    private delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint minimum, uint maximum, IntPtr module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int maximum);
}
