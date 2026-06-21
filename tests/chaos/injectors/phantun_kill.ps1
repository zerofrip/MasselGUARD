# C02 — kill Phantun client process
param([switch]$WhatIf)
if ($env:MASSELGUARD_CHAOS -ne '1') { Write-Warning 'Set MASSELGUARD_CHAOS=1'; return @{ injected = $false } }
$procs = Get-Process -Name 'phantun_client','phantun-client' -ErrorAction SilentlyContinue
if (-not $procs) { return @{ injected = $false; reason = 'phantun not running' } }
if ($WhatIf) { return @{ injected = $false; whatIf = $true; pids = @($procs.Id) } }
$procs | Stop-Process -Force
return @{ injected = $true; killed = @($procs.Id) }
