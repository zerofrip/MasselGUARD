# Production Readiness Score (PRS) calculator — Phase 14
param(
    [ValidateSet('beta','stable','enterprise')]
    [string]$Channel = 'beta',
    [string]$TelemetrySummaryPath = '',
    [string]$SoakReportPath = '',
    [string]$ChaosReportPath = '',
    [string]$UpdateHistoryPath = '',
    [string]$ReportPath = 'reports/prs'
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $ReportPath -Force | Out-Null

function Normalize-CrashFree {
    param([double]$Rate)
    if ($Rate -ge 98) { return 100 }
    if ($Rate -le 0) { return 0 }
    return [math]::Round($Rate / 98 * 100, 1)
}

function Normalize-Reconnect {
    param([double]$Rate)
    if ($Rate -ge 95) { return 100 }
    return [math]::Round($Rate / 95 * 100, 1)
}

function Normalize-Update {
    param([double]$Rate, [string]$Ch)
    $target = if ($Ch -eq 'stable') { 99 } else { 95 }
    if ($Rate -ge $target) { return 100 }
    return [math]::Round($Rate / $target * 100, 1)
}

function Load-JsonFile {
    param([string]$Path)
    if (-not $Path -or -not (Test-Path $Path)) { return $null }
    Get-Content $Path -Raw | ConvertFrom-Json
}

# Defaults when artifacts missing (local dev)
$telemetry = Load-JsonFile $TelemetrySummaryPath
$soak = Load-JsonFile $SoakReportPath
$chaos = Load-JsonFile $ChaosReportPath

$crashFreeRate = [double]($telemetry.crashFreeSessionRate ?? 100)
$updateRate = [double]($telemetry.updateHistory.successRate ?? 100)
$reconnectRate = [double]($soak.reconnectSuccessRate ?? 100)
$driverPass = if ($chaos) {
    $c07 = $chaos.results | Where-Object { $_.scenarioId -eq 'C07' } | Select-Object -First 1
    if ($c07) { [double](if ($c07.pass) { 100 } else { 0 }) } else { 100 }
} else { 100 }
$transportRate = [double]($soak.transportRecoverySuccessRate ?? 100)
$telemetryHealth = if ($telemetry.available -eq $false) { 100 } else {
    $violations = $telemetry.forbiddenKeyViolations ?? 0
    if ($violations -gt 0) { 0 } else { 100 }
}

$subScores = @{
    CrashFree = Normalize-CrashFree $crashFreeRate
    Reconnect = Normalize-Reconnect $reconnectRate
    Update = Normalize-Update $updateRate $Channel
    Driver = $driverPass
    Transport = if ($transportRate -ge 90) { 100 } else { [math]::Round($transportRate / 90 * 100, 1) }
    TelemetryHealth = $telemetryHealth
}

$weights = @{
    CrashFree = 0.25
    Reconnect = 0.20
    Update = 0.15
    Driver = 0.15
    Transport = 0.15
    TelemetryHealth = 0.10
}

$prs = 0.0
foreach ($k in $subScores.Keys) {
    $prs += $weights[$k] * $subScores[$k]
}
$prs = [math]::Round($prs, 1)

$thresholds = @{
    beta = 85
    stable = 92
    enterprise = 95
}
$min = $thresholds[$Channel]
$gatePass = $prs -ge $min

$report = @{
    channel = $Channel
    computedAt = (Get-Date).ToUniversalTime().ToString('o')
    prs = $prs
    gateMinimum = $min
    gatePass = $gatePass
    subScores = $subScores
    weights = $weights
    inputs = @{
        telemetrySummary = $TelemetrySummaryPath
        soakReport = $SoakReportPath
        chaosReport = $ChaosReportPath
        updateHistory = $UpdateHistoryPath
    }
}

$outFile = Join-Path $ReportPath "prs-$Channel.json"
$report | ConvertTo-Json -Depth 6 | Set-Content -Path $outFile -Encoding UTF8
Write-Host "PRS ($Channel): $prs / $min — $(if ($gatePass) { 'PASS' } else { 'FAIL' })" -ForegroundColor $(if ($gatePass) { 'Green' } else { 'Red' })
Write-Host "Report: $outFile"
if (-not $gatePass) { exit 1 }
exit 0
