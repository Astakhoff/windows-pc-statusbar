param([switch]$RemoveSettings)
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\PCStatusBar'))
if ([IO.Path]::GetFileName($destination) -ne 'PCStatusBar') { throw 'Invalid installation directory' }
$exe = Join-Path $destination 'PCStatusBar.exe'
if (Test-Path -LiteralPath $exe) {
    $startupProcess = Start-Process -FilePath $exe -ArgumentList '--disable-startup' -WindowStyle Hidden -PassThru -Wait
    if ($startupProcess.ExitCode -ne 0) { throw 'Could not remove PCStatusBar startup task' }
}
$ownProcesses = @(Get-Process PCStatusBar -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
if ($ownProcesses.Count) {
    Start-Process -FilePath $exe -ArgumentList '--exit' -WindowStyle Hidden -Wait
    foreach ($process in $ownProcesses) { if (!$process.WaitForExit(8000)) { throw 'Close PCStatusBar from the tray and retry' } }
}
foreach ($folder in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Startup'))) {
    $link = Join-Path $folder 'PCStatusBar.lnk'
    if (Test-Path -LiteralPath $link) {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($link)
        if ($shortcut.TargetPath -eq $exe) { Remove-Item -LiteralPath $link }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }
}
$manifest = Join-Path $destination 'installed-files.json'
if (Test-Path -LiteralPath $manifest) {
    $files = @(Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json)
    foreach ($name in $files) {
        if ([IO.Path]::GetFileName($name) -ne $name) { throw 'Invalid manifest' }
        $file = Join-Path $destination $name
        if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file }
    }
    Remove-Item -LiteralPath $manifest
}
if ((Test-Path -LiteralPath $destination) -and !(Get-ChildItem -LiteralPath $destination -Force)) { Remove-Item -LiteralPath $destination }
if ($RemoveSettings) {
    $settings = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'PCStatusBar'))
    if ($settings -eq [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'PCStatusBar'))) {
        foreach ($name in @('settings.json','settings.json.tmp','events.log','events.log.old','diagnostics.local.json','taskbar.local.json')) {
            $file = Join-Path $settings $name
            if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file }
        }
        if ((Test-Path -LiteralPath $settings) -and !(Get-ChildItem -LiteralPath $settings -Force)) { Remove-Item -LiteralPath $settings }
    }
}
Write-Output 'PCStatusBar removed. Settings are preserved unless -RemoveSettings was specified.'
