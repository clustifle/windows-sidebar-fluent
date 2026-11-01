param([ValidateRange(3,60)][int]$Seconds = 10)
$ErrorActionPreference = 'Stop'
$names = @('FluentSidebar', 'FluentSidebar.ShellHost')
$processes = @(Get-Process -Name $names -ErrorAction SilentlyContinue)
if ($processes.Count -eq 0) { throw 'Start FluentSidebar before measuring.' }
$baseline = @{}
foreach ($process in $processes) { $baseline[$process.Id] = $process.TotalProcessorTime.TotalSeconds }
$watch = [Diagnostics.Stopwatch]::StartNew()
Start-Sleep -Seconds $Seconds
$rows = foreach ($process in $processes) {
    try {
        $process.Refresh()
        if ($process.HasExited) { continue }
        [pscustomobject]@{
            Process = $process.ProcessName
            CpuPercent = [Math]::Round(100 * ($process.TotalProcessorTime.TotalSeconds - $baseline[$process.Id]) / $watch.Elapsed.TotalSeconds / [Environment]::ProcessorCount, 3)
            PrivateMB = [Math]::Round($process.PrivateMemorySize64 / 1MB, 1)
            WorkingSetMB = [Math]::Round($process.WorkingSet64 / 1MB, 1)
        }
    } catch [InvalidOperationException] { }
}
[pscustomobject]@{
    SampleSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 1)
    CpuPercent = [Math]::Round(($rows | Measure-Object CpuPercent -Sum).Sum, 3)
    PrivateMB = [Math]::Round(($rows | Measure-Object PrivateMB -Sum).Sum, 1)
    WorkingSetMB = [Math]::Round(($rows | Measure-Object WorkingSetMB -Sum).Sum, 1)
    Processes = @($rows)
} | ConvertTo-Json -Depth 3
