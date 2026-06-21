param(
    [Parameter(Mandatory = $true)]
    [string]$InstallDir
)

$ErrorActionPreference = "Stop"
$backupRoot = Join-Path $env:ProgramData "MasselGUARD\installer\rollback"
$stamp = Get-Date -Format "yyyyMMddHHmmss"
$dest = Join-Path $backupRoot $stamp
New-Item -ItemType Directory -Path $dest -Force | Out-Null

if (Test-Path $InstallDir) {
    Write-Host "Backing up existing install to $dest"
    robocopy $InstallDir $dest /MIR /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
}

Set-ItemProperty -Path "HKLM:\SOFTWARE\MasselGUARD" -Name "RollbackPath" -Value $dest -ErrorAction SilentlyContinue
exit 0
