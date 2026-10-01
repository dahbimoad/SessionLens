<#
.SYNOPSIS
    Builds the self-contained win-x64 release of Session Recorder into publish\.

.EXAMPLE
    .\scripts\publish.ps1
    .\scripts\publish.ps1 -Output "D:\Tools\SessionRecorder"
#>
param(
    [string]$Output
)

$ErrorActionPreference = "Stop"

$repo = Split-Path $PSScriptRoot -Parent
if (-not $Output) { $Output = Join-Path $repo "publish" }

$project = Join-Path $repo "src\SessionRecorder.csproj"

Write-Host "Publishing Session Recorder (self-contained, win-x64)..." -ForegroundColor Cyan

# Self-contained so the target machine needs no .NET runtime installed.
# Not single-file: the Playwright driver (.playwright\node) must sit next to the exe.
dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishReadyToRun=true `
    -p:DebugType=none `
    --output $Output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$exe = Join-Path $Output "SessionRecorder.exe"
if (-not (Test-Path $exe)) {
    throw "Publish finished but $exe is missing."
}

# Without the driver the app starts but every Record fails, so catch it here.
$driver = Join-Path $Output ".playwright"
if (-not (Test-Path $driver)) {
    throw "Publish finished but the Playwright driver folder $driver is missing."
}

$size = [math]::Round(((Get-ChildItem $Output -Recurse -Force | Measure-Object -Property Length -Sum).Sum / 1MB), 1)

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Executable : $exe"
Write-Host "  Folder size: $size MB"
