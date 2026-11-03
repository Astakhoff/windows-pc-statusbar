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
if($shown.TaskbarOverlay -and $shown.OverlayAdaptive){for($i=0;$i -lt 5 -and $shown.OverlayStatus -ne 'adaptive';$i++){Start-Sleep -Seconds 1;$shown=Snapshot}}
Check ($shown.Visible -and ($shown.TaskbarOverlay -or $shown.registered)) 'visible panel in selected placement mode'
Check (@($shown.Metrics | Where-Object { $null -ne $_.Percent }).Count -eq 4) 'CPU RAM and both real disk metrics available'
$diskBlocks=@($shown.Blocks | Where-Object AccessibleName -In @('C: свободно','Разделитель дисков','D: свободно'))
Check (($diskBlocks.AccessibleName -join '|') -eq 'C: свободно|D: свободно') 'C and D order without an extra separator'
Check ($null -ne $shown.CpuInsights.Queue -and $shown.CpuInsights.Queue -ge 0) 'real CPU queue counter available'
Check ($shown.CpuInsights.HasBaseline -and @($shown.CpuInsights.Groups).Count -gt 0) 'real CPU consumers measured over interval'
Check (@($shown.CpuHistory | Where-Object { $null -ne $_.Value }).Count -gt 0) 'CPU graph contains measured samples'
Check (@($shown.TickerTags).Count -eq 3 -and @($shown.TickerTags | Where-Object { $_.Provider -eq 'Codex' -and $_.Period -ne 'week' }).Count -eq 0) 'three tagged items with Codex short window hidden'
Check ($shown.TickerBounds.Width -gt 0 -and $shown.TickerBounds.Right -le ($shown.Bounds.Width - $shown.ContentWidth)) 'ticker stays to the left of system blocks'
$protected=$true
if($shown.TaskbarOverlay -and $shown.OverlayAdaptive){
    $ratio=$shown.ShellFrame.Dpi/[double]$shown.ShellMeasurement.Dpi
    $guard=[Math]::Max(4,[Math]::Round(10*$shown.ShellFrame.Dpi/96))
    $protected=$null -ne $shown.ShellMeasurement -and $shown.Bounds.Left -ge ($shown.ShellFrame.Bounds.Left+[Math]::Ceiling($shown.ShellMeasurement.OccupiedLeft*$ratio)+$guard) -and $shown.Bounds.Right -le ($shown.ShellFrame.Bounds.Right-[Math]::Ceiling($shown.ShellMeasurement.OccupiedRight*$ratio)-$guard)
}
Check $protected 'adaptive overlay respects measured application and tray regions'
Check (@($shown.Blocks|Where-Object {$_.Bounds.Right -gt $shown.ContentWidth -or $_.Bounds.Bottom -gt $shown.Bounds.Height}).Count -eq 0) 'visible blocks fit their current viewport'
if($shown.TaskbarOverlay){Check (!$shown.registered -and $shown.Bounds.Top -ge $shown.Taskbar.Top -and $shown.Bounds.Bottom -le $shown.Taskbar.Bottom) 'fixed overlay is inside taskbar without AppBar reservation'}
else{Check ($shown.Bounds.Bottom -le $shown.Taskbar.Top) 'panel above bottom taskbar'}
Command '--hide'
$hidden = Snapshot
Check (!$hidden.Visible -and !$hidden.registered) 'hide unregisters AppBar'
$workShown = @($shown.Monitors | Where-Object Primary)[0].WorkingArea
$workHidden = @($hidden.Monitors | Where-Object Primary)[0].WorkingArea
$expectedRelease=if($shown.TaskbarOverlay){0}else{$shown.Bounds.Height}
Check (($workHidden.Height - $workShown.Height) -eq $expectedRelease) 'hide respects selected reservation mode'
Command '--show'
$again = Snapshot
Check ($again.Bounds.Top -eq $shown.Bounds.Top) 'show does not accumulate reservation'
Command '--check-maximized'
Start-Sleep -Seconds 1
$max = Snapshot
# Normal maximized Windows frames include an invisible resize border (usually 8 pixels).
Check ($null -ne $max.MaximizedBounds -and $max.MaximizedBounds.Bottom -le ($workShown.Bottom + 12)) 'normal maximized window respects working area'
$own = @(Get-Process PCStatusBar | Where-Object Path -EQ $exe)
Command '--show'
Check (@(Get-Process PCStatusBar | Where-Object Path -EQ $exe).Count -eq 1) 'single installed instance'
Command '--exit'
foreach ($p in $own) { Check ($p.WaitForExit(5000)) 'graceful exit' }
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Normal
Start-Sleep -Seconds 3
$restarted = Snapshot
Check (($restarted.TaskbarOverlay -or $restarted.registered) -and $restarted.Bounds.Top -eq $shown.Bounds.Top) 'exit and restart preserve selected placement'
$shell = New-Object -ComObject WScript.Shell
foreach ($folder in @([Environment]::GetFolderPath('Programs'),[Environment]::GetFolderPath('Startup'))) {
    $link = Join-Path $folder 'PCStatusBar.lnk'
    Check ((Test-Path -LiteralPath $link) -and $shell.CreateShortcut($link).TargetPath -eq $exe) "shortcut targets installed executable: $(Split-Path $folder -Leaf)"
}
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
Write-Output 'Installed smoke checks passed. Panel left running.'
