using Microsoft.Win32;
using System.Security;

namespace Sidebar;

internal static class StartupPreference
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Entry = "FluentSidebar";
    private const string LegacyEntry = "ClassicFluentSidebar";
    public static bool Enabled
    {
        get
        {
            try { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(Entry) is string || key?.GetValue(LegacyEntry) is string; }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { return false; }
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value) key.SetValue(Entry, $"\"{Environment.ProcessPath}\"", RegistryValueKind.String);
                else key.DeleteValue(Entry, false);
                key.DeleteValue(LegacyEntry, false);
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { }
        }
    }
}
