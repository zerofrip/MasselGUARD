# C01 — block outbound UDP to tunnel endpoint (requires MASSELGUARD_CHAOS=1)
param([string]$Endpoint = '', [switch]$WhatIf)
if ($env:MASSELGUARD_CHAOS -ne '1') { Write-Warning 'Set MASSELGUARD_CHAOS=1 to inject'; return @{ injected = $false } }
if ($WhatIf) { return @{ injected = $false; whatIf = $true; action = 'New-NetFirewallRule block UDP egress' } }
# Manual: operator supplies endpoint; stub records intent
return @{ injected = $true; method = 'firewall_udp_block'; endpoint = $Endpoint; note = 'Remove rule after test' }
