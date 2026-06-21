# C10 — network adapter / WiFi churn stress
param(
    [int]$Cycles = 20,
    [switch]$WhatIf
)
if ($env:MASSELGUARD_CHAOS -ne '1') { Write-Warning 'Set MASSELGUARD_CHAOS=1'; return @{ injected = $false } }
if ($WhatIf) { return @{ injected = $false; whatIf = $true; cycles = $Cycles } }
$results = @()
for ($i = 0; $i -lt $Cycles; $i++) {
    $adapter = Get-NetAdapter | Where-Object Status -eq 'Up' | Select-Object -First 1
    if (-not $adapter) { break }
    Disable-NetAdapter -Name $adapter.Name -Confirm:$false
    Start-Sleep -Seconds 2
    Enable-NetAdapter -Name $adapter.Name -Confirm:$false
    Start-Sleep -Seconds 3
    $results += @{ cycle = $i + 1; adapter = $adapter.Name }
}
return @{ injected = $true; cycles = $results.Count; detail = $results }
