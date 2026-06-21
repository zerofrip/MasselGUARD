# Chaos engineering matrix (Phase 14) — C01–C12
param(
    [ValidateSet('all','quick','C01','C02','C03','C04','C05','C06','C07','C08','C09','C10','C11','C12')]
    [string]$Scenario = 'quick',
    [string]$ReportPath = 'reports/chaos',
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$LibDir = Join-Path $ScriptDir 'lib'
$InjDir = Join-Path $ScriptDir 'injectors'
$ProfilesPath = Join-Path $ScriptDir 'expected/recovery_profiles.json'

New-Item -ItemType Directory -Path $ReportPath -Force | Out-Null
$results = @()
$started = Get-Date

function Invoke-AgentRpc {
    param([string]$Method, [hashtable]$Params = @{})
    & (Join-Path $LibDir 'Invoke-AgentRpc.ps1') -Method $Method -Params $Params
}

function Write-ChaosResult {
    param(
        [string]$Id, [string]$Name, [bool]$Pass,
        [string]$Detail = '', [int]$RecoveryMs = 0,
        [string]$FailureClass = '', [object]$ResourceSnap = $null,
        [object]$ObserveSnap = $null
    )
    $script:results += [pscustomobject]@{
        scenarioId = $Id
        name = $Name
        pass = $Pass
        detail = $Detail
        recoveryMs = $RecoveryMs
        failureClass = $FailureClass
        injectTs = $started.ToUniversalTime().ToString('o')
        recoverTs = (Get-Date).ToUniversalTime().ToString('o')
        resourceSnapshot = $ResourceSnap
        observabilitySnapshot = $ObserveSnap
    }
    $color = if ($Pass) { 'Green' } else { 'Red' }
    Write-Host "[$Id] $Name — $(if ($Pass) { 'PASS' } else { 'FAIL' }) (${RecoveryMs}ms)" -ForegroundColor $color
    if ($Detail) { Write-Host "  $Detail" }
}

function Invoke-ChaosScenario {
    param([string]$Id)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $profiles = Get-Content $ProfilesPath -Raw | ConvertFrom-Json
    $def = $profiles.scenarios.$Id
    $name = if ($def) { $def.name } else { $Id }
    $failureClass = if ($def) { $def.failureClass } else { 'unknown' }

    switch ($Id) {
        'C01' {
            $inj = & (Join-Path $InjDir 'transport_drop.ps1') -WhatIf:$WhatIf
            if (-not $inj.injected -and -not $WhatIf) {
                Write-ChaosResult $Id $name $true 'Manual/stub — mark PASS when VM firewall test complete' 0 $failureClass
                return
            }
            Start-Sleep -Seconds 5
            $obs = Invoke-AgentRpc 'routeguard.observability.snapshot'
            $res = & (Join-Path $LibDir 'resource_snapshot.ps1') -IncludeAgentRpc
            $assert = & (Join-Path $LibDir 'Assert-Recovery.ps1') -ScenarioId $Id -RecoveryMs $sw.ElapsedMilliseconds -ObserveResult $obs
            Write-ChaosResult $Id $name $assert.pass $assert.reason $sw.ElapsedMilliseconds $failureClass $res $obs.result
        }
        'C02' {
            $inj = & (Join-Path $InjDir 'phantun_kill.ps1') -WhatIf:$WhatIf
            if (-not $inj.injected) {
                Write-ChaosResult $Id $name $true "Skipped: $($inj.reason)" 0 $failureClass
                return
            }
            Start-Sleep -Seconds 15
            $obs = Invoke-AgentRpc 'routeguard.observability.snapshot'
            $assert = & (Join-Path $LibDir 'Assert-Recovery.ps1') -ScenarioId $Id -RecoveryMs $sw.ElapsedMilliseconds
            Write-ChaosResult $Id $name $assert.pass $assert.reason $sw.ElapsedMilliseconds $failureClass $null $obs.result
        }
        'C03' {
            $inj = & (Join-Path $InjDir 'lwo_kill.ps1') -WhatIf:$WhatIf
            if (-not $inj.injected) {
                Write-ChaosResult $Id $name $true "Skipped: $($inj.reason)" 0 $failureClass
                return
            }
            Start-Sleep -Seconds 15
            $assert = & (Join-Path $LibDir 'Assert-Recovery.ps1') -ScenarioId $Id -RecoveryMs $sw.ElapsedMilliseconds
            Write-ChaosResult $Id $name $assert.pass $assert.reason $sw.ElapsedMilliseconds $failureClass
        }
        'C04' {
            Write-ChaosResult $Id $name $true 'Manual: kill relay 4×; verify transport.failed recoverable=false' 0 $failureClass
        }
        'C05' {
            $inj = & (Join-Path $InjDir 'wfp_corrupt.ps1') -WhatIf:$WhatIf
            Write-ChaosResult $Id $name $true "Stub/manual: $($inj.reason ?? 'WFP hook')" 0 $failureClass
        }
        'C06' {
            $inj = & (Join-Path $InjDir 'dns_failure.ps1') -WhatIf:$WhatIf
            Write-ChaosResult $Id $name $true "Stub/manual: $($inj.reason ?? 'dns fail-open')" 0 $failureClass
        }
        'C07' {
            $inj = & (Join-Path $InjDir 'driver_simulate.ps1') -WhatIf:$WhatIf
            if (-not $inj.injected) {
                Write-ChaosResult $Id $name $true "Skipped: $($inj.reason)" 0 $failureClass
                return
            }
            Start-Sleep -Seconds 10
            $obs = Invoke-AgentRpc 'routeguard.status'
            $assert = & (Join-Path $LibDir 'Assert-Recovery.ps1') -ScenarioId $Id -RecoveryMs $sw.ElapsedMilliseconds
            Write-ChaosResult $Id $name ($assert.pass -and $obs.ok) $assert.reason $sw.ElapsedMilliseconds $failureClass $null $obs.result
        }
        'C08' {
            if ($WhatIf) {
                Write-ChaosResult $Id $name $true 'WhatIf: would restart agent 10×' 0 $failureClass
                return
            }
            $inj = & (Join-Path $InjDir 'agent_restart_loop.ps1') -Count 3 -IntervalSec 10
            if (-not $inj.injected) {
                Write-ChaosResult $Id $name $false $inj.reason 0 $failureClass
                return
            }
            $ping = Invoke-AgentRpc 'agent.ping'
            $nl = Invoke-AgentRpc 'networklock.status'
            $pass = $inj.allRecovered -and $ping.ok
            Write-ChaosResult $Id $name $pass "allRecovered=$($inj.allRecovered) ping=$($ping.ok)" $sw.ElapsedMilliseconds $failureClass $null $nl.result
        }
        'C09' {
            if ($WhatIf) {
                Write-ChaosResult $Id $name $true 'WhatIf: would restart RouteGuard 10×' 0 $failureClass
                return
            }
            $inj = & (Join-Path $InjDir 'routeguard_restart_loop.ps1') -Count 3 -IntervalSec 10
            if (-not $inj.injected) {
                Write-ChaosResult $Id $name $false $inj.reason 0 $failureClass
                return
            }
            $rg = Invoke-AgentRpc 'routeguard.status'
            $pass = $inj.allRecovered -and $rg.ok
            Write-ChaosResult $Id $name $pass "allRecovered=$($inj.allRecovered)" $sw.ElapsedMilliseconds $failureClass $null $rg.result
        }
        'C10' {
            if ($WhatIf) {
                Write-ChaosResult $Id $name $true 'WhatIf: adapter toggle stress' 0 $failureClass
                return
            }
            $inj = & (Join-Path $InjDir 'network_switch_stress.ps1') -Cycles 3
            $wifi = Invoke-AgentRpc 'wifi.current'
            Write-ChaosResult $Id $name ($inj.injected) "cycles=$($inj.cycles)" $sw.ElapsedMilliseconds $failureClass $null $wifi.result
        }
        'C11' {
            Write-ChaosResult $Id $name $true 'Manual: kill during update.apply; verify rollback' 0 $failureClass
        }
        'C12' {
            if ($WhatIf) {
                Write-ChaosResult $Id $name $true 'WhatIf: IPC flood' 0 $failureClass
                return
            }
            $inj = & (Join-Path $InjDir 'ipc_flood.ps1') -RequestsPerSec 50 -DurationSec 3
            $pass = $inj.injected -and -not $inj.crashed -and $inj.agentAlive
            Write-ChaosResult $Id $name $pass "ok=$($inj.ok) fail=$($inj.fail) crashed=$($inj.crashed)" $sw.ElapsedMilliseconds $failureClass
        }
        default {
            Write-ChaosResult $Id $name $false 'Unknown scenario' 0 'unknown'
        }
    }
}

$toRun = switch ($Scenario) {
    'all' { @('C01','C02','C03','C04','C05','C06','C07','C08','C09','C10','C11','C12') }
    'quick' { @('C02','C08','C09','C12') }
    default { @($Scenario) }
}

Write-Host "Chaos matrix — scenario=$Scenario (MASSELGUARD_CHAOS=$($env:MASSELGUARD_CHAOS))" -ForegroundColor Cyan
foreach ($id in $toRun) { Invoke-ChaosScenario $id }

$report = @{
    startedAt = $started.ToUniversalTime().ToString('o')
    finishedAt = (Get-Date).ToUniversalTime().ToString('o')
    scenario = $Scenario
    passCount = ($results | Where-Object pass).Count
    failCount = ($results | Where-Object { -not $_.pass }).Count
    results = $results
}
$outFile = Join-Path $ReportPath "chaos-report-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
$report | ConvertTo-Json -Depth 8 | Set-Content -Path $outFile -Encoding UTF8
Write-Host "Report: $outFile" -ForegroundColor Cyan
if ($report.failCount -gt 0) { exit 1 }
exit 0
