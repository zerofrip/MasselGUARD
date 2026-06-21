# C07 — driver unavailable simulation
param([switch]$WhatIf)
if ($env:MASSELGUARD_CHAOS -ne '1') { Write-Warning 'Set MASSELGUARD_CHAOS=1'; return @{ injected = $false } }
$svc = Get-Service -Name 'RouteGuardCallout' -ErrorAction SilentlyContinue
if (-not $svc) { return @{ injected = $false; reason = 'RouteGuardCallout service not installed' } }
if ($WhatIf) { return @{ injected = $false; whatIf = $true; action = 'Stop-Service RouteGuardCallout' } }
Stop-Service RouteGuardCallout -Force -ErrorAction SilentlyContinue
return @{ injected = $true; action = 'stopped RouteGuardCallout' }
