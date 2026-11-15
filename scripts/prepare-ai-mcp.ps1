param(
    [Parameter(Mandatory = $true)][string]$Manifest,
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [string]$CacheRoot = (Join-Path $env:LOCALAPPDATA 'Rodel.MinecraftLauncher\BuildAssets\AiMcp')
)

$ErrorActionPreference = 'Stop'
function Get-ComponentHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose(); $stream.Dispose() }
}
$Manifest = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Manifest)
$OutputRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputRoot)
$CacheRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($CacheRoot)
$definition = Get-Content -LiteralPath $Manifest -Raw -Encoding UTF8 | ConvertFrom-Json
if ($definition.schemaVersion -ne 1 -or $definition.platform -notin @('win-x64', 'win-arm64')) {
    throw 'Unsupported managed MCP component manifest.'
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $CacheRoot -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$runtime = [ordered]@{ schemaVersion = 1; platform = $definition.platform; components = @() }
foreach ($component in $definition.components) {
    if ($component.id -notmatch '^rodel_[a-z]+$' -or $component.archiveSha256 -notmatch '^[0-9a-fA-F]{64}$' -or
        $component.executable -notmatch '^[a-zA-Z0-9_.-]+\.exe$') {
        throw 'Invalid managed MCP component identity.'
    }
    $archiveFormat = if ($component.PSObject.Properties['archiveFormat']) { $component.archiveFormat } else { 'zip' }
    $executableArtifact = $archiveFormat -eq 'executable'
    $sourceArtifact = $archiveFormat -eq 'cargo-source'
    if ($archiveFormat -and $archiveFormat -notin @('zip', 'executable', 'cargo-source')) { throw 'Unsupported MCP artifact format.' }
    if ($sourceArtifact -and ($definition.platform -ne 'win-arm64' -or $component.id -ne 'rodel_files' -or $component.rustToolchain -notmatch '^\d+\.\d+\.\d+$')) {
        throw 'Unsupported managed MCP source build.'
    }
    $extension = if ($executableArtifact) { '.exe' } elseif ($sourceArtifact) { '.tar.gz' } else { '.zip' }
    $archive = Join-Path $CacheRoot ($component.archiveSha256 + $extension)
    if (-not (Test-Path -LiteralPath $archive) -or
        (Get-ComponentHash $archive) -ne $component.archiveSha256) {
        $download = $archive + '.' + [guid]::NewGuid().ToString('N') + '.download'
        try {
            Invoke-WebRequest -Uri $component.archiveUrl -OutFile $download -UseBasicParsing
            if ((Get-ComponentHash $download) -ne $component.archiveSha256) {
                throw "Checksum mismatch for $($component.id)."
            }
            Move-Item -LiteralPath $download -Destination $archive -Force
        }
        finally {
            if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download -Force }
        }
    }
    $destination = Join-Path $OutputRoot $component.id
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $target = Join-Path $destination $component.executable
    $sourceExecutable = if ($sourceArtifact) {
        $buildRecipe = Get-ComponentHash (Join-Path $PSScriptRoot 'build-filesystem-mcp.ps1')
        Join-Path $CacheRoot ($component.archiveSha256 + '-win-arm64-rust-' + $component.rustToolchain + '-' + $buildRecipe.Substring(0, 16) + '.exe')
    } else { $archive }
    if ($sourceArtifact) {
        $receipt = $sourceExecutable + '.sha256'
        if (-not (Test-Path -LiteralPath $sourceExecutable) -or -not (Test-Path -LiteralPath $receipt) -or
            (Get-ComponentHash $sourceExecutable) -ne (Get-Content -LiteralPath $receipt -Raw).Trim()) {
            & (Join-Path $PSScriptRoot 'build-filesystem-mcp.ps1') -SourceArchive $archive -OutputFile $sourceExecutable -RustToolchain $component.rustToolchain
            [IO.File]::WriteAllText($receipt, (Get-ComponentHash $sourceExecutable))
        }
    }
    $zip = if ($executableArtifact -or $sourceArtifact) { $null } else { [IO.Compression.ZipFile]::OpenRead($archive) }
    try {
        if ($zip) {
            $executables = @($zip.Entries | Where-Object { [IO.Path]::GetFileName($_.FullName) -eq $component.executable })
            if ($executables.Count -ne 1) { throw "Expected exactly one executable for $($component.id)." }
            $entry = $executables[0]
            if ($entry.Length -gt 268435456) { throw 'Managed MCP executable exceeds its preparation bound.' }
        }
        elseif ((Get-Item -LiteralPath $sourceExecutable).Length -gt 268435456) { throw 'Managed MCP executable exceeds its preparation bound.' }
        $temporary = $target + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
        try {
            if ($executableArtifact -or $sourceArtifact) { Copy-Item -LiteralPath $sourceExecutable -Destination $temporary }
            else { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $temporary, $false) }
            Move-Item -LiteralPath $temporary -Destination $target -Force
        }
        finally {
            if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
        }
    }
    finally { if ($zip) { $zip.Dispose() } }
    $binary = [IO.BinaryReader]::new([IO.File]::OpenRead($target))
    try {
        if ($binary.ReadUInt16() -ne 0x5A4D) { throw "Invalid MCP executable: $($component.id)" }
        $binary.BaseStream.Position = 60
        $offset = $binary.ReadInt32()
        if ($offset -lt 64 -or $offset -gt 1048576) { throw 'Invalid MCP PE offset.' }
        $binary.BaseStream.Position = $offset
        $machine = if ($definition.platform -eq 'win-arm64') { 0xAA64 } else { 0x8664 }
        if ($binary.ReadUInt32() -ne 0x4550 -or $binary.ReadUInt16() -ne $machine) {
            throw "MCP executable does not match $($definition.platform): $($component.id)"
        }
    }
    finally { $binary.Dispose() }
    $licenseFile = Join-Path $destination 'LICENSE.txt'
    $cachedLicense = Join-Path $CacheRoot ($component.id + '-' + $component.revision + '.LICENSE')
    if (-not (Test-Path -LiteralPath $cachedLicense)) {
        $licenseUrl = "https://api.github.com/repos/$($component.repository)/contents/LICENSE?ref=$($component.revision)"
        $license = Invoke-RestMethod -Uri $licenseUrl -Headers @{ Accept = 'application/vnd.github+json' }
        [IO.File]::WriteAllText($cachedLicense, [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($license.content)),
            [Text.UTF8Encoding]::new($false))
    }
    Copy-Item -LiteralPath $cachedLicense -Destination $licenseFile -Force
    $record = [ordered]@{
        id = $component.id; version = $component.version; repository = $component.repository
        revision = $component.revision; license = $component.license
        executable = $component.id + '\' + $component.executable
        executableSha256 = Get-ComponentHash $target
        archiveSha256 = $component.archiveSha256
    }
    if ($sourceArtifact) {
        $record.rustToolchain = $component.rustToolchain
        $record.buildRecipeSha256 = $buildRecipe
    }
    $runtime.components += $record
}
$manifestTarget = Join-Path $OutputRoot 'runtime.json'
$manifestTemporary = $manifestTarget + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.File]::WriteAllText($manifestTemporary, ($runtime | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $manifestTemporary -Destination $manifestTarget -Force
}
finally {
    if (Test-Path -LiteralPath $manifestTemporary) { Remove-Item -LiteralPath $manifestTemporary -Force }
}
Write-Output "Prepared pinned self-contained MCP components: $($definition.components.Count)"
$publishFiles = @('runtime.json')
foreach ($component in $runtime.components) {
    $publishFiles += $component.executable
    $publishFiles += $component.id + '\LICENSE.txt'
}
[IO.File]::WriteAllLines($OutputRoot + '.files', [string[]]$publishFiles, [Text.UTF8Encoding]::new($false))
