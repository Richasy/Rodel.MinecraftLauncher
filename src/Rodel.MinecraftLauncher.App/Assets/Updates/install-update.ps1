param(
    [Parameter(Mandatory)][int]$ParentId,
    [Parameter(Mandatory)][long]$ParentStarted,
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{64}$')][string]$Sha256,
    [Parameter(Mandatory)][string]$Gate
)
$ErrorActionPreference = 'Stop'
try {
    [IO.File]::WriteAllText("$Gate.ready", '')
    try { $parent = [Diagnostics.Process]::GetProcessById($ParentId) }
    catch [ArgumentException] { $parent = $null }
    if ($parent -and $parent.StartTime.ToUniversalTime().Ticks -eq $ParentStarted) {
        if (-not $parent.WaitForExit(600000)) { exit 1 }
    }
    # Only the window's actual Closed event authorizes installation, never a tray hide or a vetoed close.
    if (-not [IO.File]::Exists($Gate)) { exit 1 }
    if ([IO.Path]::GetDirectoryName($Installer) -cne [IO.Path]::GetDirectoryName($Gate) -or
        [IO.Path]::GetFileName($Installer) -cnotmatch '^RMCL-\d+\.\d+\.\d+\.0-win-(x64|arm64)-setup\.exe$') { exit 1 }
    $stream = [IO.File]::OpenRead($Installer)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $actual = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
        if ($actual -cne $Sha256) { exit 1 }
        $start = [Diagnostics.ProcessStartInfo]::new($Installer)
        $start.UseShellExecute = $true
        $process = [Diagnostics.Process]::Start($start)
        try { $process.WaitForExit() }
        finally { $process.Dispose() }
    }
    finally { $algorithm.Dispose(); $stream.Dispose() }
    # Also clean up after a cancelled wizard; a new attempt downloads and verifies fresh bytes.
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try { [IO.File]::Delete($Installer); break }
        catch [IO.IOException] { [Threading.Thread]::Sleep(500) }
    }
}
finally {
    if ([IO.File]::Exists($Gate)) { [IO.File]::Delete($Gate) }
    if ([IO.File]::Exists("$Gate.ready")) { [IO.File]::Delete("$Gate.ready") }
    if ($parent) { $parent.Dispose() }
}
