# C05 — WFP filter corruption (dev hook; requires ROUTE_GUARD_CHAOS=1 on RG side)
param([switch]$WhatIf)
if ($env:MASSELGUARD_CHAOS -ne '1') { Write-Warning 'Set MASSELGUARD_CHAOS=1'; return @{ injected = $false } }
if ($WhatIf) { return @{ injected = $false; whatIf = $true; action = 'delete one RouteGuard_KS_* filter via test hook' } }
return @{ injected = $false; reason = 'manual: delete filter via WFP test hook or routeguard-cli when ROUTE_GUARD_CHAOS=1' }
