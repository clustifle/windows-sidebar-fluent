param([ValidateRange(5,60)][int]$Seconds=45, [string]$Report=(Join-Path $PSScriptRoot '..\artifacts\desktop-test-result.txt'))
$ErrorActionPreference='Stop'
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class DesktopVisibilityTest {
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
 [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window,uint kind);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int count);
 [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr window,uint attribute,out uint value,int size);
 public static string Class(IntPtr window){var text=new StringBuilder(128);GetClassName(window,text,text.Capacity);return text.ToString();}
 public static bool DesktopAbove(IntPtr window){for(int i=0;i<128;i++){window=GetWindow(window,3);if(window==IntPtr.Zero)return false;if(IsWindowVisible(window)&&(Class(window)=="WorkerW"||Class(window)=="Progman"))return true;}return false;}
}
'@
$sidebar = @(Get-Process FluentSidebar -ErrorAction Stop)
if ($sidebar.Count -ne 1) { throw 'Run exactly one sidebar instance.' }
$window=$sidebar[0].MainWindowHandle
[IO.File]::WriteAllText($Report,'READY: invoke Windows Show Desktop.'+[Environment]::NewLine)
$watch=[Diagnostics.Stopwatch]::StartNew()
$desktopSince=$null
while($watch.Elapsed.TotalSeconds -lt $Seconds) {
    $foreground=[DesktopVisibilityTest]::GetForegroundWindow()
    $class=[DesktopVisibilityTest]::Class($foreground)
    $onDesktop=$class -in @('Progman','WorkerW') -or $foreground -eq [DesktopVisibilityTest]::GetShellWindow()
    if($onDesktop) {
        if($null -eq $desktopSince) { $desktopSince=$watch.Elapsed.TotalMilliseconds }
        if($watch.Elapsed.TotalMilliseconds-$desktopSince -ge 500) {
            [uint32]$cloaked=0; [uint32]$peek=0
            [DesktopVisibilityTest]::DwmGetWindowAttribute($window,14,[ref]$cloaked,4) | Out-Null
            $peekResult=[DesktopVisibilityTest]::DwmGetWindowAttribute($window,12,[ref]$peek,4)
            $result=[pscustomobject]@{Visible=[DesktopVisibilityTest]::IsWindowVisible($window);Minimized=[DesktopVisibilityTest]::IsIconic($window);Cloaked=$cloaked;DesktopAbove=[DesktopVisibilityTest]::DesktopAbove($window);PeekQuery=$peekResult;ExcludedFromPeek=$peek;Foreground=$class}
            $passed=$result.Visible -and -not $result.Minimized -and $result.Cloaked -eq 0 -and -not $result.DesktopAbove
            $line=if($passed){'PASS: sidebar remains visible on Show Desktop.'}else{'FAIL: sidebar disappeared on Show Desktop.'}
            [IO.File]::AppendAllText($Report,$line+[Environment]::NewLine+($result|ConvertTo-Json)+[Environment]::NewLine)
            Get-Content -LiteralPath $Report
            if(-not $passed){exit 1};exit 0
        }
    } else { $desktopSince=$null }
    Start-Sleep -Milliseconds 100
}
[IO.File]::AppendAllText($Report,'INCOMPLETE: Show Desktop was not observed.'+[Environment]::NewLine)
Get-Content -LiteralPath $Report
exit 2
