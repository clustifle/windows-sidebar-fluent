using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
namespace Sidebar;
internal sealed class ShellBridge : IAsyncDisposable
{
    private readonly Process process;
    private readonly SemaphoreSlim writeLock = new(1);
    private bool disposed;
    private int disposing;
    public string LastError { get; private set; } = "";
    public ShellBridge(Action<string> update, Action ended)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "FluentSidebar.ShellHost.exe");
        process = new Process { StartInfo = new ProcessStartInfo(path, Environment.ProcessId.ToString()) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true }, EnableRaisingEvents = true };
        process.Exited += (_, _) => { if (!disposed && Volatile.Read(ref disposing) == 0) { Restore(); ended(); } };
        process.Start();
        _ = Task.Run(async () =>
        {
            try { while (await process.StandardOutput.ReadLineAsync() is { } line) if (line.StartsWith("SIDEBAR:", StringComparison.Ordinal) && line.Length < 8_000_000) update(line[8..]); }
            catch (IOException) { }
        });
        _ = Task.Run(async () => { try { while (await process.StandardError.ReadLineAsync() is { } error) { LastError = error.Length > 2048 ? error[..2048] : error; } } catch (IOException) { } });
    }
    public async Task SendAsync(object command)
    {
        await writeLock.WaitAsync();
        try { if (!disposed && !process.HasExited) { AllowSetForegroundWindow((uint)process.Id); await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(command)); await process.StandardInput.FlushAsync(); } }
        catch (IOException) { }
        finally { writeLock.Release(); }
    }
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
    public static void Restore()
    {
        string recovery = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassicSidebar");
        if (!File.Exists(Path.Combine(recovery, "taskbar-recovery.txt")) && !File.Exists(Path.Combine(recovery, "taskbar-region-recovery.json"))) return;
        string path = Path.Combine(AppContext.BaseDirectory, "FluentSidebar.ShellHost.exe");
        if (!File.Exists(path)) return;
        try { Process.Start(new ProcessStartInfo(path, "--restore") { UseShellExecute = false, CreateNoWindow = true }); } catch (System.ComponentModel.Win32Exception) { }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed || Interlocked.Exchange(ref disposing, 1) != 0) return;
        await SendAsync(new { action = "stop" }); disposed = true;
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4)); }
        catch (TimeoutException) { process.Kill(); Restore(); }
        process.Dispose();
    }
}
