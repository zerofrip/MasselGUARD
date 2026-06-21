# Generate release manifest.json for unified updater.
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("dev", "nightly", "beta", "stable")]
    [string]$Channel,
    [string]$DistDir = "",
    [string]$RouteGuardDir = "",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

if (-not $DistDir) { $DistDir = Join-Path $Root "dist" }
if (-not $RouteGuardDir) { $RouteGuardDir = Join-Path (Split-Path $Root -Parent) "RouteGuard\target\release" }

$version = (& (Join-Path $Root "scripts\read-version.ps1") -Property masselguard.version)
$rgVersion = (& (Join-Path $Root "scripts\read-version.ps1") -Property routeguard.version)

if (-not $OutputPath) {
    $outDir = Join-Path $Root "dist\release"
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    $OutputPath = Join-Path $outDir "manifest-$Channel.json"
}

function Get-Sha256($path) {
    if (-not (Test-Path $path)) { return $null }
    return (Get-FileHash -Path $path -Algorithm SHA256).Hash
}

function New-Component($id, $ver, $relPath, $baseDir, $service = $null) {
    $full = Join-Path $baseDir $relPath
    @{
        id = $id
        version = $ver
        path = $relPath.Replace('\', '/')
        sha256 = Get-Sha256 $full
        url = $null
        service = $service
        stopBeforeUpdate = ($null -ne $service)
    }
}

$components = @(
    (New-Component "masselguard-gui" $version "MasselGUARD.exe" $DistDir)
    (New-Component "masselguard-agent" $version "MasselGUARDAgent.exe" $DistDir "MasselGUARDAgent")
    (New-Component "masselguard-cli" $version "MasselGUARDcli.exe" $DistDir)
    (New-Component "routeguard-service" $rgVersion "RouteGuard/routeguard-service.exe" $DistDir "RouteGuard")
    (New-Component "routeguard-cli" $rgVersion "RouteGuard/routeguard-cli.exe" $DistDir)
    (New-Component "wireguard-dll" $rgVersion "RouteGuard/wireguard.dll" $DistDir)
)

$manifest = @{
    schemaVersion = 1
    channel = $Channel
    productVersion = $version
    releaseDate = (Get-Date).ToUniversalTime().ToString("o")
    mandatory = $false
    minSupportedVersion = "3.6.0"
    components = $components
    manifestSignature = $null
}

$manifest | ConvertTo-Json -Depth 6 | Set-Content $OutputPath -Encoding UTF8
Write-Host "Wrote $OutputPath"
