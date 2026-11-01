# Read-only integration probe: records the shell work area and relevant window bounds.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class SidebarDesktopProbe {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public uint Size; public RECT Monitor, Work; public uint Flags; }
  [StructLayout(LayoutKind.Sequential)] public struct NOTIFYICONIDENTIFIER { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
  [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
  [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out RECT rect, int size);
  [DllImport("dwmapi.dll", EntryPoint="DwmGetWindowAttribute")] public static extern int GetBorderColor(IntPtr hwnd, uint attribute, out uint color, int size);
  [DllImport("shell32.dll")] public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT rect);
}
'@
$monitorInfo = New-Object SidebarDesktopProbe+MONITORINFO
$monitorInfo.Size = [Runtime.InteropServices.Marshal]::SizeOf($monitorInfo)
$monitorHandle = [SidebarDesktopProbe]::MonitorFromWindow([IntPtr]::Zero, 1)
if (-not [SidebarDesktopProbe]::GetMonitorInfo($monitorHandle, [ref]$monitorInfo)) { throw 'Cannot read desktop work area.' }
$windowInfo = @(Get-Process Sidebar,'FluentSidebar',Notepad -ErrorAction SilentlyContinue | Where-Object MainWindowHandle -ne 0 | ForEach-Object {
    $rect = New-Object SidebarDesktopProbe+RECT
    $frame = New-Object SidebarDesktopProbe+RECT
    [void][SidebarDesktopProbe]::GetWindowRect($_.MainWindowHandle, [ref]$rect)
    [void][SidebarDesktopProbe]::DwmGetWindowAttribute($_.MainWindowHandle, 9, [ref]$frame, 16)
    $style = [SidebarDesktopProbe]::GetWindowLongPtr($_.MainWindowHandle, -20).ToInt64()
    $normalStyle = [SidebarDesktopProbe]::GetWindowLongPtr($_.MainWindowHandle, -16).ToInt64()
    [uint32]$borderColor = 0
    $borderResult = [SidebarDesktopProbe]::GetBorderColor($_.MainWindowHandle, 34, [ref]$borderColor, 4)
    $identifier = New-Object SidebarDesktopProbe+NOTIFYICONIDENTIFIER
    $identifier.Size = [Runtime.InteropServices.Marshal]::SizeOf($identifier)
    $identifier.Window = $_.MainWindowHandle
    $identifier.Id = 1
    $trayRect = New-Object SidebarDesktopProbe+RECT
    $trayResult = [SidebarDesktopProbe]::Shell_NotifyIconGetRect([ref]$identifier, [ref]$trayRect)
    [pscustomobject]@{ Name = $_.ProcessName; Bounds = $rect; VisibleFrame = $frame; ToolWindow = ($style -band 0x80) -ne 0; AppWindow = ($style -band 0x40000) -ne 0; HasNativeFrame = ($normalStyle -band 0x00C40000) -ne 0; DwmBorderSuppressed = ($borderResult -eq 0 -and $borderColor -eq [uint32]4294967294); TrayIconRegistered = $trayResult -eq 0 }
})
[pscustomobject]@{ Monitor = $monitorInfo.Monitor; WorkArea = $monitorInfo.Work; Windows = $windowInfo } | ConvertTo-Json -Depth 5
