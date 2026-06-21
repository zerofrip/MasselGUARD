param(
    [Parameter(Mandatory = $true)]
    [string]$InfPath
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path $InfPath)) {
    Write-Warning "Driver INF not found: $InfPath — skipping"
    exit 0
}

Write-Host "Installing callout driver: $InfPath"
$result = & pnputil.exe /add-driver $InfPath /install 2>&1
Write-Host $result

# Record OEM name for uninstall
$oem = ($result | Select-String -Pattern "Published name\s*:\s*(oem\d+\.inf)" | ForEach-Object { $_.Matches.Groups[1].Value })
if ($oem) {
    $regPath = "HKLM:\SOFTWARE\RouteGuard\Driver"
    New-Item -Path $regPath -Force | Out-Null
    Set-ItemProperty -Path $regPath -Name "OemInf" -Value $oem
    Set-ItemProperty -Path $regPath -Name "InstalledAt" -Value (Get-Date).ToUniversalTime().ToString("o")
}

# Phase 13: installer outcome for telemetry ingest
$statusDir = Join-Path $env:ProgramData "MasselGUARD\installer"
New-Item -ItemType Directory -Path $statusDir -Force | Out-Null
$driverOk = $LASTEXITCODE -eq 0 -or $null -ne $oem
@{
    schemaVersion = 1
    scenario = "driver.install"
    result = if ($driverOk) { "ok" } else { "fail" }
    ts = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json | Set-Content (Join-Path $statusDir "last-install.json") -Encoding UTF8

# Start driver service if present
sc.exe query RouteGuardCallout 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
    sc.exe start RouteGuardCallout | Out-Null
}

exit 0
