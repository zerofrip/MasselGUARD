# Validate that every lang/*.json has the same keys as lang/en.json (canonical).
param(
    [string]$LangDir = ""
)
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $Root
if (-not $LangDir) { $LangDir = Join-Path $RepoRoot "lang" }

$canonicalPath = Join-Path $LangDir "en.json"
if (-not (Test-Path $canonicalPath)) {
    Write-Error "Canonical lang file not found: $canonicalPath"
}

function Get-LangKeys([string]$Path) {
    $json = Get-Content -Raw -Encoding UTF8 $Path | ConvertFrom-Json
    $props = $json.PSObject.Properties | Where-Object { $_.Name -notlike '_*' }
    return [System.Collections.Generic.HashSet[string]]::new([string[]]($props.Name | Sort-Object))
}

$canonical = Get-LangKeys $canonicalPath
Write-Host "Canonical en.json: $($canonical.Count) keys"

$failed = $false
Get-ChildItem $LangDir -Filter "*.json" | ForEach-Object {
    if ($_.Name -eq "en.json") { return }
    $keys = Get-LangKeys $_.FullName
    $missing = @($canonical | Where-Object { -not $keys.Contains($_) })
    $extra = @($keys | Where-Object { -not $canonical.Contains($_) })
    if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
        $failed = $true
        Write-Host "FAIL $($_.Name): missing=$($missing.Count) extra=$($extra.Count)"
        if ($missing.Count -gt 0) { Write-Host "  missing: $($missing -join ', ')" }
        if ($extra.Count -gt 0) { Write-Host "  extra: $($extra -join ', ')" }
    } else {
        Write-Host "OK   $($_.Name): $($keys.Count) keys"
    }
}

if ($failed) { exit 1 }
Write-Host "All locale files match en.json"
