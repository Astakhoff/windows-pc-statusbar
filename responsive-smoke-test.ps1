# Temporarily caps only PCStatusBar's viewport; real readings and Windows display settings are unchanged.
$ErrorActionPreference='Stop'
$exe=Join-Path $env:LOCALAPPDATA 'Programs\PCStatusBar\PCStatusBar.exe'
$file=Join-Path $env:LOCALAPPDATA 'PCStatusBar\diagnostics.local.json'
function Command([string[]]$arguments){
    $p=Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if(!$p.WaitForExit(5000)){throw 'PCStatusBar command timeout'}
}
function Snapshot{
    $before=if(Test-Path -LiteralPath $file){(Get-Item -LiteralPath $file).LastWriteTimeUtc}else{[datetime]::MinValue}
    Command @('--diagnostics')
    for($i=0;$i -lt 50;$i++){
        if((Test-Path -LiteralPath $file) -and (Get-Item -LiteralPath $file).LastWriteTimeUtc -gt $before){return Get-Content -LiteralPath $file -Raw|ConvertFrom-Json}
        Start-Sleep -Milliseconds 100
    }
    throw 'Diagnostics timeout'
}
function Check([bool]$condition,[string]$name){if(!$condition){throw "FAIL $name"};Write-Output "PASS $name"}
$initial=Snapshot
if(!$initial.TaskbarOverlay){throw 'Enable taskbar overlay before this test'}
$settings=Join-Path $env:LOCALAPPDATA 'PCStatusBar\settings.json'
$settingsHash=(Get-FileHash -LiteralPath $settings).Hash
try{
    foreach($width in @(1100,650,150)){
        Command @('--preview-width',"$width")
        $state=Snapshot
        Check ($state.Bounds.Width -le $width -and $state.ContentWidth -le $state.Bounds.Width) "viewport fits cap $width"
        Check (@($state.Blocks|Where-Object {$_.Bounds.Right -gt $state.ContentWidth -or $_.Bounds.Bottom -gt $state.Bounds.Height}).Count -eq 0) "blocks stay inside cap $width"
        if($width -eq 650){Check ($state.CompactLayout -and @($state.HiddenBlocks).Count -gt 0) 'compression hides lower-priority blocks'}
        if($width -eq 150){Check (@($state.Blocks|Where-Object AccessibleName -EQ 'Все показатели').Count -eq 1) 'overflow remains reachable in tiny viewport'}
    }
}finally{Command @('--preview-width',"$($initial.PreviewWidth)")}
$restored=Snapshot
Check ($restored.PreviewWidth -eq $initial.PreviewWidth -and $restored.Bounds.Width -gt 150) 'normal adaptive viewport restored'
Check ((Get-FileHash -LiteralPath $settings).Hash -eq $settingsHash) 'preview does not change saved settings'
