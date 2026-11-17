#requires -Version 7.5
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$AssetDirectory,
    [Parameter(Mandatory)][string]$NotesPath,
    [Parameter(Mandatory)][string]$OutputPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Version -cnotmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.0$' -or
    @($Version.Split('.') | Where-Object { [long]$_ -gt 65535 }).Count) { throw 'Invalid release version.' }
$notes = Get-Content -LiteralPath $NotesPath -Raw | ConvertFrom-Json -AsHashtable
$languages = @('zh-CN', 'zh-TW', 'en', 'ja', 'ko', 'de', 'fr', 'es', 'pt-BR', 'ru', 'it', 'pl', 'tr', 'id', 'ar')
if (@(Compare-Object @($notes.Keys | Sort-Object) @($languages | Sort-Object)).Count) { throw 'Release notes must cover exactly the fifteen App languages.' }
foreach ($language in $languages) {
    if ($notes[$language] -isnot [string] -or [string]::IsNullOrWhiteSpace($notes[$language]) -or $notes[$language].Length -gt 16000) {
        throw "Missing or oversized release notes: $language"
    }
}
$root = Split-Path -Parent $PSScriptRoot
$source = (& git -C $root rev-parse --verify HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $source -cnotmatch '^[a-f0-9]{40}$') { throw 'Cannot resolve the release source.' }
$installers = [ordered]@{}
foreach ($architecture in @('x64', 'arm64')) {
    $name = "RMCL-$Version-win-$architecture-setup.exe"
    $path = Join-Path $AssetDirectory $name
    $file = Get-Item -LiteralPath $path
    if ($file.Length -le 0 -or $file.Length -gt 2GB) { throw "Invalid installer size: $name" }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksum = [IO.File]::ReadAllText("$path.sha256").Trim()
    if ($checksum -cne "$hash  $name") { throw "Installer checksum mismatch: $name" }
    $installers[$architecture] = [ordered]@{ fileName = $name; sha256 = $hash; size = $file.Length }
}
$manifest = [ordered]@{ schemaVersion = 1; version = $Version; sourceCommit = $source; notes = $notes; installers = $installers }
$temporary = "$OutputPath.$([guid]::NewGuid().ToString('N')).tmp"
try {
    [IO.File]::WriteAllText($temporary, ($manifest | ConvertTo-Json -Depth 10) + "`n", [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary, $OutputPath, $true)
}
finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
