param([Parameter(Mandatory)][string]$OutputDirectory, [string]$JavaHome, [string]$ObservationJavaHome)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'resolve-build-jdk.ps1')
$jdkPath = Resolve-BuildJdk $JavaHome 17 'GameBridgeJavaHome'
$observationJdk = Resolve-BuildJdk $ObservationJavaHome 8 'GameObservationJavaHome' 8
$sourceDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'java/game-bridge'
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($outputPath) | Out-Null
$javac = Join-Path $jdkPath 'bin/javac.exe'
$jar = Join-Path $jdkPath 'bin/jar.exe'
$classesPath = Join-Path $outputPath ('classes-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($classesPath) | Out-Null
try {
$sources = @(Get-ChildItem -LiteralPath (Join-Path $sourceDirectory 'src') -Recurse -Filter '*.java' | Sort-Object FullName | ForEach-Object FullName)
$sources += @(Get-ChildItem -LiteralPath (Join-Path $sourceDirectory 'bootstrap') -Recurse -Filter '*.java' | Sort-Object FullName | ForEach-Object FullName)
& $javac --release 17 -encoding UTF-8 -d $classesPath @sources
if ($LASTEXITCODE -ne 0) { throw 'Game bridge Java compilation failed.' }
$hookClass = 'io/github/richasy/rodel/input/KeyHooks.class'
& $jar --create --date=2020-01-01T00:00:00Z --file (Join-Path $classesPath 'rodel-input-hooks.jar') -C $classesPath $hookClass
if ($LASTEXITCODE -ne 0) { throw 'Game bridge bootstrap JAR creation failed.' }
Remove-Item -LiteralPath (Join-Path $classesPath $hookClass)
$temporaryJar = Join-Path $outputPath 'rodel-game-bridge.new.jar'
& $jar --create --date=2020-01-01T00:00:00Z --file $temporaryJar --manifest (Join-Path $sourceDirectory 'agent.mf') -C $classesPath .
if ($LASTEXITCODE -ne 0) { throw 'Game bridge JAR creation failed.' }
$jarPath = Join-Path $outputPath 'rodel-game-bridge.jar'
if ([IO.File]::Exists($jarPath)) { [IO.File]::Replace($temporaryJar, $jarPath, [System.Management.Automation.Language.NullString]::Value) }
else { [IO.File]::Move($temporaryJar, $jarPath) }

$legacyClassesPath = Join-Path $classesPath 'legacy'
[IO.Directory]::CreateDirectory($legacyClassesPath) | Out-Null
$legacySources = @(Get-ChildItem -LiteralPath (Join-Path $sourceDirectory 'legacy') -Recurse -Filter '*.java' | Sort-Object FullName | ForEach-Object FullName)
$legacySources += @(Get-ChildItem -LiteralPath (Join-Path $sourceDirectory 'legacy-bootstrap') -Recurse -Filter '*.java' | Sort-Object FullName | ForEach-Object FullName)
foreach ($sharedSource in @('Json.java', 'Wire.java', 'BridgeAuthentication.java')) {
    $legacySources += Join-Path $sourceDirectory ('src/io/github/richasy/rodel/bridge/' + $sharedSource)
}
& (Join-Path $observationJdk 'bin/javac.exe') -source 6 -target 6 -Xlint:-options -encoding UTF-8 -d $legacyClassesPath @legacySources
if ($LASTEXITCODE -ne 0) { throw 'Java 6 observation compilation failed.' }
$legacyHook = 'io/github/richasy/rodel/input/LegacyHooks.class'
& $jar --create --date=2020-01-01T00:00:00Z --file (Join-Path $legacyClassesPath 'rodel-input-hooks.jar') -C $legacyClassesPath $legacyHook
if ($LASTEXITCODE -ne 0) { throw 'Java 6 observation bootstrap JAR creation failed.' }
Remove-Item -LiteralPath (Join-Path $legacyClassesPath $legacyHook)
$temporaryLegacyJar = Join-Path $outputPath 'rodel-game-observation.new.jar'
& $jar --create --date=2020-01-01T00:00:00Z --file $temporaryLegacyJar --manifest (Join-Path $sourceDirectory 'legacy-agent.mf') -C $legacyClassesPath .
if ($LASTEXITCODE -ne 0) { throw 'Java 6 observation JAR creation failed.' }
$legacyJar = Join-Path $outputPath 'rodel-game-observation.jar'
if ([IO.File]::Exists($legacyJar)) { [IO.File]::Replace($temporaryLegacyJar, $legacyJar, [System.Management.Automation.Language.NullString]::Value) }
else { [IO.File]::Move($temporaryLegacyJar, $legacyJar) }
}
finally {
    $resolvedClassesPath = [IO.Path]::GetFullPath($classesPath)
    if (-not $resolvedClassesPath.StartsWith($outputPath.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The generated class directory escaped its build output directory.'
    }
    Remove-Item -LiteralPath $resolvedClassesPath -Recurse -Force
}
