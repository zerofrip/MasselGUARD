# C03 — simulate LWO relay death (kill lwo child if present)
param([switch]$WhatIf)
if ($env:MASSELGUARD_CHAOS -ne '1') { Write-Warning 'Set MASSELGUARD_CHAOS=1'; return @{ injected = $false } }
$procs = Get-Process -Name 'lwo','lwo-relay' -ErrorAction SilentlyContinue
if (-not $procs) { return @{ injected = $false; reason = 'lwo relay not running' } }
if ($WhatIf) { return @{ injected = $false; whatIf = $true } }
$procs | Stop-Process -Force
return @{ injected = $true; killed = @($procs.Id) }
