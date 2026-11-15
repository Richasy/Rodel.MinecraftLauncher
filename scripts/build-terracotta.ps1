param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateSet('x64','arm64')][string]$Architecture = 'x64'
)
$ErrorActionPreference = 'Stop'
function Get-ArchiveHash([string]$File) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($File)
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}
$version = '0.4.2'
$upstreamArch = if ($Architecture -eq 'arm64') { 'arm64' } else { 'x86_64' }
$expected = if ($Architecture -eq 'arm64') { 'acfab0a87a02dedc6dab7c05303186c8907f56f815548b693fb3324358da7d14' } else { '07ebe139e3ca5f74576e58b1a96efe59abdfbe148d3f1a49bfdca8b6f70745f0' }
$targetDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
$archiveName = "terracotta-$version-windows-$upstreamArch-pkg.tar.gz"
$archivePath = Join-Path $targetDirectory $archiveName
if (!(Test-Path -LiteralPath $archivePath) -or (Get-ArchiveHash $archivePath) -ne $expected) {
    $temporary = $archivePath + '.download'
    try {
        & curl.exe --fail --location --silent --show-error --retry 3 --connect-timeout 30 --max-time 180 --retry-max-time 600 `
            --output $temporary "https://github.com/burningtnt/Terracotta/releases/download/v$version/$archiveName"
        if ($LASTEXITCODE -ne 0) { throw "Terracotta download failed (curl exit $LASTEXITCODE)." }
        if ((Get-ArchiveHash $temporary) -ne $expected) { throw 'Terracotta archive checksum mismatch.' }
        Move-Item -LiteralPath $temporary -Destination $archivePath -Force
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
}
& tar.exe -xzf $archivePath -C $targetDirectory
if ($LASTEXITCODE -ne 0) { throw 'Terracotta extraction failed.' }
Copy-Item -LiteralPath (Join-Path $targetDirectory "terracotta-$version-windows-$upstreamArch.exe") -Destination (Join-Path $targetDirectory 'terracotta.exe') -Force
$notice = "Terracotta v$version`r`nCopyright (C) Burning_TNT and Terracotta contributors.`r`nUnmodified upstream release, bundled for IPC integration.`r`nSource and corresponding release: https://github.com/burningtnt/Terracotta/tree/v$version`r`nLicense: GNU AGPL-3.0-or-later with the upstream integration exception.`r`n"
[IO.File]::WriteAllText((Join-Path $targetDirectory 'NOTICE.txt'), $notice, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../licenses/Terracotta.LICENSE') -Destination (Join-Path $targetDirectory 'LICENSE.txt') -Force
