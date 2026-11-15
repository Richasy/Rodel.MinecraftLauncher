#requires -Version 7.5
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Package,
    [Parameter(Mandatory)][string]$Version,
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    [switch]$Store,
    [string]$MicrosoftClientId
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$Package = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Package)
$packageSha256 = (Get-FileHash -LiteralPath $Package -Algorithm SHA256).Hash.ToLowerInvariant()
$archive = [IO.Compression.ZipFile]::OpenRead($Package)
function Read-PackageText([string]$Name) {
    $entry = $archive.GetEntry($Name)
    if (-not $entry) { throw "Missing package entry: $Name" }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { $reader.ReadToEnd() } finally { $reader.Dispose() }
}
function Get-PackageHash([string]$Name) {
    $entry = $archive.GetEntry($Name)
    if (-not $entry -or -not $entry.Length) { throw "Missing or empty package entry: $Name" }
    $stream = $entry.Open()
    try { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
    finally { $stream.Dispose() }
}
function Assert-PackageArchitecture([string]$Name, [switch]$AllowHybridDll) {
    $entry = $archive.GetEntry($Name)
    if (-not $entry) { throw "Missing $Architecture binary: $Name" }
    $reader = [IO.BinaryReader]::new($entry.Open())
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw "Invalid DOS header: $Name" }
        $null = $reader.ReadBytes(58)
        $offset = $reader.ReadInt32()
        if ($offset -lt 64 -or $offset -gt 1048576) { throw "Invalid PE offset: $Name" }
        $null = $reader.ReadBytes($offset - 64)
        $machine = if ($Architecture -eq 'arm64') { 0xAA64 } else { 0x8664 }
        $signature = $reader.ReadUInt32()
        $actual = $reader.ReadUInt16()
        $hybrid = $AllowHybridDll -and $Architecture -eq 'arm64' -and $actual -eq 0xA64E
        if ($signature -ne 0x4550 -or ($actual -ne $machine -and -not $hybrid)) {
            throw "Package binary is not a Windows $Architecture PE: $Name"
        }
    }
    finally { $reader.Dispose() }
}
try {
    [xml]$manifest = Read-PackageText 'AppxManifest.xml'
    $identity = $manifest.Package.Identity
    if ($identity.Name -cne '60520B029E250.RodelMinecraftLauncher' -or
        $identity.Publisher -cne 'CN=9EFA2047-A580-400B-A5B2-0C7ABB0DB5C0' -or
        $identity.Version -cne $Version -or $identity.ProcessorArchitecture -cne $Architecture -or
        $manifest.Package.Applications.Application.Executable -cne 'Rodel.MinecraftLauncher.App.exe') {
        throw 'Final package identity, version, architecture or executable does not match the release contract.'
    }
    $visual = $manifest.SelectSingleNode('/*[local-name()="Package"]/*[local-name()="Applications"]/*[local-name()="Application"]/*[local-name()="VisualElements"]')
    $tile = if ($visual) { $visual.SelectSingleNode('./*[local-name()="DefaultTile"]') } else { $null }
    if ($manifest.Package.Properties.DisplayName -cne 'RMCL' -or -not $visual -or
        $visual.GetAttribute('DisplayName') -cne 'RMCL' -or
        ($tile -and $tile.HasAttribute('ShortName') -and $tile.GetAttribute('ShortName') -cne 'RMCL')) {
        throw 'Final package display name must be RMCL for every supported language.'
    }
    $signature = $archive.GetEntry('AppxSignature.p7x')
    if (($Store -and $signature) -or (-not $Store -and -not $signature)) { throw 'Incorrect Store/sideload signing state.' }
    $names = @($archive.Entries.FullName)
    $unexpected = @($names | Where-Object {
        $_ -match '(?i)(^|/)(bin|obj|test|tests|gametest|nativetest|logs|\.local|\.git|\.gradle|\.sprout-devtools)(/|$)' -or
        $_ -match '(?i)\.(pdb|appxsym|resw|pfx|p12|key|log|tmp|bak|download|cs|java|gguf|safetensors|onnx)$|\.log\.gz$' -or
        $_ -match '(?i)(^|/)(\.env(?:\.[^/]*)?|tokens?\.json|credentials?\.json|secrets?\.json)$' -or
        $_ -match '(?i)(^|/)(rodel-mc|rin|rin-onnx-worker|llama-server)\.exe$|Rodel\.Inference[^/]*\.dll$|rodel[_-](game[_-])?ui' -or
        $_ -match '(?i)(^|/)(onnxruntime[^/]*|ggml[^/]*|llama|cublas[^/]*|cudart[^/]*)\.dll$'
    })
    if ($unexpected.Count) { throw "Forbidden package content: $($unexpected -join ', ')" }
    $null = Get-PackageHash 'Rodel.MinecraftLauncher.App.exe'
    Assert-PackageArchitecture 'Rodel.MinecraftLauncher.App.exe'
    $microsoftLoginConfigured = $false
    if ($MicrosoftClientId) {
        $clientId = [guid]::Empty
        if (-not [guid]::TryParse($MicrosoftClientId, [ref]$clientId) -or $clientId -eq [guid]::Empty) {
            throw 'Microsoft login audit requires a valid application ID.'
        }
        $stream = $archive.GetEntry('Rodel.MinecraftLauncher.App.exe').Open()
        try {
            $buffer = [byte[]]::new(1048576)
            $tail = ''
            while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $text = $tail + [Text.Encoding]::Latin1.GetString($buffer, 0, $read)
                if ($text.Contains($clientId.ToString(), [StringComparison]::OrdinalIgnoreCase)) {
                    $microsoftLoginConfigured = $true
                    break
                }
                $tail = $text.Substring([Math]::Max(0, $text.Length - 36))
            }
        }
        finally { $stream.Dispose() }
        if (-not $microsoftLoginConfigured) { throw 'The packaged NativeAOT executable omits the injected Microsoft OAuth application ID.' }
    }
    foreach ($dll in $names | Where-Object { $_ -like '*.dll' }) {
        Assert-PackageArchitecture $dll -AllowHybridDll
    }
    $null = Get-PackageHash 'resources.pri'
    $languages = @(Get-ChildItem (Join-Path $repository 'src\Rodel.MinecraftLauncher.App\Strings') -Directory | Select-Object -ExpandProperty Name)
    $actualLanguages = @($manifest.Package.Resources.Resource.Language)
    if (@($languages | Where-Object { $_ -notin $actualLanguages }).Count) { throw 'The package omits an application resource language.' }

    $backgrounds = @(Get-ChildItem -LiteralPath (Join-Path $repository 'src\Rodel.MinecraftLauncher.App\Assets\Backgrounds') -Filter '*.jpg' -File)
    $expectedBackgrounds = @($backgrounds | ForEach-Object { 'Assets/Backgrounds/' + $_.Name })
    $actualBackgrounds = @($names | Where-Object { $_ -like 'Assets/Backgrounds/*' -and -not $_.EndsWith('/') })
    if (-not $backgrounds.Count -or @(Compare-Object $expectedBackgrounds $actualBackgrounds).Count) {
        throw 'Wallpaper payload must contain exactly the current JPEG backgrounds, without stale PNG files.'
    }
    foreach ($background in $backgrounds) {
        if ((Get-PackageHash ('Assets/Backgrounds/' + $background.Name)) -cne
            (Get-FileHash -LiteralPath $background.FullName -Algorithm SHA256).Hash.ToLowerInvariant()) {
            throw "Wallpaper checksum mismatch: $($background.Name)"
        }
    }

    [xml]$targets = Get-Content -LiteralPath (Join-Path $repository 'mods\bundled-targets.props') -Raw
    $expectedMods = @($targets.Project.ItemGroup.BundledModAsset | ForEach-Object { $_.Include.Split(';') } |
        ForEach-Object { 'Assets/BuiltInMods/' + $_ })
    $actualMods = @($names | Where-Object { $_ -like 'Assets/BuiltInMods/*' -and -not $_.EndsWith('/') })
    if (@(Compare-Object ($expectedMods + 'Assets/BuiltInMods/catalog.json') $actualMods).Count) {
        throw 'Bundled payload does not match the registered JAR matrix plus catalog.json.'
    }
    $catalog = Read-PackageText 'Assets/BuiltInMods/catalog.json' | ConvertFrom-Json
    foreach ($target in $catalog.targets) {
        foreach ($module in $target.modules) {
            if ("Assets/BuiltInMods/$($target.minecraftVersion)/$($target.loader)/$module.jar" -notin $expectedMods) {
                throw 'Bundled catalog contains an unregistered artifact.'
            }
        }
    }
    $componentDefinition = Get-Content -LiteralPath (Join-Path $repository "src\Rodel.MinecraftLauncher.Core\Application\AI\ManagedMcp\components.win-$Architecture.json") -Raw | ConvertFrom-Json
    $runtime = Read-PackageText 'Assets/AiMcp/runtime.json' | ConvertFrom-Json
    if ($runtime.schemaVersion -ne 1 -or $runtime.platform -cne "win-$Architecture" -or
        $runtime.components.Count -ne $componentDefinition.components.Count) { throw 'MCP runtime platform or component count mismatch.' }
    $expectedMcp = @('Assets/AiMcp/runtime.json')
    foreach ($component in $componentDefinition.components) {
        $record = @($runtime.components | Where-Object { $_.id -ceq $component.id })
        if ($record.Count -ne 1 -or $record[0].revision -cne $component.revision -or $record[0].version -cne $component.version -or
            $record[0].repository -cne $component.repository -or $record[0].license -cne $component.license -or
            $record[0].archiveSha256 -cne $component.archiveSha256 -or
            $record[0].executable.Replace('\', '/') -cne "$($component.id)/$($component.executable)") {
            throw "MCP provenance mismatch: $($component.id)"
        }
        if ($component.PSObject.Properties['archiveFormat'] -and $component.archiveFormat -eq 'cargo-source' -and
            ($record[0].rustToolchain -cne $component.rustToolchain -or
            $record[0].buildRecipeSha256 -cne (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'build-filesystem-mcp.ps1')).Hash.ToLowerInvariant())) {
            throw "MCP source build recipe mismatch: $($component.id)"
        }
        $executable = "Assets/AiMcp/$($component.id)/$($component.executable)"
        $license = "Assets/AiMcp/$($component.id)/LICENSE.txt"
        if ((Get-PackageHash $executable) -cne $record[0].executableSha256) { throw "MCP executable checksum mismatch: $executable" }
        Assert-PackageArchitecture $executable
        $null = Get-PackageHash $license
        $expectedMcp += $executable, $license
    }
    if (@(Compare-Object $expectedMcp @($names | Where-Object { $_ -like 'Assets/AiMcp/*' -and -not $_.EndsWith('/') })).Count) {
        throw 'Unexpected files in the fixed MCP payload.'
    }
    $null = Get-PackageHash 'Assets/Terracotta/terracotta.exe'
    Assert-PackageArchitecture 'Assets/Terracotta/terracotta.exe'
    $null = Get-PackageHash 'Assets/Terracotta/LICENSE.txt'
    if ($Store) {
        $submission = [IO.Compression.ZipFile]::OpenRead([IO.Path]::ChangeExtension($Package, '.msixupload'))
        try {
            if ($submission.Entries.Count -ne 1 -or
                $submission.Entries[0].FullName -cne [IO.Path]::GetFileName($Package)) {
                throw 'Store upload must contain only the exact MSIX, without debug symbols or extra files.'
            }
            $stream = $submission.Entries[0].Open()
            try { $embeddedSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
            finally { $stream.Dispose() }
            if ($embeddedSha256 -cne $packageSha256) { throw 'Store upload contains a different MSIX.' }
        }
        finally { $submission.Dispose() }
    }
    $receipt = [ordered]@{
        package = [IO.Path]::GetFileName($Package)
        sha256 = $packageSha256
        version = $Version
        architecture = $Architecture
        displayName = [string]$manifest.Package.Properties.DisplayName
        signed = [bool]$signature
        languages = $actualLanguages
        bundledMods = @($expectedMods | ForEach-Object { @{ path = $_; sha256 = Get-PackageHash $_ } })
        managedMcpComponents = @($runtime.components.id)
        localInference = $false
        microsoftLoginConfigured = $microsoftLoginConfigured
        files = @($names | Sort-Object)
    }
    $receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath ($Package + '.audit.json') -Encoding utf8NoBOM
    Write-Output "Verified $($receipt.package): $($expectedMods.Count) JARs, $($runtime.components.Count) fixed MCP components, $($actualLanguages.Count) languages; no local inference or development payload."
}
finally { $archive.Dispose() }
