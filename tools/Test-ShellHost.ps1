param([string]$Directory = (Join-Path $PSScriptRoot '..\artifacts\FluentSidebar'), [switch]$TestTaskbarRecovery, [switch]$TestSystemArea)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class SidebarTaskbarProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Data {public uint Size; public IntPtr Window; public uint Callback,Edge; public int Left,Top,Right,Bottom; public IntPtr Parameter;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls,string title);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint msg,ref Data data);
 [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr w,IntPtr r);
 [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
 [DllImport("gdi32.dll")] static extern uint GetRegionData(IntPtr r,uint size,byte[] data);
 [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr r);
 public static ulong State(IntPtr h) { var d=new Data {Size=(uint)Marshal.SizeOf<Data>(), Window=h}; return SHAppBarMessage(4,ref d).ToUInt64(); }
 public static string Region(IntPtr w) { var r=CreateRectRgn(0,0,0,0); try { if(GetWindowRgn(w,r)==0)return "none";uint n=GetRegionData(r,0,null);var data=new byte[n];GetRegionData(r,n,data);return Convert.ToBase64String(data); } finally {DeleteObject(r);} }
}
"@
$exe = Join-Path (Resolve-Path -LiteralPath $Directory).Path 'FluentSidebar.ShellHost.exe'
$taskbar = [SidebarTaskbarProbe]::FindWindow('Shell_TrayWnd',$null)
$original = [SidebarTaskbarProbe]::State($taskbar)
$originalRegion = [SidebarTaskbarProbe]::Region($taskbar)
$info = [Diagnostics.ProcessStartInfo]::new($exe,"$PID")
$info.UseShellExecute=$false; $info.CreateNoWindow=$true
$info.RedirectStandardInput=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
$hostProcess=[Diagnostics.Process]::Start($info)
$probe=$null
function Read-Snapshot {
 for($i=0;$i -lt 8;$i++) {
  $read=$hostProcess.StandardOutput.ReadLineAsync()
  if(!$read.Wait(10000)){throw 'Shell snapshot timed out'}
  $line=$read.Result
  if($null -eq $line){throw ('Shell exited: '+$hostProcess.StandardError.ReadToEnd())}
  if($line.StartsWith('SIDEBAR:')){return ($line.Substring(8)|ConvertFrom-Json)}
 }
 throw 'No shell snapshot'
}
function Send-Command($command) { $hostProcess.StandardInput.WriteLine(($command|ConvertTo-Json -Compress)); $hostProcess.StandardInput.Flush() }
try {
 $first=Read-Snapshot
 if($first.PSObject.Properties['windows'] -or $first.PSObject.Properties['tray']){throw 'Task and tray services must be disabled'}
 $systemArea=$null
 if($TestSystemArea) {
  Send-Command @{action='replace';enabled=$false;clean=$true}
  $clip=Read-Snapshot
  for($attempt=0;$attempt -lt 3 -and !$clip.clean -and $clip.cleanSupported;$attempt++){$clip=Read-Snapshot}
  if(!$clip.clean){throw 'Windows taskbar system area could not be identified'}
  if([SidebarTaskbarProbe]::Region($taskbar) -eq $originalRegion){throw 'Taskbar region did not change'}
  $maskedRegion=[SidebarTaskbarProbe]::Region($taskbar)
  for($sample=0;$sample -lt 5;$sample++){Start-Sleep -Seconds 1;if([SidebarTaskbarProbe]::Region($taskbar) -ne $maskedRegion){throw 'Taskbar region was not stable across refreshes'}}
  Send-Command @{action='replace';enabled=$false;clean=$false}
  $restored=Read-Snapshot
  $systemArea=(!$restored.clean -and [SidebarTaskbarProbe]::Region($taskbar) -eq $originalRegion)
  if(!$systemArea){throw 'Original taskbar region was not restored'}
 }
 $recovery=$null
 if($TestTaskbarRecovery) {
  Send-Command @{action='replace';enabled=$true}
  $hidden=Read-Snapshot
  if(!$hidden.hidden){throw 'Replacement mode not enabled'}
  $hostProcess.Kill(); $hostProcess.WaitForExit()
  $restore=Start-Process -FilePath $exe -ArgumentList '--restore' -WindowStyle Hidden -PassThru -Wait
  if($restore.ExitCode -ne 0){throw 'Recovery failed'}
  $recovery=([SidebarTaskbarProbe]::IsWindowVisible($taskbar) -and [SidebarTaskbarProbe]::State($taskbar) -eq $original)
  if(!$recovery){throw 'Taskbar state was not restored after helper termination'}
  if($TestSystemArea) {
   $hostProcess.Dispose(); $hostProcess=[Diagnostics.Process]::Start($info); $restart=Read-Snapshot
   Send-Command @{action='replace';enabled=$false;clean=$true};$clip=Read-Snapshot
   for($attempt=0;$attempt -lt 3 -and !$clip.clean -and $clip.cleanSupported;$attempt++){$clip=Read-Snapshot}
   if(!$clip.clean){throw 'System area did not hide on restart'}
   $hostProcess.Kill();$hostProcess.WaitForExit();Start-Process -FilePath $exe -ArgumentList '--restore' -WindowStyle Hidden -Wait
   if([SidebarTaskbarProbe]::Region($taskbar) -ne $originalRegion){throw 'Taskbar region was not restored after helper crash'}
  }
 } else {
  Send-Command @{action='stop'}
  if(!$hostProcess.WaitForExit(5000) -or $hostProcess.ExitCode -ne 0){throw 'Shell host shutdown failed'}
 }
 [pscustomobject]@{Ready=$first.ready;TaskTrackingDisabled=$true;TrayHostingDisabled=$true;SystemAreaHideAndRestore=$systemArea;TaskbarCrashRecovery=$recovery}
} finally {
 if($probe){$probe.Dispose()}
 if(!$hostProcess.HasExited){$hostProcess.Kill()}
 Start-Process -FilePath $exe -ArgumentList '--restore' -WindowStyle Hidden -Wait
 $hostProcess.Dispose()
}
