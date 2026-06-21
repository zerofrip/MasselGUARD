# C11 — kill agent during update.apply (manual VM snapshot required)
param([switch]$WhatIf)
if ($env:MASSELGUARD_CHAOS -ne '1') { Write-Warning 'Set MASSELGUARD_CHAOS=1'; return @{ injected = $false } }
return @{ injected = $false; reason = 'manual: trigger update.apply then Stop-Process MasselGUARDAgent; verify rollback tree' }
