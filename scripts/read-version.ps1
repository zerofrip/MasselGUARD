param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("masselguard.version", "masselguard.codename", "routeguard.version", "releaseChannel")]
    [string]$Property
)

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$doc = Get-Content (Join-Path $Root "version.json") -Raw | ConvertFrom-Json

switch ($Property) {
    "masselguard.version"  { Write-Output $doc.masselguard.version; break }
    "masselguard.codename"   { Write-Output $doc.masselguard.codename; break }
    "routeguard.version"     { Write-Output $doc.routeguard.version; break }
    "releaseChannel"         { Write-Output $doc.releaseChannel; break }
}
