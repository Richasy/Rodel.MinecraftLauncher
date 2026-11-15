#requires -Version 7.5
param([string]$Repository, [string]$Directory, [string]$Scenario)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$architecture = if ($Scenario -eq 'audit-x64') { 'x64' } else { 'arm64' }
$root = Join-Path $Directory 'audit-source'
foreach ($path in @('scripts', 'mods', 'src\Rodel.MinecraftLauncher.App\Strings\en',
        'src\Rodel.MinecraftLauncher.App\Assets\Backgrounds', 'src\Rodel.MinecraftLauncher.Core\Application\AI\ManagedMcp')) {
    $null = [IO.Directory]::CreateDirectory((Join-Path $root $path))
}
foreach ($script in @('verify-release-package.ps1', 'build-filesystem-mcp.ps1')) {
    Copy-Item -LiteralPath (Join-Path $Repository "scripts\$script") -Destination (Join-Path $root "scripts\$script")
}
$definitionPath = "src\Rodel.MinecraftLauncher.Core\Application\AI\ManagedMcp\components.win-$architecture.json"
Copy-Item -LiteralPath (Join-Path $Repository $definitionPath) -Destination (Join-Path $root $definitionPath)
$definition = Get-Content -LiteralPath (Join-Path $root $definitionPath) -Raw | ConvertFrom-Json -AsHashtable
[IO.File]::WriteAllText((Join-Path $root 'mods\bundled-targets.props'),
    '<Project><ItemGroup><BundledModAsset Include="26.3/fabric/rodel-ai.jar" /></ItemGroup></Project>')
$wallpaper = [Text.Encoding]::UTF8.GetBytes('synthetic-wallpaper')
[IO.File]::WriteAllBytes((Join-Path $root 'src\Rodel.MinecraftLauncher.App\Assets\Backgrounds\sample.jpg'), $wallpaper)
$image = [byte[]]::new(128)
[BitConverter]::GetBytes([uint16]0x5A4D).CopyTo($image, 0)
[BitConverter]::GetBytes(64).CopyTo($image, 60)
[BitConverter]::GetBytes([uint32]0x4550).CopyTo($image, 64)
$machine = if ($architecture -eq 'arm64') { 0xAA64 } else { 0x8664 }
[BitConverter]::GetBytes([uint16]$machine).CopyTo($image, 68)
$imageHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($image)).ToLowerInvariant()
$package = Join-Path $Directory "synthetic-$architecture.msix"
$archive = [IO.Compression.ZipFile]::Open($package, [IO.Compression.ZipArchiveMode]::Create)
function Add-Entry([string]$Name, [byte[]]$Bytes) {
    $stream = $archive.CreateEntry($Name).Open()
    try { $stream.Write($Bytes, 0, $Bytes.Length) }
    finally { $stream.Dispose() }
}
function Add-Text([string]$Name, [string]$Text) {
    Add-Entry $Name ([Text.Encoding]::UTF8.GetBytes($Text))
}
try {
    $displayName = if ($Scenario -eq 'audit-wrong-name') { 'Rodel Minecraft Launcher' } else { 'RMCL' }
    Add-Text 'AppxManifest.xml' @"
<Package xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"><Identity Name="60520B029E250.RodelMinecraftLauncher" Publisher="CN=9EFA2047-A580-400B-A5B2-0C7ABB0DB5C0" Version="0.2611.1.0" ProcessorArchitecture="$architecture" />
<Properties><DisplayName>RMCL</DisplayName></Properties>
<Applications><Application Executable="Rodel.MinecraftLauncher.App.exe"><uap:VisualElements DisplayName="$displayName"><uap:DefaultTile ShortName="RMCL" /></uap:VisualElements></Application></Applications><Resources><Resource Language="en" /></Resources></Package>
"@
    Add-Entry 'Rodel.MinecraftLauncher.App.exe' $image
    Add-Text 'resources.pri' 'synthetic-resources'
    Add-Entry 'Assets/Backgrounds/sample.jpg' $wallpaper
    Add-Text 'Assets/BuiltInMods/26.3/fabric/rodel-ai.jar' 'synthetic-jar'
    Add-Text 'Assets/BuiltInMods/catalog.json' '{"targets":[{"minecraftVersion":"26.3","loader":"fabric","modules":["rodel-ai"]}]}'
    $runtime = @{ schemaVersion = 1; platform = "win-$architecture"; components = @() }
    foreach ($component in $definition.components) {
        $record = [ordered]@{
            id = $component.id; version = $component.version; revision = $component.revision
            repository = $component.repository; license = $component.license
            archiveSha256 = $component.archiveSha256
            executable = "$($component.id)\$($component.executable)"
            executableSha256 = $imageHash
        }
        if ($component['archiveFormat'] -eq 'cargo-source') {
            $record.rustToolchain = $component.rustToolchain
            $record.buildRecipeSha256 = if ($Scenario -eq 'audit-tampered-source') { '0' * 64 }
                else { (Get-FileHash -LiteralPath (Join-Path $root 'scripts\build-filesystem-mcp.ps1')).Hash.ToLowerInvariant() }
        }
        $runtime.components += $record
        Add-Entry "Assets/AiMcp/$($component.id)/$($component.executable)" $image
        Add-Text "Assets/AiMcp/$($component.id)/LICENSE.txt" 'synthetic-license'
    }
    Add-Text 'Assets/AiMcp/runtime.json' ($runtime | ConvertTo-Json -Depth 6)
    Add-Entry 'Assets/Terracotta/terracotta.exe' $image
    Add-Text 'Assets/Terracotta/LICENSE.txt' 'synthetic-license'
}
finally { $archive.Dispose() }
$submission = [IO.Compression.ZipFile]::Open([IO.Path]::ChangeExtension($package, '.msixupload'), [IO.Compression.ZipArchiveMode]::Create)
try {
    $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($submission, $package, [IO.Path]::GetFileName($package))
}
finally { $submission.Dispose() }
$caught = $false
$failure = $null
try {
    $null = & (Join-Path $root 'scripts\verify-release-package.ps1') -Package $package -Version '0.2611.1.0' -Architecture $architecture -Store
}
catch { $caught = $true; $failure = $_.Exception.Message }
$audit = if (Test-Path -LiteralPath "$package.audit.json") { Get-Content -LiteralPath "$package.audit.json" -Raw | ConvertFrom-Json -AsHashtable } else { $null }
@{ caught = $caught; failure = $failure; audit = $audit } | ConvertTo-Json -Depth 8 -Compress
