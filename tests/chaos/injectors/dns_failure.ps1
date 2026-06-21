# C06 — DNS proxy failure simulation
param([switch]$WhatIf)
if ($env:MASSELGUARD_CHAOS -ne '1') { Write-Warning 'Set MASSELGUARD_CHAOS=1'; return @{ injected = $false } }
if ($WhatIf) { return @{ injected = $false; whatIf = $true; action = 'block TCP/UDP 5353 or stop DnsProxy' } }
$dns = Get-Process -Name 'routeguard-service' -ErrorAction SilentlyContinue
return @{ injected = $false; reason = 'manual: block port 5353 or restart RG with dns disabled for test VM' }
