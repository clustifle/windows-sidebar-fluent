using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;

// Keeps Explorer running. No Explorer injection or persistent policy changes.
internal static class TaskbarRegionMask
{
    private sealed record SavedRegion(long Window, int Process, long Started, string? Data);
    private sealed record TrayBounds(IntPtr Window, int Left, int Top, int Right, int Bottom);
    private static readonly string recovery = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassicSidebar", "taskbar-region-recovery.json");
    private static readonly Dictionary<IntPtr, SavedRegion> saved = new();
    private static Task<TrayBounds?>? pending;
    private static DateTime nextProbe;
    private static AutomationElement? cachedTray;
    private static IntPtr cachedWindow;
    public static bool Wanted { get; set; }
    public static bool Active { get; private set; }
    public static bool Supported { get; private set; } = true;
    public static void Tick(bool taskbarHidden)
    {
        if (!Wanted || taskbarHidden) { Restore(); return; }
        if (pending is { IsCompleted: true })
        {
            try { var bounds = pending.GetAwaiter().GetResult(); Supported = bounds is not null; if (bounds is not null) Apply(bounds); else Restore(); }
            catch { Supported = false; Restore(); }
            pending = null;
        }
        if (pending is null && DateTime.UtcNow >= nextProbe)
        { nextProbe = DateTime.UtcNow.AddSeconds(2); pending = Task.Run(Probe); }
    }
    private static TrayBounds? Probe()
    {
        IntPtr window = FindWindow("Shell_TrayWnd", null);
        if (window == IntPtr.Zero) return null;
        if (cachedWindow != window) { cachedTray = null; cachedWindow = window; }
        if (cachedTray is null)
        {
            var root = AutomationElement.FromHandle(window);
            var condition = new OrCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayFrame"),
                new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.SystemTrayFrame"),
                new PropertyCondition(AutomationElement.ClassNameProperty, "TrayNotifyWnd"));
            cachedTray = root.FindFirst(TreeScope.Descendants, condition);
        }
        var tray = cachedTray;
        if (tray is null) return null;
        System.Windows.Rect bounds;
        try { bounds = tray.Current.BoundingRectangle; } catch (ElementNotAvailableException) { cachedTray = null; return null; }
        if (bounds.IsEmpty || bounds.Width < 8 || bounds.Height < 8) return null;
        return new TrayBounds(window, (int)Math.Floor(bounds.Left), (int)Math.Floor(bounds.Top), (int)Math.Ceiling(bounds.Right), (int)Math.Ceiling(bounds.Bottom));
    }
    private static void Apply(TrayBounds tray)
    {
        if (!GetWindowRect(tray.Window, out var rect)) return;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        bool horizontal = width > height;
        int start = horizontal ? tray.Left - rect.Left : tray.Top - rect.Top;
        int end = horizontal ? tray.Right - rect.Left : tray.Bottom - rect.Top;
        int length = horizontal ? width : height;
        if (start < -2 || end > length + 2 || start >= end || end - start > length / 2) { Supported = false; return; }
        int low = (start + end) / 2 > length / 2 ? 0 : end;
        int high = low == 0 ? start : length;
        if (high - low < length / 2) { Supported = false; return; }
        IntPtr existing = CreateRectRgn(0, 0, 0, 0);
        try
        {
            if (Active && GetWindowRgn(tray.Window, existing) != 0 && GetRgnBox(existing, out var current) != 0 && current.Left == (horizontal ? low : 0) && current.Top == (horizontal ? 0 : low) && current.Right == (horizontal ? high : width) && current.Bottom == (horizontal ? height : high)) return;
        }
        finally { DeleteObject(existing); }
        if (!saved.ContainsKey(tray.Window))
        {
            GetWindowThreadProcessId(tray.Window, out uint pid);
            using var process = Process.GetProcessById((int)pid);
            IntPtr original = CreateRectRgn(0, 0, 0, 0);
            string? data = null;
            try
            {
                if (GetWindowRgn(tray.Window, original) != 0)
                {
                    uint size = GetRegionData(original, 0, null);
                    if (size > 0 && size < 1_000_000) { var bytes = new byte[size]; GetRegionData(original, size, bytes); data = Convert.ToBase64String(bytes); }
                    else throw new InvalidOperationException("Cannot preserve taskbar region.");
                }
            }
            finally { DeleteObject(original); }
            saved[tray.Window] = new SavedRegion(tray.Window.ToInt64(), (int)pid, process.StartTime.ToUniversalTime().Ticks, data);
            Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
            File.WriteAllText(recovery, JsonSerializer.Serialize(saved.Values));
        }
        IntPtr region = horizontal ? CreateRectRgn(low, 0, high, height) : CreateRectRgn(0, low, width, high);
        if (region == IntPtr.Zero) return;
        if (SetWindowRgn(tray.Window, region, true) == 0) { DeleteObject(region); Supported = false; return; }
        Active = true;
    }
    public static void Restore()
    {
        Active = false;
        if (saved.Count == 0 && !File.Exists(recovery)) return;
        try
        {
            var records = saved.Count > 0 ? saved.Values.ToArray() : JsonSerializer.Deserialize<SavedRegion[]>(File.ReadAllText(recovery)) ?? [];
            var remaining = new List<SavedRegion>();
            foreach (var record in records)
            {
                IntPtr window = new(record.Window);
                if (!IsWindow(window)) continue;
                GetWindowThreadProcessId(window, out uint pid);
                if (pid != record.Process) continue;
                using var process = Process.GetProcessById(record.Process);
                if (process.StartTime.ToUniversalTime().Ticks != record.Started) continue;
                IntPtr region = IntPtr.Zero;
                if (record.Data is not null) { byte[] data = Convert.FromBase64String(record.Data); region = ExtCreateRegion(IntPtr.Zero, (uint)data.Length, data); if (region == IntPtr.Zero) { remaining.Add(record); continue; } }
                if (SetWindowRgn(window, region, true) == 0) { if (region != IntPtr.Zero) DeleteObject(region); remaining.Add(record); }
            }
            saved.Clear(); foreach (var record in remaining) saved[new IntPtr(record.Window)] = record;
            if (remaining.Count == 0) File.Delete(recovery); else File.WriteAllText(recovery, JsonSerializer.Serialize(remaining));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException) { Console.Error.WriteLine("Taskbar region recovery: " + ex.Message); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string name, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern uint GetRegionData(IntPtr region, uint size, [Out] byte[]? data);
    [DllImport("gdi32.dll")] private static extern IntPtr ExtCreateRegion(IntPtr transform, uint count, byte[] data);
    [DllImport("gdi32.dll")] private static extern int GetRgnBox(IntPtr region, out Rect rect);
}
