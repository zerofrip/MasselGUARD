# RouteGuard CLI / pipe IPC wrapper for chaos injectors
param(
    [Parameter(Mandatory = $true)][string]$Method,
    [hashtable]$Params = @{},
    [string]$CliPath = '',
    [int]$TimeoutMs = 5000
)

$ErrorActionPreference = 'Stop'

function Find-RouteGuardCli {
    param([string]$Hint)
    if ($Hint -and (Test-Path $Hint)) { return $Hint }
    $candidates = @(
        (Join-Path $env:ProgramFiles 'RouteGuard\routeguard-cli.exe'),
        (Join-Path $env:ProgramFiles 'MasselGUARD\routeguard-cli.exe'),
        (Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) 'dist\routeguard-cli.exe')
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { return $c }
    }
    return $null
}

$cli = Find-RouteGuardCli -Hint $CliPath
if (-not $cli) {
    return @{ ok = $false; error = 'routeguard-cli.exe not found' }
}

$paramJson = if ($Params.Count -gt 0) { ($Params | ConvertTo-Json -Compress) } else { '{}' }
try {
    $output = & $cli $Method $paramJson 2>&1 | Out-String
    if (-not $output) { return @{ ok = $false; error = 'empty cli output' } }
    $parsed = $output.Trim() | ConvertFrom-Json
    if ($parsed.error) { return @{ ok = $false; error = $parsed.error } }
    return @{ ok = $true; result = $parsed.result ?? $parsed }
} catch {
    return @{ ok = $false; error = $_.Exception.Message }
}
