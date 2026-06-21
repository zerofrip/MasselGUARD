# Build MasselGUARD MSI + Burn bootstrapper (Windows + WiX 4 required).
param(
    [string]$DistDir = "",
    [string]$RouteGuardDir = "",
    [string]$OutputDir = "",
    [string]$Channel = "beta"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $Root

if (-not $DistDir) { $DistDir = Join-Path $RepoRoot "dist" }
if (-not $RouteGuardDir) { $RouteGuardDir = Join-Path (Split-Path $RepoRoot -Parent) "RouteGuard\target\release" }
if (-not $OutputDir) { $OutputDir = Join-Path $RepoRoot "dist\installer" }

& (Join-Path $RepoRoot "scripts\sync-version.ps1")

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$version = (& (Join-Path $RepoRoot "scripts\read-version.ps1") -Property masselguard.version)
$installerSource = $Root

# Cache .NET 10 Desktop Runtime for Burn bootstrapper
$dotnetExe = Join-Path $OutputDir "dotnet-desktop-runtime-10-win-x64.exe"
if (-not (Test-Path $dotnetExe)) {
    Write-Host "Fetching .NET 10 Desktop Runtime installer..."
    $dotnetUrl = "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.0/windowsdesktop-runtime-10.0.0-win-x64.exe"
    try {
        Invoke-WebRequest -Uri $dotnetUrl -OutFile $dotnetExe -UseBasicParsing
    } catch {
        Write-Warning "Could not download .NET 10 runtime ($dotnetUrl). Burn bundle may fail."
        Write-Warning $_.Exception.Message
    }
}

Write-Host "Building MSI v$version channel=$Channel"
Write-Host "  DistDir=$DistDir"
Write-Host "  RouteGuardDir=$RouteGuardDir"

# Requires WiX 4 CLI: wix build
$wxs = @(
    (Join-Path $Root "Product.wxs"),
    (Join-Path $Root "Components.wxs"),
    (Join-Path $Root "Driver.wxs")
)

$msiOut = Join-Path $OutputDir "MasselGUARD-$version-x64.msi"
wix build $wxs `
    -ext WixToolset.Util.wixext `
    -d DistDir=$DistDir `
    -d RouteGuardDir=$RouteGuardDir `
    -d OutputDir=$OutputDir `
    -d PrereqDir=$OutputDir `
    -d InstallerSourceDir=$installerSource `
    -o $msiOut

$bundleOut = Join-Path $OutputDir "MasselGUARD-$version-$Channel-x64-setup.exe"
wix build (Join-Path $Root "Bundle.wxs") `
    -ext WixToolset.Bal.wixext `
    -d OutputDir=$OutputDir `
    -d PrereqDir=$OutputDir `
    -o $bundleOut

Write-Host "Built:"
Write-Host "  $msiOut"
Write-Host "  $bundleOut"
