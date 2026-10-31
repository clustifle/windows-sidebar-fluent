using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Sidebar;

/// <summary>Reserves desktop space through the Windows shell, without changing global work-area settings.</summary>
internal sealed class DesktopAppBar : IDisposable
{
    private const uint AbmNew = 0, AbmRemove = 1, AbmQueryPos = 2, AbmSetPos = 3;
    private const uint AbmActivate = 6, AbmWindowPosChanged = 9;
    private readonly IntPtr hwnd;
    private readonly SubclassProc subclass;
    private readonly Action<Action> enqueue;
    private readonly uint callbackMessage = RegisterWindowMessage("ClassicSidebar.AppBar.Notification");
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private bool registered, disposed, positioning, positionQueued;
    private bool left, topmost, fullscreen;
    private Rect lastRect;
    private bool hasPosition;
    private int logicalWidth = 240;
    private bool autoHide, revealed = true;

    public DesktopAppBar(IntPtr hwnd, Action<Action> enqueue)
    {
        this.hwnd = hwnd;
        this.enqueue = enqueue;
        subclass = WindowProcedure;
        if (!SetWindowSubclass(hwnd, subclass, 1, UIntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot attach sidebar shell notifications.");
        try { Register(); }
        catch { RemoveWindowSubclass(hwnd, subclass, 1); throw; }
    }

    public void Configure(bool onLeft, bool aboveWindows, int width = 240, bool hideAutomatically = false)
    {
        left = onLeft;
        topmost = aboveWindows;
        logicalWidth = Math.Clamp(width, 220, 360);
        autoHide = hideAutomatically;
        if (!autoHide) revealed = true;
        Position();
        UpdateZOrder();
    }

    public void SetRevealed(bool value)
    {
        revealed = value || !autoHide;
        Position(); UpdateZOrder();
    }

    private AppBarData Data() => new() { Size = (uint)Marshal.SizeOf<AppBarData>(), Window = hwnd, CallbackMessage = callbackMessage };

    private void Register()
    {
        var data = Data();
        registered = SHAppBarMessage(AbmNew, ref data) != UIntPtr.Zero;
        hasPosition = false;
        if (!registered) throw new InvalidOperationException("Windows could not reserve space for Sidebar.");
    }

    private void Position()
    {
        if (disposed || !registered || positioning) return;
        positioning = true;
        try
        {
            var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor)) return;
            // Start from the full monitor every time: WorkArea already includes our own reservation.
            // QUERYPOS negotiates around the taskbar and other registered appbars.
            int width = (int)Math.Round(logicalWidth * GetDpiForWindow(hwnd) / 96d);
            int reservedWidth = autoHide ? Math.Max(3, (int)Math.Round(3 * GetDpiForWindow(hwnd) / 96d)) : width;
            var data = Data();
            data.Edge = left ? 0u : 2u;
            data.Rect = monitor.Monitor;
            if (left) data.Rect.Right = data.Rect.Left + reservedWidth;
            else data.Rect.Left = data.Rect.Right - reservedWidth;
            SHAppBarMessage(AbmQueryPos, ref data);
            if (left) data.Rect.Right = data.Rect.Left + reservedWidth;
            else data.Rect.Left = data.Rect.Right - reservedWidth;
            if (!hasPosition || !data.Rect.Equals(lastRect))
            {
                SHAppBarMessage(AbmSetPos, ref data);
                lastRect = data.Rect;
                hasPosition = true;
            }
            var windowRect = data.Rect;
            if (autoHide && revealed)
            {
                if (left) windowRect.Right = windowRect.Left + width;
                else windowRect.Left = windowRect.Right - width;
            }
            if (!GetWindowRect(hwnd, out var actual) || !actual.Equals(windowRect))
                SetWindowPos(hwnd, IntPtr.Zero, windowRect.Left, windowRect.Top,
                    windowRect.Right - windowRect.Left, windowRect.Bottom - windowRect.Top, 0x0004 | 0x0010);
        }
        finally { positioning = false; }
    }

    private void QueuePosition()
    {
        if (positioning || positionQueued || disposed) return;
        positionQueued = true;
        enqueue(() => { positionQueued = false; if (!disposed) Position(); });
    }

    private void UpdateZOrder()
    {
        // Yield to full-screen applications, even when the user enabled always-on-top.
        SetWindowPos(hwnd, new IntPtr(fullscreen ? 1 : topmost || autoHide ? -1 : -2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    private IntPtr WindowProcedure(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        // Never let managed exceptions cross a native window procedure boundary.
        try
        {
            if (!disposed)
            {
                if (message == callbackMessage)
                {
                    if (wParam.ToUInt64() == 1) QueuePosition(); // ABN_POSCHANGED
                    else if (wParam.ToUInt64() == 2) { fullscreen = lParam != IntPtr.Zero; UpdateZOrder(); }
                }
                else if (message == taskbarCreated)
                {
                    registered = false;
                    Register();
                    QueuePosition();
                }
                else if (message is 0x007E or 0x02E0) QueuePosition(); // display / DPI change
                else if (message == 0x0006 && registered)
                {
                    var notification = Data();
                    notification.Parameter = new IntPtr((long)(wParam.ToUInt64() & 0xffff) == 0 ? 0 : 1);
                    SHAppBarMessage(AbmActivate, ref notification);
                }
                else if (message == 0x0047 && registered && !positioning)
                {
                    var notification = Data();
                    SHAppBarMessage(AbmWindowPosChanged, ref notification);
                    QueuePosition();
                }
                else if (message == 0x0082) Dispose(); // WM_NCDESTROY safety net
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (registered)
        {
            var data = Data();
            SHAppBarMessage(AbmRemove, ref data);
            registered = false;
        }
        RemoveWindowSubclass(hwnd, subclass, 1);
        GC.KeepAlive(subclass);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct AppBarData { public uint Size; public IntPtr Window; public uint CallbackMessage, Edge; public Rect Rect; public IntPtr Parameter; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
    [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
