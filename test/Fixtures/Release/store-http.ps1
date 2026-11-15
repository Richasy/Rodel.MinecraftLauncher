#requires -Version 7.5
param([string]$Repository, [string]$Directory, [string]$Scenario)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $Repository 'scripts\StoreRelease.psm1') -Force
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$peer = [powershell]::Create()
$null = $peer.AddScript({
    param($listener, $reject)
    $connection = $listener.AcceptTcpClient()
    try {
        $connection.ReceiveTimeout = 10000
        $stream = $connection.GetStream()
        $header = [Collections.Generic.List[byte]]::new()
        do {
            $next = $stream.ReadByte()
            if ($next -lt 0) { throw 'The native HTTP client closed before sending headers.' }
            $header.Add([byte]$next)
            $text = [Text.Encoding]::ASCII.GetString($header.ToArray())
        } until ($text.EndsWith("`r`n`r`n"))
        $length = [int][regex]::Match($text, '(?im)^Content-Length:\s*(\d+)').Groups[1].Value
        $body = [byte[]]::new($length)
        $stream.ReadExactly($body, 0, $length)
        $status = if ($reject) { '400 Bad Request' } else { '200 OK' }
        $json = if ($reject) { '{"code":"InvalidRequest","message":"API rejected synthetic-bearer-token at https://example.invalid/upload?sig=synthetic-sas"}' } else { '{"ok":true}' }
        $response = [Text.Encoding]::UTF8.GetBytes($json)
        $stream.Write([Text.Encoding]::ASCII.GetBytes("HTTP/1.1 $status`r`nContent-Type: application/json`r`nContent-Length: $($response.Length)`r`nConnection: close`r`n`r`n"))
        $stream.Write($response)
        @{ contentType = [regex]::Match($text, '(?im)^Content-Type:\s*([^\r\n;]+)').Groups[1].Value
            body = [Text.Encoding]::UTF8.GetString($body) }
    }
    finally { $connection.Dispose() }
}).AddArgument($listener).AddArgument($Scenario -eq 'http-rejected')
$pending = $peer.BeginInvoke()
try {
    $request = @{ Operation = 'Synthetic native HTTP boundary'; Method = 'POST'
        Uri = "http://127.0.0.1:$($listener.LocalEndpoint.Port)/"; Token = 'synthetic-bearer-token' }
    switch ($Scenario) {
        'http-form' { $request.Form = [ordered]@{ grant_type = 'client_credentials'; client_id = 'synthetic' } }
        'http-json' { $request.Body = @{ value = 'synthetic' } }
        'http-upload' {
            $request.Method = 'PUT'
            $request.File = Join-Path $Directory 'synthetic.zip'
            [IO.File]::WriteAllText($request.File, 'synthetic-zip')
        }
    }
    $caught = $false
    $failure = $null
    try { $null = & (Get-Module StoreRelease) { param($request) Invoke-StoreHttp $request } $request }
    catch { $caught = $true; $failure = $_.Exception.Message }
    $captured = $peer.EndInvoke($pending)[0]
    @{ caught = $caught; failure = $failure; contentType = $captured.contentType; body = $captured.body } |
        ConvertTo-Json -Compress
}
finally { $listener.Stop(); $peer.Stop(); $peer.Dispose() }
