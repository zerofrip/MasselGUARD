# Process resource snapshot for soak/chaos monitoring
param(
    [string[]]$ProcessNames = @('MasselGUARDAgent', 'routeguard-service'),
    [switch]$IncludeAgentRpc
)

$ErrorActionPreference = 'SilentlyContinue'
$snap = @{
    ts = (Get-Date).ToUniversalTime().ToString('o')
    processes = @()
}

foreach ($name in $ProcessNames) {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
    foreach ($p in $procs) {
        $snap.processes += [pscustomobject]@{
            name = $name
            pid = $p.Id
            rssMb = [math]::Round($p.WorkingSet64 / 1MB, 2)
            privateMb = [math]::Round($p.PrivateMemorySize64 / 1MB, 2)
            handles = $p.HandleCount
            threads = $p.Threads.Count
        }
    }
}

if ($IncludeAgentRpc) {
    $helper = Join-Path $PSScriptRoot 'Invoke-AgentRpc.ps1'
    if (Test-Path $helper) {
        $rpc = & $helper -Method 'agent.diagnostics.resources' -Params @{}
        if ($rpc.ok) { $snap.agentRpc = $rpc.result }
    }
}

return $snap
