param([switch]$SkipBuild, [switch]$NoStart, [switch]$EnableStartup)
$ErrorActionPreference = 'Stop'
if (!$SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$source = Join-Path $PSScriptRoot 'artifacts\publish'
if (!(Test-Path -LiteralPath (Join-Path $source 'PCStatusBar.exe'))) { throw 'Build is missing' }
$destination = Join-Path $env:LOCALAPPDATA 'Programs\PCStatusBar'
$exe = Join-Path $destination 'PCStatusBar.exe'
$startupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'PCStatusBar.lnk'
$keepStartup = !(Test-Path -LiteralPath (Join-Path $destination 'installed-files.json')) -or (Test-Path -LiteralPath $startupLink)
$taskName = 'PCStatusBar-' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($existingTask) { $keepStartup = $existingTask.Settings.Enabled }
if ($EnableStartup) { $keepStartup = $true }
# Only stop the process from our exact installation directory.
$ownProcesses = @(Get-Process PCStatusBar -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
if ($ownProcesses.Count) {
    Start-Process -FilePath $exe -ArgumentList '--exit' -WindowStyle Hidden -Wait
    foreach ($process in $ownProcesses) {
        if (!$process.WaitForExit(8000)) { throw 'PCStatusBar did not exit; close it from the tray and retry.' }
    }
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$previousManifest = Join-Path $destination 'installed-files.json'
if (Test-Path -LiteralPath $previousManifest) {
    foreach ($name in (Get-Content -LiteralPath $previousManifest -Raw | ConvertFrom-Json)) {
        if ([IO.Path]::GetFileName($name) -ne $name) { throw 'Invalid install manifest' }
        $old = Join-Path $destination $name
        if (Test-Path -LiteralPath $old -PathType Leaf) { Remove-Item -LiteralPath $old }
    }
}
Get-ChildItem -LiteralPath $source -File | Copy-Item -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $destination -Force
$files = @((Get-ChildItem -LiteralPath $source -File).Name) + 'uninstall.ps1'
ConvertTo-Json -InputObject $files | Set-Content -LiteralPath $previousManifest -Encoding utf8
$shell = New-Object -ComObject WScript.Shell
$shortcutFolders = @([Environment]::GetFolderPath('Programs'))
foreach ($folder in $shortcutFolders) {
    $shortcut = $shell.CreateShortcut((Join-Path $folder 'PCStatusBar.lnk'))
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $destination
    $shortcut.Description = 'CPU, RAM и свободное место C:'
    $shortcut.Save()
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
}
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
Start-Process -FilePath $exe -ArgumentList '--register-shortcuts' -WindowStyle Hidden -Wait
$startupArgument = if ($keepStartup) { '--enable-startup' } else { '--disable-startup' }
$startupProcess = Start-Process -FilePath $exe -ArgumentList $startupArgument -WindowStyle Hidden -PassThru -Wait
if ($startupProcess.ExitCode -ne 0) { throw 'Could not configure PCStatusBar startup; see its events.log' }
if (!$NoStart) { Start-Process -FilePath $exe -WorkingDirectory $destination -WindowStyle Normal }
Write-Output "Installed: $exe"
