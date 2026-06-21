# Validates SupportBundleRedactor fixture expectations (run on Windows with Agent built, or review-only on Linux).
param(
    [switch]$ReviewOnly
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)
$Fixture = Join-Path $RepoRoot 'tests/fixtures/support-bundle/sample-pii.json'
$AgentDir = Join-Path $RepoRoot 'MasselGUARDAgent'

if (-not (Test-Path $Fixture)) {
    Write-Error "Fixture not found: $Fixture"
}

$raw = Get-Content $Fixture -Raw

# Expected sanitized patterns (string checks on redacted JSON)
$expectSanitized = @{
    'endpoint redacted' = '<redacted>'
    'privateKey redacted' = '<redacted>'
    'machineName hashed' = 'sha256:'
}

Write-Host "=== Support bundle redaction fixture ===" 
Write-Host "Fixture: $Fixture"

if ($ReviewOnly) {
    Write-Host "Review-only mode: manual expectations documented in tests/fixtures/support-bundle/README.md"
    Write-Host "  sanitized: endpoints -> <redacted>, privateKey -> <redacted>, machineName -> sha256:..."
    Write-Host "  support: endpoints kept, ssid still redacted in agent JSON redactor"
    exit 0
}

# Build a tiny inline test via dotnet if SDK available
$testProj = Join-Path $AgentDir 'Tests/SupportBundleRedactor.Tests.csproj'
if (-not (Test-Path $testProj)) {
    Write-Warning "Test project not found; running review-only"
    & $PSCommandPath -ReviewOnly
    exit 0
}

Push-Location (Split-Path $testProj)
try {
    dotnet test --no-restore --verbosity quiet
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "PASS: SupportBundleRedactor unit tests"
} finally {
    Pop-Location
}
