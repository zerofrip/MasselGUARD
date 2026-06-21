# Observability smoke matrix (Phase 10/13)
param(
    [string]$ReportPath = 'reports/observability'
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $ReportPath -Force | Out-Null

$helper = Join-Path (Split-Path $MyInvocation.MyCommand.Path) 'stability/Invoke-AgentRpc.ps1'
$checks = @()

if (Test-Path $helper) {
    foreach ($method in @('agent.status', 'routeguard.status', 'telemetry.summary')) {
        $r = & $helper -Method $method
        $checks += [pscustomobject]@{ method = $method; pass = [bool]$r.ok; detail = if ($r.error) { $r.error } else { 'ok' } }
    }
} else {
    $checks += [pscustomobject]@{ method = 'setup'; pass = $false; detail = 'Invoke-AgentRpc.ps1 missing' }
}

$checks | ConvertTo-Json | Set-Content (Join-Path $ReportPath 'observability-matrix.json')
$fail = ($checks | Where-Object { -not $_.pass }).Count
if ($fail -gt 0) { exit 1 }
