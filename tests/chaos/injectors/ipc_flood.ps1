# C12 — IPC flood to agent.ping
param(
    [int]$RequestsPerSec = 100,
    [int]$DurationSec = 5,
    [switch]$WhatIf
)
if ($env:MASSELGUARD_CHAOS -ne '1') { Write-Warning 'Set MASSELGUARD_CHAOS=1'; return @{ injected = $false } }
$helper = Join-Path (Split-Path $PSScriptRoot -Parent) 'lib/Invoke-AgentRpc.ps1'
if ($WhatIf) { return @{ injected = $false; whatIf = $true; rps = $RequestsPerSec; durationSec = $DurationSec } }
$ok = 0; $fail = 0; $crashed = $false
$deadline = (Get-Date).AddSeconds($DurationSec)
while ((Get-Date) -lt $deadline) {
    1..$RequestsPerSec | ForEach-Object {
        $r = & $helper -Method 'agent.ping' -Params @{}
        if ($r.ok) { $script:ok++ } else { $script:fail++ }
    }
    Start-Sleep -Milliseconds 900
}
$after = & $helper -Method 'agent.ping' -Params @{}
if (-not $after.ok) { $crashed = $true }
return @{ injected = $true; ok = $ok; fail = $fail; agentAlive = $after.ok; crashed = $crashed }
