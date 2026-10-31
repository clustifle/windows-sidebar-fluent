param([string]$Directory = (Join-Path $PSScriptRoot '..\artifacts\Windows Sidebar Fluent'), [switch]$TestTaskbarRecovery)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class SidebarTaskbarProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Data {public uint Size; public IntPtr Window; public uint Callback,Edge; public int Left,Top,Right,Bottom; public IntPtr Parameter;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls,string title);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint msg,ref Data data);
 public static ulong State(IntPtr h) { var d=new Data {Size=(uint)Marshal.SizeOf<Data>(), Window=h}; return SHAppBarMessage(4,ref d).ToUInt64(); }
}
"@
$exe = Join-Path (Resolve-Path -LiteralPath $Directory).Path 'WindowsSidebar.ShellHost.exe'
$taskbar = [SidebarTaskbarProbe]::FindWindow('Shell_TrayWnd',$null)
$original = [SidebarTaskbarProbe]::State($taskbar)
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
 $probe=[Windows.Forms.NotifyIcon]::new(); $probe.Icon=[Drawing.SystemIcons]::Application
 $probe.Text='SidebarHostedTrayProbe'; $probe.Visible=$true
 $script:clicks=0; $probe.add_Click({$script:clicks++}); $probe.add_DoubleClick({$script:clicks++})
 $snapshot=Read-Snapshot
 $item=@($snapshot.tray|Where-Object title -eq 'SidebarHostedTrayProbe')
 if($item.Count -ne 1){throw 'New tray icon not hosted'}
 Send-Command @{action='tray';id=$item[0].id;x=100;y=100}
 for($i=0;$i -lt 20 -and $script:clicks -eq 0;$i++){[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 50}
 if($script:clicks -eq 0){throw 'Tray click callback was not delivered'}
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
 } else {
  Send-Command @{action='stop'}
  if(!$hostProcess.WaitForExit(5000) -or $hostProcess.ExitCode -ne 0){throw 'Shell host shutdown failed'}
 }
 [pscustomobject]@{Ready=$first.ready;RunningWindows=@($first.windows).Count;TrayIcons=@($snapshot.tray).Count;NewTrayRegistration=$true;TrayClickCallback=$true;TaskbarCrashRecovery=$recovery}
} finally {
 if($probe){$probe.Dispose()}
 if(!$hostProcess.HasExited){$hostProcess.Kill()}
 Start-Process -FilePath $exe -ArgumentList '--restore' -WindowStyle Hidden -Wait
 $hostProcess.Dispose()
}
