# Tests the current user's installed instance. Temporarily hides and restarts only PCStatusBar.
param()
$ErrorActionPreference = 'Stop'
$exe = Join-Path $env:LOCALAPPDATA 'Programs\PCStatusBar\PCStatusBar.exe'
$snapshot = Join-Path $env:LOCALAPPDATA 'PCStatusBar\diagnostics.local.json'
if (!(Test-Path -LiteralPath $exe)) { throw 'Install PCStatusBar first' }
if (!@(Get-Process PCStatusBar -ErrorAction SilentlyContinue | Where-Object Path -EQ $exe).Count) { throw 'Start the installed panel first' }
function Command([string]$argument) {
    $p = Start-Process -FilePath $exe -ArgumentList $argument -WindowStyle Hidden -PassThru
    if (!$p.WaitForExit(5000)) { throw "Command did not finish: $argument" }
}
function Snapshot {
    $previous = if (Test-Path -LiteralPath $snapshot) { (Get-Item -LiteralPath $snapshot).LastWriteTimeUtc } else { [datetime]::MinValue }
    Command '--diagnostics'
    for ($i = 0; $i -lt 50; $i++) {
        if ((Test-Path -LiteralPath $snapshot) -and (Get-Item -LiteralPath $snapshot).LastWriteTimeUtc -gt $previous) { return Get-Content -LiteralPath $snapshot -Raw | ConvertFrom-Json }
        Start-Sleep -Milliseconds 100
    }
    throw 'Diagnostics timeout'
}
function Check([bool]$condition, [string]$name) { if (!$condition) { throw "FAIL $name" }; Write-Output "PASS $name" }
Command '--show'
Start-Sleep -Seconds 3
$shown = Snapshot
if (!$shown.CpuInsights.HasBaseline) { Start-Sleep -Seconds 4; $shown = Snapshot }
Check ($shown.Visible -and $shown.registered) 'visible registered AppBar'
Check (@($shown.Metrics | Where-Object { $null -ne $_.Percent }).Count -eq 4) 'CPU RAM and both real disk metrics available'
$diskBlocks=@($shown.Blocks | Where-Object AccessibleName -In @('C: свободно','Разделитель дисков','D: свободно'))
Check (($diskBlocks.AccessibleName -join '|') -eq 'C: свободно|Разделитель дисков|D: свободно') 'C separator D display order'
Check ($null -ne $shown.CpuInsights.Queue -and $shown.CpuInsights.Queue -ge 0) 'real CPU queue counter available'
Check ($shown.CpuInsights.HasBaseline -and @($shown.CpuInsights.Groups).Count -gt 0) 'real CPU consumers measured over interval'
Check (@($shown.CpuHistory | Where-Object { $null -ne $_.Value }).Count -gt 0) 'CPU graph contains measured samples'
Check (@($shown.Quotas).Count -eq 2 -and @($shown.Quotas | ForEach-Object { $_.Windows }).Count -eq 4) 'ticker exposes both windows for both providers'
Check ($shown.TickerBounds.Width -gt 0 -and $shown.TickerBounds.Right -le ($shown.Bounds.Width - $shown.ContentWidth)) 'ticker stays to the left of system blocks'
Check ($shown.Bounds.Bottom -le $shown.Taskbar.Top) 'panel above bottom taskbar'
Command '--hide'
$hidden = Snapshot
Check (!$hidden.Visible -and !$hidden.registered) 'hide unregisters AppBar'
$workShown = @($shown.Monitors | Where-Object Primary)[0].WorkingArea
$workHidden = @($hidden.Monitors | Where-Object Primary)[0].WorkingArea
Check (($workHidden.Height - $workShown.Height) -eq $shown.Bounds.Height) 'hide releases exactly panel height'
Command '--show'
$again = Snapshot
Check ($again.Bounds.Top -eq $shown.Bounds.Top) 'show does not accumulate reservation'
Command '--check-maximized'
Start-Sleep -Seconds 1
$max = Snapshot
# Normal maximized Windows frames include an invisible resize border (usually 8 pixels).
Check ($null -ne $max.MaximizedBounds -and $max.MaximizedBounds.Bottom -le ($shown.Bounds.Top + 12)) 'normal maximized window respects AppBar'
$own = @(Get-Process PCStatusBar | Where-Object Path -EQ $exe)
Command '--show'
Check (@(Get-Process PCStatusBar | Where-Object Path -EQ $exe).Count -eq 1) 'single installed instance'
Command '--exit'
foreach ($p in $own) { Check ($p.WaitForExit(5000)) 'graceful exit' }
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Normal
Start-Sleep -Seconds 3
$restarted = Snapshot
Check ($restarted.registered -and $restarted.Bounds.Top -eq $shown.Bounds.Top) 'exit and restart leave no accumulated reservation'
$shell = New-Object -ComObject WScript.Shell
foreach ($folder in @([Environment]::GetFolderPath('Programs'),[Environment]::GetFolderPath('Startup'))) {
    $link = Join-Path $folder 'PCStatusBar.lnk'
    Check ((Test-Path -LiteralPath $link) -and $shell.CreateShortcut($link).TargetPath -eq $exe) "shortcut targets installed executable: $(Split-Path $folder -Leaf)"
}
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
Write-Output 'Installed smoke checks passed. Panel left running.'
