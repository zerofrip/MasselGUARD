# C08 — agent restart loop
param(
    [int]$Count = 10,
    [int]$IntervalSec = 30,
    [switch]$WhatIf
)
if ($env:MASSELGUARD_CHAOS -ne '1') { Write-Warning 'Set MASSELGUARD_CHAOS=1'; return @{ injected = $false } }
$svc = Get-Service -Name MasselGUARDAgent -ErrorAction SilentlyContinue
if (-not $svc) { return @{ injected = $false; reason = 'MasselGUARDAgent not installed' } }
if ($WhatIf) { return @{ injected = $false; whatIf = $true; count = $Count } }
$results = @()
for ($i = 0; $i -lt $Count; $i++) {
    Restart-Service MasselGUARDAgent -Force
    Start-Sleep -Seconds 5
    $running = (Get-Service MasselGUARDAgent).Status -eq 'Running'
    $results += @{ cycle = $i + 1; running = $running }
    if ($i -lt ($Count - 1)) { Start-Sleep -Seconds $IntervalSec }
}
$allOk = ($results | Where-Object { -not $_.running }).Count -eq 0
return @{ injected = $true; cycles = $results; allRecovered = $allOk }
