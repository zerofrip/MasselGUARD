# Forward to stability helper
param(
    [Parameter(Mandatory = $true)][string]$Method,
    [hashtable]$Params = @{}
)
$RepoRoot = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$StabilityHelper = Join-Path $RepoRoot 'tests/scripts/stability/Invoke-AgentRpc.ps1'
if (-not (Test-Path $StabilityHelper)) {
    return @{ ok = $false; error = "Invoke-AgentRpc.ps1 not found" }
}
& $StabilityHelper -Method $Method -Params $Params
