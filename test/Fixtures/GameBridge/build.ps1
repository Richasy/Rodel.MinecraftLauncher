param([Parameter(Mandatory)][string]$OutputDirectory, [string]$JavaHome, [string]$ObservationJavaHome)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../../scripts/resolve-build-jdk.ps1')
$jdkPath = Resolve-BuildJdk $JavaHome 17 'GameBridgeJavaHome'
$observationJdk = Resolve-BuildJdk $ObservationJavaHome 8 'GameObservationJavaHome' 8
$javaSources = Join-Path $PSScriptRoot '../../../java/game-bridge/src'
$javac = Join-Path $jdkPath 'bin/javac.exe'
$classesPath = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($classesPath) | Out-Null
$sources = @(Get-ChildItem -LiteralPath $javaSources -Recurse -Filter '*.java' | Sort-Object FullName | ForEach-Object FullName)
$sources += @(Get-ChildItem -LiteralPath (Join-Path $javaSources '../bootstrap') -Recurse -Filter '*.java' | Sort-Object FullName | ForEach-Object FullName)
$sources += @(Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -Filter '*.java' | Sort-Object FullName | ForEach-Object FullName)
& $javac --release 17 -encoding UTF-8 -d $classesPath @sources
if ($LASTEXITCODE -ne 0) { throw 'Java bridge regression compilation failed.' }
$optionalSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../GameKnowledge') -Recurse -Filter '*.java' | Sort-Object FullName | ForEach-Object FullName)
& $javac --release 17 -encoding UTF-8 -cp $classesPath -d (Join-Path $classesPath 'optional-mods') @optionalSources
if ($LASTEXITCODE -ne 0) { throw 'Optional knowledge API fixture compilation failed.' }
$java = Join-Path $jdkPath 'bin/java.exe'
[IO.File]::WriteAllText((Join-Path $classesPath 'java-path.txt'), $java)
$legacyClasses = Join-Path $classesPath 'legacy-fixture'
[IO.Directory]::CreateDirectory($legacyClasses) | Out-Null
$legacySources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../GameBridgeLegacy') -Recurse -Filter '*.java' | Sort-Object FullName | ForEach-Object FullName)
& (Join-Path $observationJdk 'bin/javac.exe') -source 6 -target 6 -Xlint:-options -encoding UTF-8 -d $legacyClasses @legacySources
if ($LASTEXITCODE -ne 0) { throw 'Java 6 observation fixture compilation failed.' }
& (Join-Path $jdkPath 'bin/jar.exe') --create --date=2020-01-01T00:00:00Z --file (Join-Path $classesPath 'legacy-fixture.jar') -C $legacyClasses .
if ($LASTEXITCODE -ne 0) { throw 'Java 6 observation fixture JAR creation failed.' }
