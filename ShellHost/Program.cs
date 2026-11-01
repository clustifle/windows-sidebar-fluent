using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using ManagedShell.AppBar;
using Application = System.Windows.Application;

internal static class Program
{
    static ExplorerHelper? explorer;
    static bool stopping;
    static int parent;
    static long parentStarted;
    static IntPtr originalTaskband, originalTaskman;
    static string previous = "";
    static readonly string recovery = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassicSidebar", "taskbar-recovery.txt");
    static void Recover(bool clear = true)
    {
        TaskbarRegionMask.Restore();
        RestoreExplorerRouting();
        if (!File.Exists(recovery)) return;
        if (!int.TryParse(File.ReadAllText(recovery).Split('|')[0], out int state)) state = 0;
        var helper = explorer ?? new ExplorerHelper();
        helper.SetTaskbarState((ManagedShell.Interop.NativeMethods.ABState)state);
        helper.SetTaskbarVisibility(0x0040);
        if (clear) File.Delete(recovery);
    }
    [STAThread] static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--restore") { Recover(); return; }
        Recover();
        if (args.Length == 0 || !int.TryParse(args[0], out parent)) return;
        try { parentStarted = Process.GetProcessById(parent).StartTime.ToUniversalTime().Ticks; } catch (ArgumentException) { return; }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => { Console.Error.WriteLine(e.Exception.Message); e.Handled = true; Stop(); };
        try
        {
            explorer = new ExplorerHelper();
            var taskbar = FindWindow("Shell_TrayWnd", null);
            originalTaskband = GetProp(taskbar, "TaskbandHWND"); originalTaskman = GetTaskmanWindow();
            var watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            watch.Tick += (_, _) =>
            {
                try { using var process = Process.GetProcessById(parent); if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != parentStarted) Stop(); else { TaskbarRegionMask.Tick(explorer.HideExplorerTaskbar); Snapshot(); } }
                catch (ArgumentException) { Stop(); }
            };
            watch.Start(); Snapshot();
            _ = Task.Run(async () =>
            {
                try { while (await Console.In.ReadLineAsync() is { } line) if (line.Length < 16384) await app.Dispatcher.InvokeAsync(() => Command(line)); }
                finally { _ = app.Dispatcher.BeginInvoke(Stop); }
            });
            app.Run();
        }
        catch (Exception e) { Console.Error.WriteLine(e.Message); Environment.ExitCode = 1; }
        finally { if (explorer is not null) explorer.HideExplorerTaskbar = false; Recover(); }
    }
    static void Stop() { if (stopping) return; stopping = true; Application.Current.Shutdown(); }
    static void Snapshot()
    {
        string data = JsonSerializer.Serialize(new { ready = true, hidden = explorer?.HideExplorerTaskbar ?? false, clean = TaskbarRegionMask.Active, cleanSupported = TaskbarRegionMask.Supported });
        if (data == previous) return; previous = data;
        Console.WriteLine("SIDEBAR:" + data); Console.Out.Flush();
    }
    static void Command(string line)
    {
        if (explorer is null || stopping) return;
        try
        {
            using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
            string action = root.GetProperty("action").GetString() ?? "";
            if (action == "stop") { Stop(); return; }
            if (action != "replace") return;
            bool enabled = root.GetProperty("enabled").GetBoolean();
            if (root.TryGetProperty("clean", out var clean)) TaskbarRegionMask.Wanted = clean.GetBoolean();
            if (enabled && !File.Exists(recovery))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
                File.WriteAllText(recovery, ((int)explorer.GetTaskbarState()).ToString());
            }
            explorer.HideExplorerTaskbar = enabled;
            if (!enabled) Recover(false);
            TaskbarRegionMask.Tick(enabled);
            Snapshot();
        }
        catch (Exception e) { Console.Error.WriteLine(e.Message); }
    }
    private static void RestoreExplorerRouting()
    {
        IntPtr tray = ManagedShell.Common.Helpers.WindowHelper.FindWindowsTray(IntPtr.Zero);
        if (tray == IntPtr.Zero) return;
        if (originalTaskband == IntPtr.Zero || !IsWindow(originalTaskband))
            EnumChildWindows(tray, (child, _) =>
            {
                var name = new System.Text.StringBuilder(128); GetClassName(child, name, name.Capacity);
                if (name.ToString() != "MSTaskSwWClass") return true;
                originalTaskband = child; return false;
            }, IntPtr.Zero);
        if (originalTaskband != IntPtr.Zero)
        {
            SetProp(tray, "TaskbandHWND", originalTaskband);
            SetTaskmanWindow(originalTaskman != IntPtr.Zero && IsWindow(originalTaskman) ? originalTaskman : originalTaskband);
        }
    }
    private delegate bool EnumWindowCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr window, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetProp(IntPtr window, string name, IntPtr value);
    [DllImport("user32.dll")] private static extern IntPtr GetTaskmanWindow();
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string name, string? title);
    [DllImport("user32.dll")] static extern bool SetTaskmanWindow(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
}
