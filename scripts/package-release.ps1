#requires -Version 7.5
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [ValidateSet('Both', 'Sideload', 'Store')][string]$Artifacts = 'Both',
    [ValidateSet('x64', 'arm64')][string[]]$Architectures = @('x64'),
    [switch]$IncludeValidationAssets,
    [string]$OutputDirectory = 'artifacts\release',
    [string]$JavaHome,
    [string]$LegacyJavaHome,
    [string]$ObservationJavaHome,
    [string]$NuGetSource = 'https://www.nuget.org/api/v2/'
)

$ErrorActionPreference = 'Stop'
$clientId = [guid]::Empty
if (-not [guid]::TryParse($env:RODEL_MICROSOFT_CLIENT_ID, [ref]$clientId) -or $clientId -eq [guid]::Empty) {
    throw 'Release packages require the RODEL_MICROSOFT_CLIENT_ID environment variable (the approved public OAuth application ID).'
}
$env:RodelMicrosoftClientId = $clientId.ToString()
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.0$' -or
    @($Version.Split('.') | Where-Object { [long]$_ -gt 65535 }).Count) {
    throw 'Version must contain four canonical UInt16 components and end in .0.'
}
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'src\Rodel.MinecraftLauncher.App\Rodel.MinecraftLauncher.App.csproj'
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'

. (Join-Path $PSScriptRoot 'resolve-build-jdk.ps1')
$env:BundledModsJavaHome = Resolve-BuildJdk $JavaHome 25 'BundledModsJavaHome'
$env:BundledModsLegacyJavaHome = Resolve-BuildJdk $LegacyJavaHome 21 'BundledModsLegacyJavaHome' 24
$env:GameObservationJavaHome = Resolve-BuildJdk $ObservationJavaHome 8 'GameObservationJavaHome' 8
$env:EnableLocalInference = 'false'
$env:CleanBundledMods = 'true'
$env:NuGetAudit = 'false'
$env:RestoreSources = "$NuGetSource;https://pkgs.dev.azure.com/zrichasy/_packaging/sprout/nuget/v3/index.json"
$env:PATH = (Split-Path -Parent $vswhere) + ';' + $env:PATH

function Write-StoreUpload([string]$Package) {
    $upload = [IO.Path]::ChangeExtension($Package, '.msixupload')
    $temporary = "$upload.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        $archive = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
        try {
            $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $Package, [IO.Path]::GetFileName($Package), [IO.Compression.CompressionLevel]::Optimal)
        }
        finally { $archive.Dispose() }
        [IO.File]::Move($temporary, $upload, $true)
    }
    finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
}

$distributions = switch ($Artifacts) {
    'Sideload' { @('Development') }
    'Store' { @('Store') }
    default { @('Development', 'Store') }
}
$failures = [Collections.Generic.List[string]]::new()
foreach ($architecture in ($Architectures | Select-Object -Unique)) {
    $rid = "win-$architecture"
    $component = if ($architecture -eq 'arm64') { 'ARM64' } else { 'x86.x64' }
    $visualStudio = & $vswhere -latest -prerelease -products '*' -requires "Microsoft.VisualStudio.Component.VC.Tools.$component" -property installationPath
    if (-not $visualStudio) { throw "NativeAOT packaging requires the Visual Studio $architecture C++ build tools." }
    $vcvars = Join-Path $visualStudio 'VC\Auxiliary\Build\vcvarsall.bat'
    if (-not (Test-Path -LiteralPath $vcvars)) { throw "Missing NativeAOT environment: $vcvars" }
    $vcArchitecture = if ($architecture -eq 'arm64') { 'x64_arm64' } else { 'x64' }
    & dotnet restore $project -r $rid
    if ($LASTEXITCODE -ne 0) { throw "Application restore failed for $rid ($LASTEXITCODE)." }
    $hasPackage = $false
    foreach ($distribution in $distributions) {
        $name = if ($distribution -eq 'Development') { 'sideload-test' } else { 'store' }
        $destination = Join-Path $output "$Version\$rid\$name"
        $command = "call `"$vcvars`" $vcArchitecture >nul && dotnet msbuild `"$project`" -t:SproutCreateRelease -p:Configuration=Release -p:SproutPackageRids=$rid -p:SproutPackageDistribution=$distribution -p:SproutAppxVersion=$Version -p:SproutPackageOutputDir=`"$destination`" -p:EnableLocalInference=false"
        & $env:ComSpec /d /c $command
        if ($LASTEXITCODE -ne 0) {
            $failures.Add("$rid $name packaging failed (exit $LASTEXITCODE).")
            continue
        }
        $packages = @(Get-ChildItem -LiteralPath $destination -Filter '*.msix' -File)
        if ($packages.Count -ne 1) { throw "Expected one $architecture MSIX in $destination." }
        if ($distribution -eq 'Store') { Write-StoreUpload $packages[0].FullName }
        & (Join-Path $PSScriptRoot 'verify-release-package.ps1') -Package $packages[0].FullName -Version $Version -Architecture $architecture -Store:($distribution -eq 'Store') -MicrosoftClientId $env:RodelMicrosoftClientId
        $hasPackage = $true
    }
    if ($IncludeValidationAssets -and $hasPackage) {
        $validation = Join-Path $output "$Version\$rid\validation"
        $cli = Join-Path $repository 'src\Rodel.MinecraftLauncher.Cli\Rodel.MinecraftLauncher.Cli.csproj'
        $tests = Join-Path $repository 'test\Rodel.MinecraftLauncher.Assistant.Tests\Rodel.MinecraftLauncher.Assistant.Tests.csproj'
        $command = "call `"$vcvars`" $vcArchitecture >nul && dotnet publish `"$cli`" -c Release -r $rid -o `"$validation\cli`" -p:EnableLocalInference=false -p:CleanBundledMods=false && dotnet publish `"$tests`" -c Release -r $rid --self-contained false -o `"$validation\tests`" -p:EnableLocalInference=false -p:CleanBundledMods=false"
        & $env:ComSpec /d /c $command
        if ($LASTEXITCODE -ne 0) { throw "Runtime validation asset build failed for $rid ($LASTEXITCODE)." }
    }
}
if ($failures.Count) { throw ($failures -join ' ') }
