using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ManagedShell;
using ManagedShell.WindowsTasks;
using ManagedShell.WindowsTray;
using NotifyIcon = ManagedShell.WindowsTray.NotifyIcon;
using Application = System.Windows.Application;

internal static class Program
{
    static ShellManager? shell;
    static bool stopping;
    static string previous = "";
    static volatile bool snapshotDirty = true;
    static readonly HashSet<ApplicationWindow> observedWindows = new();
    static readonly HashSet<NotifyIcon> observedTray = new();
    static void MarkDirty(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => snapshotDirty = true;
    static void ObserveServices()
    {
        if (shell is null) return;
        var windows = shell.Tasks.GroupedWindows.Cast<ApplicationWindow>().ToHashSet();
        foreach (var removed in observedWindows.Except(windows).ToArray()) { removed.PropertyChanged -= MarkDirty; observedWindows.Remove(removed); }
        foreach (var added in windows.Except(observedWindows)) { added.PropertyChanged += MarkDirty; observedWindows.Add(added); }
        var tray = shell.NotificationArea.TrayIcons.ToHashSet();
        foreach (var removed in observedTray.Except(tray).ToArray()) { removed.PropertyChanged -= MarkDirty; observedTray.Remove(removed); }
        foreach (var added in tray.Except(observedTray)) { added.PropertyChanged += MarkDirty; observedTray.Add(added); }
        snapshotDirty = true;
    }
    static string TrayId(NotifyIcon icon) => icon.GUID != Guid.Empty ? icon.GUID.ToString("D") : icon.HWnd.ToInt64() + ":" + icon.UID;
    static readonly Dictionary<ImageSource, string> images = new();
    static int parent;
    static long parentStarted;
    static IntPtr originalTaskband, originalTaskman;
    static readonly string recovery = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassicSidebar", "taskbar-recovery.txt");
    static void Recover(bool clear = true)
    {
        RestoreExplorerRouting();
        if (!File.Exists(recovery)) return;
        if (!int.TryParse(File.ReadAllText(recovery).Split('|')[0], out int state)) state = 0;
        var helper = shell?.ExplorerHelper ?? new ManagedShell.AppBar.ExplorerHelper();
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
            var config = ShellManager.DefaultShellConfig;
            config.PinnedNotifyIcons = [];
            var explorerTray = FindWindow("Shell_TrayWnd", null);
            Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
            originalTaskband = GetProp(explorerTray, "TaskbandHWND"); originalTaskman = GetTaskmanWindow();
            File.WriteAllText(recovery, ((int)new ManagedShell.AppBar.ExplorerHelper().GetTaskbarState()).ToString());
            shell = new ShellManager(config);
            // Keep Explorer's Start menu and Windows-key handling available.
            RestoreExplorerRouting();
            if (shell.NotificationArea.IsFailed) throw new InvalidOperationException("Windows tray hosting failed");
            shell.Tasks.GroupedWindows.CollectionChanged += (_, _) => ObserveServices();
            shell.NotificationArea.TrayIcons.CollectionChanged += (_, _) => ObserveServices();
            ObserveServices();
            var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            poll.Tick += (_, _) =>
            {
                try { if (Process.GetProcessById(parent).HasExited || Process.GetProcessById(parent).StartTime.ToUniversalTime().Ticks != parentStarted) { Stop(); return; } Snapshot(); }
                catch (ArgumentException) { Stop(); }
                catch (Exception e) { Console.Error.WriteLine(e.Message); }
            };
            poll.Start(); Snapshot();
            _ = Task.Run(async () =>
            {
                try
                {
                    while (await Console.In.ReadLineAsync() is { } line)
                    {
                        if (line.Length > 16384) continue;
                        await app.Dispatcher.InvokeAsync(() => Command(line));
                    }
                }
                finally { _ = app.Dispatcher.BeginInvoke(Stop); }
            });
            app.Run();
        }
        catch (Exception e) { Console.Error.WriteLine(e.Message); Environment.ExitCode = 1; }
        finally
        {
            if (shell is not null)
            {
                shell.ExplorerHelper.HideExplorerTaskbar = false;
                shell.AppBarManager.SignalGracefulShutdown(); shell.Dispose();
                RestoreExplorerRouting();
                Recover();
            }
        }
    }
    static void Stop() { if (stopping) return; stopping = true; Application.Current.Shutdown(); }
    static string Icon(ImageSource? source)
    {
        if (source is not BitmapSource bitmap) return "";
        if (images.TryGetValue(source, out var cached)) return cached;
        try
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream(); encoder.Save(stream);
            string result = Convert.ToBase64String(stream.ToArray());
            if (images.Count > 256) images.Clear(); images[source] = result; return result;
        }
        catch { return ""; }
    }
    static void Snapshot()
    {
        if (shell is null) return;
        RestoreExplorerRouting();
        if (!snapshotDirty) return;
        snapshotDirty = false;
        var windows = shell.Tasks.GroupedWindows.Cast<ApplicationWindow>().Where(w => w.ShowInTaskbar && w.ProcId != (uint)parent).Take(128)
            .Select(w => new { id = w.Handle.ToInt64().ToString(), title = w.Title, icon = Icon(w.Icon), active = w.State == ApplicationWindow.WindowState.Active }).ToArray();
        var tray = shell.NotificationArea.TrayIcons.Where(i => !i.IsHidden).Take(128)
            .Select(i => new { id = TrayId(i), title = i.Title, icon = Icon(i.Icon) }).ToArray();
        string data = JsonSerializer.Serialize(new { windows, tray, ready = true, hidden = shell.ExplorerHelper.HideExplorerTaskbar });
        if (data == previous) return; previous = data;
        Console.WriteLine("SIDEBAR:" + data); Console.Out.Flush();
    }
    static void Command(string line)
    {
        if (shell is null || stopping) return;
        try
        {
            using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
            string action = root.GetProperty("action").GetString() ?? "";
            string id = root.TryGetProperty("id", out var item) ? item.GetString() ?? "" : "";
            if (action == "stop") { Stop(); return; }
            if (action == "replace")
            {
                bool enabled = root.GetProperty("enabled").GetBoolean();
                if (enabled && !File.Exists(recovery))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
                    File.WriteAllText(recovery, ((int)shell.ExplorerHelper.GetTaskbarState()).ToString());
                }
                shell.ExplorerHelper.HideExplorerTaskbar = enabled;
                snapshotDirty = true;
                if (!enabled) Recover(false);
                return;
            }
            if (action == "window")
            {
                var window = shell.Tasks.GroupedWindows.Cast<ApplicationWindow>().FirstOrDefault(w => w.Handle.ToInt64().ToString() == id);
                if (window is null) return;
                if (window.State == ApplicationWindow.WindowState.Active) window.Minimize(); else window.BringToFront();
            }
            else if (action == "close") shell.Tasks.GroupedWindows.Cast<ApplicationWindow>().FirstOrDefault(w => w.Handle.ToInt64().ToString() == id)?.Close();
            else if (action is "tray" or "trayMenu")
            {
                var icon = shell.NotificationArea.TrayIcons.FirstOrDefault(i => TrayId(i) == id);
                if (icon is null) return;
                int x = root.GetProperty("x").GetInt32(), y = root.GetProperty("y").GetInt32();
                icon.Placement = new ManagedShell.Interop.NativeMethods.Rect { Left = x - 12, Top = y - 12, Right = x + 12, Bottom = y + 12 };
                uint position = unchecked((uint)((y << 16) | (x & 0xffff)));
                var button = action == "trayMenu" ? MouseButton.Right : MouseButton.Left;
                icon.IconMouseDown(button, position, (int)GetDoubleClickTime()); icon.IconMouseUp(button, position, (int)GetDoubleClickTime());
            }
        }
        catch (Exception e) { Console.Error.WriteLine(e.Message); }
    }
    private static void RestoreExplorerRouting()
    {
        IntPtr tray = ManagedShell.Common.Helpers.WindowHelper.FindWindowsTray(shell?.NotificationArea.Handle ?? IntPtr.Zero);
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
            if (shell?.NotificationArea.Handle is { } host && host != IntPtr.Zero && IsWindow(host)) SetProp(host, "TaskbandHWND", originalTaskband);
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
