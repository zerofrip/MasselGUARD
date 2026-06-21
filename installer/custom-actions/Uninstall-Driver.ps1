param(
    [Parameter(Mandatory = $true)]
    [string]$InfName
)

$ErrorActionPreference = "SilentlyContinue"
$regPath = "HKLM:\SOFTWARE\RouteGuard\Driver"
$oem = (Get-ItemProperty -Path $regPath -Name "OemInf" -ErrorAction SilentlyContinue).OemInf

if ($oem) {
    Write-Host "Removing driver package: $oem"
    pnputil.exe /delete-driver $oem /uninstall /force | Out-Null
}

# Fallback: enumerate by original name
pnputil.exe /enum-drivers | Select-String -Pattern $InfName -Context 0,1 | ForEach-Object {
    if ($_ -match "Published name\s*:\s*(oem\d+\.inf)") {
        pnputil.exe /delete-driver $Matches[1] /uninstall /force | Out-Null
    }
}

Remove-Item -Path $regPath -Recurse -Force -ErrorAction SilentlyContinue
exit 0
