param([Parameter(Mandatory)][string]$OutputDirectory, [string]$JavaHome)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'resolve-build-jdk.ps1')
$jdkPath = Resolve-BuildJdk $JavaHome 25 'SeedExplorerJavaHome'
$env:JAVA_HOME = $jdkPath
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
& (Join-Path $repository 'mods/rodel-ui/gradlew.bat') --project-dir (Join-Path $repository 'java/seed-explorer') jar --no-daemon --console=plain
if ($LASTEXITCODE -ne 0) { throw 'Seed explorer Java compilation failed.' }
$source = Join-Path $repository 'java/seed-explorer/build/libs/rodel-seed-explorer-26.3.jar'
$target = Join-Path $output 'rodel-seed-explorer-26.3.jar'
$temporary = $target + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
Copy-Item -LiteralPath $source -Destination $temporary
if ([IO.File]::Exists($target)) { [IO.File]::Replace($temporary, $target, [System.Management.Automation.Language.NullString]::Value) }
else { [IO.File]::Move($temporary, $target) }

$classes = Join-Path $output ('portable-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($classes) | Out-Null
try {
    $sources = @(Get-ChildItem -LiteralPath (Join-Path $repository 'java/seed-explorer/portable') -Recurse -Filter '*.java' | Sort-Object FullName | ForEach-Object FullName)
    $sources += Join-Path $repository 'java/game-bridge/src/io/github/richasy/rodel/bridge/Json.java'
    $sources += Join-Path $repository 'java/game-bridge/legacy/io/github/richasy/rodel/bridge/LegacyClassFile.java'
    & (Join-Path $jdkPath 'bin/javac.exe') --release 8 -Xlint:-options -encoding UTF-8 -d $classes @sources
    if ($LASTEXITCODE -ne 0) { throw 'Portable original seed worker compilation failed.' }
    $target = Join-Path $output 'rodel-seed-explorer-portable.jar'
    $temporary = $target + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    & (Join-Path $jdkPath 'bin/jar.exe') --create --date=2020-01-01T00:00:00Z --file $temporary -C $classes .
    if ($LASTEXITCODE -ne 0) { throw 'Portable original seed worker packaging failed.' }
    if ([IO.File]::Exists($target)) { [IO.File]::Replace($temporary, $target, [System.Management.Automation.Language.NullString]::Value) }
    else { [IO.File]::Move($temporary, $target) }
}
finally {
    $resolved = [IO.Path]::GetFullPath($classes)
    if (-not $resolved.StartsWith($output.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Portable class output escaped its build directory.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
