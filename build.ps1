param([ValidateSet('win-x64','win-arm64')][string]$Runtime = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {'win-arm64'} else {'win-x64'}))
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet run --project tests/PCStatusBar.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    dotnet publish src/PCStatusBar/PCStatusBar.csproj -c Release -r $Runtime --self-contained true -o artifacts/publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
} finally { Pop-Location }
