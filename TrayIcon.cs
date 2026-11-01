using System.Runtime.InteropServices;

namespace Sidebar;

internal sealed class TrayIcon : IDisposable
{
    private readonly IntPtr hwnd, icon;
    private readonly SubclassProc subclass;
    private readonly Action<int> command;
    private readonly Func<bool> alwaysOnTop;
    private readonly uint callback = RegisterWindowMessage("ClassicSidebar.Tray.Callback");
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private bool disposed;

    public TrayIcon(IntPtr hwnd, string iconPath, Action<int> command, Func<bool> alwaysOnTop)
    {
        this.hwnd = hwnd; this.command = command; this.alwaysOnTop = alwaysOnTop;
        icon = LoadImage(IntPtr.Zero, iconPath, 1, 0, 0, 0x0010 | 0x0040);
        if (icon == IntPtr.Zero) throw new InvalidOperationException("Could not load Sidebar's tray icon.");
        subclass = WindowProcedure;
        if (!SetWindowSubclass(hwnd, subclass, 2, UIntPtr.Zero)) { DestroyIcon(icon); throw new InvalidOperationException("Could not initialize Sidebar's tray menu."); }
        Add();
    }

    private NotifyIconData Data() => new() { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = hwnd, Id = 1, Flags = 1 | 2 | 4, Callback = callback, Icon = icon, Tip = "FluentSidebar — right-click for settings", Info = "", InfoTitle = "" };
    private void Add()
    {
        var data = Data();
        if (!Shell_NotifyIcon(0, ref data)) throw new InvalidOperationException("Windows could not add Sidebar to the notification area.");
        data.Version = 4;
        Shell_NotifyIcon(4, ref data);
    }

    private IntPtr WindowProcedure(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        try
        {
            if (!disposed && message == taskbarCreated) Add();
            else if (!disposed && message == callback)
            {
                uint notification = (uint)(lParam.ToInt64() & 0xffff);
                if (notification is 0x007B or 0x0205) ShowMenu();
                else if (notification is 0x0400 or 0x0401 or 0x0202) command(1);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenu(menu, 0, 1, "Open Sidebar");
            AppendMenu(menu, 0, 2, "Settings…");
            AppendMenu(menu, 0, 3, "Categories and gadgets…");
            AppendMenu(menu, 0x0800, 0, null);
            AppendMenu(menu, StartupPreference.Enabled ? 8u : 0u, 4, "Start with Windows");
            AppendMenu(menu, alwaysOnTop() ? 8u : 0u, 5, "Always on top");
            AppendMenu(menu, 0x0800, 0, null);
            AppendMenu(menu, 0, 6, "Exit Sidebar");
            GetCursorPos(out var point);
            SetForegroundWindow(hwnd);
            int selected = TrackPopupMenuEx(menu, 0x0100 | 0x0002, point.X, point.Y, hwnd, IntPtr.Zero);
            PostMessage(hwnd, 0, UIntPtr.Zero, IntPtr.Zero);
            if (selected != 0) command(selected);
        }
        finally { DestroyMenu(menu); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var data = Data(); Shell_NotifyIcon(2, ref data);
        RemoveWindowSubclass(hwnd, subclass, 2);
        DestroyIcon(icon);
        GC.KeepAlive(subclass);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NotifyIconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr instance, string path, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
}
