using Microsoft.Win32;

namespace Sidebar;

internal static class StartupPreference
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Entry = "ClassicFluentSidebar";
    public static bool Enabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(Entry) is string; }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(Entry, $"\"{Environment.ProcessPath}\"", RegistryValueKind.String);
            else key.DeleteValue(Entry, false);
        }
    }
}
