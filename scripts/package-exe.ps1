#requires -Version 7.5
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version,
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    [string]$OutputDirectory = 'artifacts\release',
    [string]$Compiler,
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$WindowsSdkVersion,
    [string]$JavaHome,
    [string]$LegacyJavaHome,
    [string]$ObservationJavaHome,
    [string]$NuGetSource = 'https://www.nuget.org/api/v2/'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'src\Rodel.MinecraftLauncher.App\Rodel.MinecraftLauncher.App.csproj'
if (-not $Version) {
    [xml]$definition = Get-Content -LiteralPath $project -Raw
    $Version = $definition.Project.PropertyGroup.SproutAppxVersion | Where-Object { $_ } | Select-Object -First 1
}
if (@($Version.Split('.') | Where-Object { [long]$_ -gt 65535 }).Count) {
    throw 'Version components must fit in UInt16.'
}
$clientId = [guid]::Empty
if (-not [guid]::TryParse($env:RODEL_MICROSOFT_CLIENT_ID, [ref]$clientId) -or $clientId -eq [guid]::Empty) {
    throw 'Release installers require RODEL_MICROSOFT_CLIENT_ID (the approved public OAuth application ID).'
}
if (-not $Compiler) {
    $candidates = @((Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'))
    $Compiler = $candidates | Where-Object { $_ -and [IO.File]::Exists($_) } | Select-Object -First 1
}
if (-not $Compiler -or -not [IO.File]::Exists($Compiler)) {
    throw 'Install Inno Setup 6.7 or 7 and pass -Compiler <path to ISCC.exe> if it is not discovered.'
}
$Compiler = [IO.Path]::GetFullPath($Compiler)
$output = Join-Path ($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)) "$Version\win-$Architecture\exe"
# A unique payload cannot pick up stale DLLs, packages or development files from an earlier publish.
$payload = Join-Path $output ('payload-' + [guid]::NewGuid().ToString('N'))
. (Join-Path $PSScriptRoot 'resolve-build-jdk.ps1')
$env:BundledModsJavaHome = Resolve-BuildJdk $JavaHome 25 'BundledModsJavaHome'
$env:BundledModsLegacyJavaHome = Resolve-BuildJdk $LegacyJavaHome 21 'BundledModsLegacyJavaHome' 24
$env:GameObservationJavaHome = Resolve-BuildJdk $ObservationJavaHome 8 'GameObservationJavaHome' 8
$env:RodelMicrosoftClientId = $clientId.ToString()
$env:RestoreSources = "$NuGetSource;https://pkgs.dev.azure.com/zrichasy/_packaging/sprout/nuget/v3/index.json"
$env:NuGetAudit = 'false'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$component = if ($Architecture -eq 'arm64') { 'ARM64' } else { 'x86.x64' }
$visualStudio = & $vswhere -latest -prerelease -products '*' -requires "Microsoft.VisualStudio.Component.VC.Tools.$component" -property installationPath
if (-not $visualStudio) { throw "NativeAOT requires the Visual Studio $Architecture C++ build tools." }
$vcvars = Join-Path $visualStudio 'VC\Auxiliary\Build\vcvarsall.bat'
$vcArchitecture = if ($Architecture -eq 'arm64') { 'x64_arm64' } else { 'x64' }
$env:PATH = (Split-Path -Parent $vswhere) + ';' + $env:PATH
$metadataArgument = if ($WindowsSdkVersion) { " -p:CsWinRTWindowsMetadata=$WindowsSdkVersion" } else { '' }
$command = "call `"$vcvars`" $vcArchitecture >nul && dotnet publish `"$project`" -c Release -r win-$Architecture -o `"$payload`" -p:Platform=AnyCPU -p:SproutPackageFormat=None -p:WindowsPackageType=None -p:SproutAppxVersion=$Version -p:EnableLocalInference=false -p:CopyOutputSymbolsToPublishDirectory=false$metadataArgument"
& $env:ComSpec /d /c $command
if ($LASTEXITCODE -ne 0) { throw "Unpackaged NativeAOT publish failed ($LASTEXITCODE)." }
foreach ($required in @('Rodel.MinecraftLauncher.App.exe', 'Assets\Backgrounds', 'Assets\BuiltInMods', 'Assets\AiMcp', 'Assets\Updates\install-update.ps1', 'Microsoft.WindowsAppRuntime.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $required))) { throw "Missing installer payload: $required" }
}
# Referenced projects can still copy their symbols even when the entry point's symbols are excluded.
foreach ($symbol in (Get-ChildItem -LiteralPath $payload -Recurse -File -Filter '*.pdb')) {
    if (-not $symbol.FullName.StartsWith($payload.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'A symbol path escaped the fresh installer payload.'
    }
    [IO.File]::Delete($symbol.FullName)
}
$forbidden = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Where-Object { $_.Extension -in '.pdb', '.pfx', '.resw', '.msix', '.msixupload' -or $_.Name -eq 'rodel-mc.exe' })
if ($forbidden.Count) { throw "Unexpected installer payload: $($forbidden.Name -join ', ')" }
$installerStage = Join-Path $output ('installer-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($installerStage) | Out-Null
& $Compiler "/DPayloadDirectory=$payload" "/DAppVersion=$Version" "/DAppArchitecture=$Architecture" "/O$installerStage" (Join-Path $PSScriptRoot 'installer\rmcl.iss')
if ($LASTEXITCODE -ne 0) { throw "EXE installer compilation failed ($LASTEXITCODE)." }
$installer = Join-Path $output "RMCL-$Version-win-$Architecture-setup.exe"
$stagedInstaller = Join-Path $installerStage ([IO.Path]::GetFileName($installer))
$hash = (Get-FileHash -LiteralPath $stagedInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
$stagedChecksum = Join-Path $installerStage ([IO.Path]::GetFileName($installer) + '.sha256')
[IO.File]::WriteAllText($stagedChecksum, "$hash  $([IO.Path]::GetFileName($installer))`n")
# Publish complete artifacts only; a failed build or replacement leaves the previous installer recoverable.
[IO.File]::Move($stagedInstaller, $installer, $true)
[IO.File]::Move($stagedChecksum, "$installer.sha256", $true)
[IO.Directory]::Delete($installerStage, $false)
Write-Output $installer
