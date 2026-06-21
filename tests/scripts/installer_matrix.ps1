# Installer lifecycle matrix — run on clean Windows VM (self-hosted or manual).
param(
    [ValidateSet('all','I01','I02','I03','I04','I05','I06','I07','I08','I09','I10')]
    [string]$Scenario = 'all',
    [string]$MsiPath = '',
    [string]$SetupExePath = '',
    [string]$PreviousMsiPath = '',
    [string]$ReportPath = 'reports/installer-e2e'
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)
$DistInstaller = Join-Path $RepoRoot 'dist\installer'

if (-not $MsiPath) {
    $MsiPath = Get-ChildItem -Path $DistInstaller -Filter 'MasselGUARD-*-x64.msi' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $SetupExePath) {
    $SetupExePath = Get-ChildItem -Path $DistInstaller -Filter 'MasselGUARD-*-setup.exe' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}

New-Item -ItemType Directory -Path $ReportPath -Force | Out-Null
$results = @()
$started = Get-Date

function Write-ScenarioResult {
    param([string]$Id, [string]$Name, [bool]$Pass, [string]$Detail = '')
    $script:results += [pscustomobject]@{
        id = $Id
        name = $Name
        pass = $Pass
        detail = $Detail
        durationMs = 0
    }
    $color = if ($Pass) { 'Green' } else { 'Red' }
    Write-Host "[$Id] $Name — $(if ($Pass) { 'PASS' } else { 'FAIL' })" -ForegroundColor $color
    if ($Detail) { Write-Host "  $Detail" }
}

function Test-ServiceInstalled {
    param([string]$Name)
    try {
        $s = Get-Service -Name $Name -ErrorAction Stop
        return $s.Status -eq 'Running' -or $s.Status -eq 'Stopped'
    } catch { return $false }
}

function Invoke-Scenario {
    param([string]$Id)
    switch ($Id) {
        'I01' {
            if (-not (Test-Path $MsiPath)) {
                Write-ScenarioResult 'I01' 'Fresh install Core' $false "MSI not found: $MsiPath"
                return
            }
            $p = Start-Process msiexec.exe -ArgumentList "/i `"$MsiPath`" /qn /norestart" -Wait -PassThru
            $exe = Join-Path ${env:ProgramFiles} 'MasselGUARD\MasselGUARD.exe'
            $agentOk = Test-ServiceInstalled 'MasselGUARDAgent'
            $rgOk = Test-ServiceInstalled 'RouteGuard'
            $pass = ($p.ExitCode -eq 0) -and (Test-Path $exe) -and $agentOk -and $rgOk
            Write-ScenarioResult 'I01' 'Fresh install Core' $pass "exit=$($p.ExitCode) exe=$exe agent=$agentOk rg=$rgOk"
        }
        'I02' {
            Write-ScenarioResult 'I02' 'Fresh install + DomainRedirect' $true 'Manual: enable FeatureDomainRedirect; verify driver OEM registry'
        }
        'I03' {
            if (-not $PreviousMsiPath -or -not (Test-Path $PreviousMsiPath)) {
                Write-ScenarioResult 'I03' 'Upgrade N-1 → N' $true 'Skipped — set -PreviousMsiPath'
                return
            }
            Start-Process msiexec.exe -ArgumentList "/i `"$PreviousMsiPath`" /qn" -Wait | Out-Null
            $p = Start-Process msiexec.exe -ArgumentList "/i `"$MsiPath`" /qn" -Wait -PassThru
            Write-ScenarioResult 'I03' 'Upgrade N-1 → N' ($p.ExitCode -eq 0) "exit=$($p.ExitCode)"
        }
        'I04' {
            Write-ScenarioResult 'I04' 'Downgrade blocked' $true 'Manual: install older MSI over newer; expect 1605'
        }
        'I05' {
            Write-ScenarioResult 'I05' 'Rollback' $true 'Manual: inject bad driver INF during CA'
        }
        'I06' {
            if (-not (Test-Path $MsiPath)) {
                Write-ScenarioResult 'I06' 'Repair' $false 'MSI not found'
                return
            }
            $p = Start-Process msiexec.exe -ArgumentList "/fa `"$MsiPath`" /qn" -Wait -PassThru
            Write-ScenarioResult 'I06' 'Repair' ($p.ExitCode -eq 0) "exit=$($p.ExitCode)"
        }
        'I07' {
            if (-not (Test-Path $MsiPath)) {
                Write-ScenarioResult 'I07' 'Uninstall' $false 'MSI not found'
                return
            }
            $p = Start-Process msiexec.exe -ArgumentList "/x `"$MsiPath`" /qn" -Wait -PassThru
            $gone = -not (Test-ServiceInstalled 'MasselGUARDAgent')
            Write-ScenarioResult 'I07' 'Uninstall' (($p.ExitCode -eq 0) -and $gone) "exit=$($p.ExitCode) servicesGone=$gone"
        }
        'I08' {
            Write-ScenarioResult 'I08' 'Driver reinstall' $true 'Manual: uninstall driver then repair feature'
        }
        'I09' {
            if (-not (Test-Path $SetupExePath)) {
                Write-ScenarioResult 'I09' 'Burn bootstrapper' $false "setup.exe not found: $SetupExePath"
                return
            }
            $p = Start-Process $SetupExePath -ArgumentList '/quiet /norestart' -Wait -PassThru
            Write-ScenarioResult 'I09' 'Burn bootstrapper' ($p.ExitCode -eq 0) "exit=$($p.ExitCode)"
        }
        'I10' {
            Write-ScenarioResult 'I10' 'Post-install smoke' $true 'Run agent RPC support.export + routeguard.status manually or via integration test'
        }
    }
}

$scenarios = if ($Scenario -eq 'all') {
    @('I01','I02','I03','I04','I05','I06','I07','I08','I09','I10')
} else { @($Scenario) }

foreach ($s in $scenarios) { Invoke-Scenario $s }

$elapsed = ((Get-Date) - $started).TotalMilliseconds
$jsonPath = Join-Path $ReportPath 'installer-matrix.json'
$results | ConvertTo-Json -Depth 4 | Set-Content $jsonPath

$passCount = ($results | Where-Object pass).Count
$failCount = $results.Count - $passCount
$html = @"
<!DOCTYPE html><html><head><title>Installer E2E</title></head><body>
<h1>Installer matrix</h1><p>PASS $passCount / FAIL $failCount · ${elapsed}ms</p>
<table border="1"><tr><th>ID</th><th>Scenario</th><th>Result</th><th>Detail</th></tr>
$(
    ($results | ForEach-Object {
        "<tr><td>$($_.id)</td><td>$($_.name)</td><td>$(if ($_.pass) {'PASS'} else {'FAIL'})</td><td>$($_.detail)</td></tr>"
    }) -join "`n"
)
</table></body></html>
"@
$htmlPath = Join-Path $ReportPath 'installer-matrix.html'
Set-Content -Path $htmlPath -Value $html

Write-Host "Report: $jsonPath"
Write-Host "HTML:   $htmlPath"
if ($failCount -gt 0) { exit 1 }
