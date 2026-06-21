# Minimal agent JSON-RPC over named pipe (single request/response line)
param(
    [Parameter(Mandatory = $true)]
    [string]$Method,
    [hashtable]$Params = @{}
)

$pipeName = 'MasselGUARD'
$req = @{
    jsonrpc = '2.0'
    id = 1
    method = $Method
    params = $Params
} | ConvertTo-Json -Compress

try {
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $pipeName, [System.IO.Pipes.PipeDirection]::InOut)
    $pipe.Connect(3000)
    $writer = New-Object System.IO.StreamWriter($pipe)
    $writer.AutoFlush = $true
    $reader = New-Object System.IO.StreamReader($pipe)
    $writer.WriteLine($req)
    $line = $reader.ReadLine()
    $pipe.Dispose()
    if (-not $line) { return @{ ok = $false; error = 'empty response' } }
    $resp = $line | ConvertFrom-Json
    if ($resp.error) { return @{ ok = $false; error = $resp.error.message } }
    return @{ ok = $true; result = $resp.result }
} catch {
    return @{ ok = $false; error = $_.Exception.Message }
}
