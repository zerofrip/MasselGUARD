# Reads version.json and propagates versions to all build artifacts.
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$VersionFile = Join-Path $Root "version.json"
$doc = Get-Content $VersionFile -Raw | ConvertFrom-Json

$mgVer = $doc.masselguard.version
$rgVer = $doc.routeguard.version
$codename = $doc.masselguard.codename
$channel = $doc.releaseChannel

function Set-XmlVersion($path, $version) {
    if (-not (Test-Path $path)) { return }
    [xml]$xml = Get-Content $path
    $xml.Project.PropertyGroup.Version = $version
    $xml.Project.PropertyGroup.AssemblyVersion = $version
    $xml.Project.PropertyGroup.FileVersion = $version
    $xml.Save($path)
}

# .NET csproj files
@(
    "MasselGUARD.csproj",
    "MasselGUARDAgent\MasselGUARDAgent.csproj",
    "MasselGUARDcli\MasselGUARDcli.csproj"
) | ForEach-Object { Set-XmlVersion (Join-Path $Root $_) $mgVer }

# Tauri + npm
$tauriConf = Join-Path $Root "masselguard-ui\src-tauri\tauri.conf.json"
if (Test-Path $tauriConf) {
    $tc = Get-Content $tauriConf -Raw | ConvertFrom-Json
    $tc.version = $mgVer
    $tc | ConvertTo-Json -Depth 10 | Set-Content $tauriConf -Encoding UTF8
}

$pkgJson = Join-Path $Root "masselguard-ui\package.json"
if (Test-Path $pkgJson) {
    $pkg = Get-Content $pkgJson -Raw | ConvertFrom-Json
    $pkg.version = $mgVer
    $pkg | ConvertTo-Json -Depth 10 | Set-Content $pkgJson -Encoding UTF8
}

$cargoToml = Join-Path $Root "masselguard-ui\src-tauri\Cargo.toml"
if (Test-Path $cargoToml) {
    (Get-Content $cargoToml -Raw) -replace '(?m)^version = ".*"$', "version = `"$mgVer`"" | Set-Content $cargoToml -Encoding UTF8 -NoNewline
}

# UpdateChecker.cs — CurrentVersion + codename dictionary entry
$updateChecker = Join-Path $Root "UpdateChecker.cs"
if (Test-Path $updateChecker) {
    $uc = Get-Content $updateChecker -Raw
    $uc = $uc -replace 'private const string CurrentVersion = "[^"]+"', "private const string CurrentVersion = `"$mgVer`""
    if ($uc -notmatch "\{ `"$([regex]::Escape($mgVer))`"") {
        $uc = $uc -replace '(\{ "3\.6\.0", "Dangerous Donkey"\s*\})', "{ `"$mgVer`", `"$codename`" }`n                `$1"
    }
    Set-Content $updateChecker $uc -Encoding UTF8
}

# BUILD.bat VERSION/CODENAME lines
$buildBat = Join-Path $Root "BUILD.bat"
if (Test-Path $buildBat) {
    $bat = Get-Content $buildBat -Raw
    $bat = $bat -replace 'set VERSION=[^\r\n]+', "set VERSION=$mgVer"
    $bat = $bat -replace 'set CODENAME=[^\r\n]+', "set CODENAME=$codename"
    Set-Content $buildBat $bat -Encoding UTF8
}

# WiX include
$wxi = Join-Path $Root "installer\Version.wxi"
if (Test-Path (Split-Path $wxi)) {
    @"
<?xml version="1.0" encoding="UTF-8"?>
<Include>
  <?define ProductVersion = "$mgVer" ?>
  <?define RouteGuardVersion = "$rgVer" ?>
  <?define ProductCodename = "$codename" ?>
  <?define ReleaseChannel = "$channel" ?>
  <?define UpgradeCode = "A1B2C3D4-E5F6-7890-ABCD-EF1234567890" ?>
</Include>
"@ | Set-Content $wxi -Encoding UTF8
}

# RouteGuard workspace (sibling repo)
$rgRoot = Join-Path (Split-Path $Root -Parent) "RouteGuard"
$rgCargo = Join-Path $rgRoot "Cargo.toml"
if (Test-Path $rgCargo) {
    (Get-Content $rgCargo -Raw) -replace '(?m)^version = "0\.[0-9]+\.[0-9]+"$', "version = `"$rgVer`"" | Set-Content $rgCargo -Encoding UTF8 -NoNewline
}

Write-Host "Synced versions: MasselGUARD=$mgVer RouteGuard=$rgVer channel=$channel"
