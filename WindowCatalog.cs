using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace Sidebar;

internal sealed record RunningWindow(IntPtr Handle, string Name, string IconPath);
internal static class WindowCatalog
{
    internal static Task<IReadOnlyList<RunningWindow>> GetAsync() => Task.Run<IReadOnlyList<RunningWindow>>(() =>
    {
        var result = new List<RunningWindow>(); var icons = new Dictionary<uint, string>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, 4) != IntPtr.Zero || (GetWindowLongPtr(hwnd, -20).ToInt64() & 0x80) != 0) return true;
            GetWindowThreadProcessId(hwnd, out uint pid); if (pid == Environment.ProcessId) return true;
            if (DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            var title = new StringBuilder(512); GetWindowText(hwnd, title, title.Capacity); if (title.Length == 0) return true;
            if (!icons.TryGetValue(pid, out string? path))
            {
                path = "";
                try { using var process = Process.GetProcessById((int)pid); path = process.MainModule?.FileName ?? ""; }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException) { }
                icons[pid] = path;
            }
            result.Add(new(hwnd, title.ToString(), path)); return true;
        }, IntPtr.Zero);
        return result.OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    });
    internal static bool Activate(IntPtr hwnd)
    { if (!IsWindow(hwnd)) return false; if (IsIconic(hwnd)) ShowWindowAsync(hwnd, 9); return SetForegroundWindow(hwnd); }
    private delegate bool EnumCallback(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maximum);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out int value, int size);
}
