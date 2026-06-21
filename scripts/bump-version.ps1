# Unified version bump — updates version.json and all dependent project files.
# Usage: .\scripts\bump-version.ps1 -MasselGuardVersion 3.7.0 -Codename "New Codename" [-RouteGuardVersion 0.2.0] [-Channel beta]
param(
    [Parameter(Mandatory = $true)]
    [string]$MasselGuardVersion,
    [Parameter(Mandatory = $true)]
    [string]$Codename,
    [string]$RouteGuardVersion = "",
    [ValidateSet("dev", "nightly", "beta", "stable")]
    [string]$Channel = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$VersionFile = Join-Path $Root "version.json"

if (-not (Test-Path $VersionFile)) {
    throw "version.json not found at $VersionFile"
}

$doc = Get-Content $VersionFile -Raw | ConvertFrom-Json
$doc.masselguard.version = $MasselGuardVersion
$doc.masselguard.codename = $Codename
if ($RouteGuardVersion) { $doc.routeguard.version = $RouteGuardVersion }
if ($Channel) { $doc.releaseChannel = $Channel }
$doc | ConvertTo-Json -Depth 4 | Set-Content $VersionFile -Encoding UTF8

& (Join-Path $Root "scripts\sync-version.ps1")

Write-Host "Bumped MasselGUARD to $MasselGuardVersion ($Codename), RouteGuard $($doc.routeguard.version), channel $($doc.releaseChannel)"
