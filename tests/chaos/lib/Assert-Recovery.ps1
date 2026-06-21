# Assert recovery within SLO from recovery_profiles.json
param(
    [Parameter(Mandatory = $true)][string]$ScenarioId,
    [Parameter(Mandatory = $true)][int]$RecoveryMs,
    [string]$ProfilesPath = '',
    [hashtable]$ObserveResult = @{}
)

$ErrorActionPreference = 'Stop'
if (-not $ProfilesPath) {
    $ProfilesPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'expected/recovery_profiles.json'
}
$profiles = Get-Content $ProfilesPath -Raw | ConvertFrom-Json
$def = $profiles.scenarios.$ScenarioId
if (-not $def) {
    return @{ pass = $false; reason = "unknown scenario $ScenarioId" }
}

$maxMs = $def.maxRecoveryMs
if ($null -eq $maxMs -and $def.autoRecover -eq $false) {
    return @{ pass = $true; reason = 'fatal scenario — no recovery expected'; recoveryMs = $RecoveryMs; failureClass = $def.failureClass }
}

$withinSlo = ($maxMs -eq 0) -or ($RecoveryMs -le $maxMs)
return @{
    pass = $withinSlo
    scenarioId = $ScenarioId
    recoveryMs = $RecoveryMs
    maxRecoveryMs = $maxMs
    failureClass = $def.failureClass
    reason = if ($withinSlo) { 'within SLO' } else { "exceeded SLO ($RecoveryMs > $maxMs ms)" }
    observe = $ObserveResult
}
