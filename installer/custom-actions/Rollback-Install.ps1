param(
    [Parameter(Mandatory = $true)]
    [string]$InstallDir
)

$ErrorActionPreference = "SilentlyContinue"
$rollback = (Get-ItemProperty -Path "HKLM:\SOFTWARE\MasselGUARD" -Name "RollbackPath" -ErrorAction SilentlyContinue).RollbackPath
if (-not $rollback -or -not (Test-Path $rollback)) {
    Write-Host "No rollback snapshot — nothing to restore"
    exit 0
}

Write-Host "Rolling back install from $rollback"
robocopy $rollback $InstallDir /MIR /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
exit 0
