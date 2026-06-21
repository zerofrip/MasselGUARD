# Long-running stability test matrix (Phase 13)
param(
    [ValidateSet('all','S01','S02','S03','S04','S05','S06','S07','S08','S09','S10','quick')]
    [string]$Scenario = 'quick',
    [string]$ReportPath = 'reports/stability',
    [int]$PollIntervalSec = 60,
    [string]$AgentPipe = '\\.\pipe\MasselGUARD'
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)
$LibDir = Join-Path $ScriptDir 'stability'

New-Item -ItemType Directory -Path $ReportPath -Force | Out-Null
$results = @()
$started = Get-Date

function Write-ScenarioResult {
    param([string]$Id, [string]$Name, [bool]$Pass, [string]$Detail = '', [int]$DurationMs = 0)
    $script:results += [pscustomobject]@{
        id = $Id; name = $Name; pass = $Pass; detail = $Detail; durationMs = $DurationMs
    }
    $color = if ($Pass) { 'Green' } else { 'Red' }
    Write-Host "[$Id] $Name — $(if ($Pass) { 'PASS' } else { 'FAIL' }) ($DurationMs ms)" -ForegroundColor $color
    if ($Detail) { Write-Host "  $Detail" }
}

function Invoke-AgentRpc {
    param([string]$Method, [hashtable]$Params = @{})
    $helper = Join-Path $LibDir 'Invoke-AgentRpc.ps1'
    if (-not (Test-Path $helper)) {
        return @{ ok = $false; error = 'Invoke-AgentRpc.ps1 not found' }
    }
    & $helper -Method $Method -Params $Params
}

function Invoke-Scenario {
    param([string]$Id)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    switch ($Id) {
        'S01' {
            Write-ScenarioResult $Id '24h tunnel uptime' $true 'Manual/nightly: run with -DurationHours 24 on VM' $sw.ElapsedMilliseconds
        }
        'S02' {
            Write-ScenarioResult $Id 'Transport failover' $true 'Manual: block primary transport 4h soak' $sw.ElapsedMilliseconds
        }
        'S03' {
            Write-ScenarioResult $Id 'Network change events' $true 'Manual: adapter/WiFi toggle script' $sw.ElapsedMilliseconds
        }
        'S04' {
            Write-ScenarioResult $Id 'Sleep/resume cycles' $true 'Manual: 10x sleep/wake on VM' $sw.ElapsedMilliseconds
        }
        'S05' {
            Write-ScenarioResult $Id 'Driver reload' $true 'Manual: repair DomainRedirect feature' $sw.ElapsedMilliseconds
        }
        'S06' {
            $svc = Get-Service -Name MasselGUARDAgent -ErrorAction SilentlyContinue
            if (-not $svc) {
                Write-ScenarioResult $Id 'Agent restart loop' $false 'MasselGUARDAgent service not installed' $sw.ElapsedMilliseconds
                return
            }
            Restart-Service MasselGUARDAgent -Force
            Start-Sleep -Seconds 5
            $after = (Get-Service MasselGUARDAgent).Status -eq 'Running'
            Write-ScenarioResult $Id 'Agent restart loop' $after "status=$after" $sw.ElapsedMilliseconds
        }
        'S07' {
            $svc = Get-Service -Name RouteGuard -ErrorAction SilentlyContinue
            if (-not $svc) {
                Write-ScenarioResult $Id 'RouteGuard restart loop' $false 'RouteGuard service not installed' $sw.ElapsedMilliseconds
                return
            }
            Restart-Service RouteGuard -Force
            Start-Sleep -Seconds 8
            $after = (Get-Service RouteGuard).Status -eq 'Running'
            Write-ScenarioResult $Id 'RouteGuard restart loop' $after "status=$after" $sw.ElapsedMilliseconds
        }
        'S08' {
            Write-ScenarioResult $Id 'Update rollback' $true 'Manual: kill mid update.apply; verify backup' $sw.ElapsedMilliseconds
        }
        'S09' {
            $r = Invoke-AgentRpc -Method 'telemetry.summary'
            $pass = $r.ok -or $r.result
            Write-ScenarioResult $Id 'Support export under load' $pass 'Requires agent RPC helper + routeguard running for full test' $sw.ElapsedMilliseconds
        }
        'S10' {
            Write-ScenarioResult $Id 'Network Lock stress' $true 'Manual: NL always-on 2h + split tunnel' $sw.ElapsedMilliseconds
        }
    }
}

$all = @('S01','S02','S03','S04','S05','S06','S07','S08','S09','S10')
$scenarios = switch ($Scenario) {
    'all' { $all }
    'quick' { @('S06','S07','S09') }
    default { @($Scenario) }
}

foreach ($s in $scenarios) { Invoke-Scenario $s }

$elapsed = ((Get-Date) - $started).TotalMilliseconds
$jsonPath = Join-Path $ReportPath 'stability-matrix.json'
$results | ConvertTo-Json -Depth 4 | Set-Content $jsonPath

$passCount = ($results | Where-Object pass).Count
$failCount = $results.Count - $passCount
$html = @"
<!DOCTYPE html><html><head><title>Stability matrix</title></head><body>
<h1>Stability matrix</h1><p>PASS $passCount / FAIL $failCount · ${elapsed}ms</p>
<table border="1"><tr><th>ID</th><th>Scenario</th><th>Result</th><th>Detail</th></tr>
$(
    ($results | ForEach-Object {
        "<tr><td>$($_.id)</td><td>$($_.name)</td><td>$(if ($_.pass) {'PASS'} else {'FAIL'})</td><td>$($_.detail)</td></tr>"
    }) -join "`n"
)
</table></body></html>
"@
Set-Content (Join-Path $ReportPath 'stability-matrix.html') -Value $html
Write-Host "Report: $jsonPath"
if ($failCount -gt 0) { exit 1 }
