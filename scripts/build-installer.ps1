<#
.SYNOPSIS
    Publishes SessionLens and compiles the Inno Setup installer into dist\.

.EXAMPLE
    .\scripts\build-installer.ps1
    .\scripts\build-installer.ps1 -SkipPublish     # reuse the existing publish\ folder
#>
param(
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$repo = Split-Path $PSScriptRoot -Parent

# The installer's version and the exe's version must be the same release.
$csprojVersion = ([xml](Get-Content "$repo\src\SessionLens.csproj")).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
$issMatch = Select-String -Path "$repo\installer\SessionLens.iss" -Pattern '^#define\s+AppVersion\s+"([^"]+)"'
if (-not $csprojVersion) { throw "<Version> not found in src\SessionLens.csproj" }
if (-not $issMatch) { throw "AppVersion not found in installer\SessionLens.iss" }

$issVersion = $issMatch.Matches[0].Groups[1].Value
if ($csprojVersion -ne $issVersion) {
    throw "Version mismatch: csproj says $csprojVersion, installer says $issVersion. Make them match."
}
Write-Host "Version $issVersion (csproj and installer agree)." -ForegroundColor DarkGray

# Inno Setup 6 may be installed per-user rather than into Program Files.
$candidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)

$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "ISCC.exe not found. Install it with: winget install --id JRSoftware.InnoSetup"
}

if (-not $SkipPublish) {
    & "$PSScriptRoot\publish.ps1" | Out-Null
}

$publish = Join-Path $repo "publish"
if (-not (Test-Path (Join-Path $publish "SessionLens.exe"))) {
    throw "publish\SessionLens.exe is missing. Run .\scripts\publish.ps1 first."
}

$distDir = Join-Path $repo "dist"
New-Item -ItemType Directory -Force $distDir | Out-Null

Write-Host "Compiling installer with $iscc ..." -ForegroundColor Cyan

# Captured rather than streamed, so a failure shows the real compiler message.
$isccOutput = & $iscc "$repo\installer\SessionLens.iss" 2>&1
if ($LASTEXITCODE -ne 0) {
    $isccOutput | Select-Object -Last 25 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
    throw "ISCC failed with exit code $LASTEXITCODE (see output above)."
}

$setup = Get-ChildItem $distDir -Filter "SessionLens-Setup-*.exe" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

Write-Host ""
Write-Host "Installer ready." -ForegroundColor Green
Write-Host "  $($setup.FullName)"
Write-Host "  $([math]::Round($setup.Length / 1MB, 1)) MB"
