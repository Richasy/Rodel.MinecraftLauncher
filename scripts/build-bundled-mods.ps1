param([Parameter(Mandatory)][string]$OutputDirectory, [string]$JavaHome, [string]$LegacyJavaHome, [switch]$Clean)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$version = (Get-Content (Join-Path $repository 'mods/gradle.properties') | Where-Object { $_ -match '^mod_version=' }) -replace '^mod_version=', ''
[xml]$targetConfig = Get-Content (Join-Path $repository 'mods/bundled-targets.props') -Raw
$artifacts = @($targetConfig.Project.ItemGroup.BundledModAsset | ForEach-Object { $_.Include -split ';' })
$targets = @($artifacts | ForEach-Object {
    if ($_ -notmatch '^(\d+(?:\.\d+)+)/(fabric|quilt|forge|neoforge)/(rodel-ai|world-reroll|bettertools|fabric-api)\.jar$') { throw 'Invalid bundled artifact registration.' }
    [pscustomobject]@{ MinecraftVersion = $Matches[1]; Loader = $Matches[2]; Module = $Matches[3] }
} | Group-Object { $_.MinecraftVersion + '/' + $_.Loader })
. (Join-Path $PSScriptRoot 'resolve-build-jdk.ps1')
$modernJdk = Resolve-BuildJdk $JavaHome 25 'BundledModsJavaHome'
$env:JAVA_HOME = $modernJdk
$gradleNetworkArguments = @('-Dorg.gradle.internal.http.connectionTimeout=15000', '-Dorg.gradle.internal.http.socketTimeout=15000')
[string[]]$cleanTasks = if ($Clean) { @('clean') } else { @() }
[string[]]$releaseArguments = if ($Clean) { @('--no-configuration-cache', '--no-build-cache') } else { @() }
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
function Publish-BundledArtifact([string]$Source, [string]$Target) {
    $temporary = $Target + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    Copy-Item -LiteralPath $Source -Destination $temporary
    if ([IO.File]::Exists($Target)) { [IO.File]::Replace($temporary, $Target, [System.Management.Automation.Language.NullString]::Value) }
    else { [IO.File]::Move($temporary, $Target) }
    [IO.File]::SetLastWriteTimeUtc($Target, [DateTime]::UtcNow)
}
function Build-FabricModule([string]$Module, [string]$GameTarget, [bool]$BundleApi, [string]$CommonLoader = 'fabric') {
    $project = Join-Path $repository "mods/$Module"
    Push-Location $project
    try {
        $archiveTask = 'assemble'
        [string[]]$tasks = if ($BundleApi) { @($archiveTask, 'bundleFabricApi') } else { @($archiveTask) }
        if ($Clean) {
            # Loom can capture old client class names while configuring assemble, before a same-invocation clean.
            & .\gradlew.bat clean "-Pminecraft_target=$GameTarget" "-Pbundled_loader=$CommonLoader" @releaseArguments @gradleNetworkArguments --no-daemon --console=plain
            if ($LASTEXITCODE -ne 0) { throw "Bundled mod clean failed: $Module ($GameTarget/Fabric)" }
        }
        & .\gradlew.bat @tasks "-Pminecraft_target=$GameTarget" "-Pbundled_loader=$CommonLoader" @releaseArguments @gradleNetworkArguments --no-daemon --console=plain
        if ($LASTEXITCODE -ne 0) { throw "Bundled mod build failed: $Module ($GameTarget/Fabric)" }
    } finally { Pop-Location }
}
function Get-FabricArchive([string]$Module, [string]$GameTarget) {
    $buildDirectory = if ($GameTarget -eq '26.3') { 'build' } else { "build/targets/$GameTarget" }
    $archiveName = if ($Module -eq 'world-reroll') { "worldreroll-$version.jar" } else { "$Module-$version.jar" }
    return Join-Path $repository "mods/$Module/$buildDirectory/libs/$archiveName"
}
foreach ($runtime in $targets) {
    $gameTarget = $runtime.Group[0].MinecraftVersion
    $loader = $runtime.Group[0].Loader
    if ($gameTarget -notmatch '^\d+(\.\d+)+$') { throw 'Invalid bundled Minecraft target.' }
    $targetDirectory = Join-Path $output "$gameTarget/$loader"
    [IO.Directory]::CreateDirectory($targetDirectory) | Out-Null
    $modules = @($runtime.Group.Module | Where-Object { $_ -ne 'fabric-api' })
    if ($loader -in @('fabric', 'quilt')) {
        # Quilt loads the audited Fabric compatibility package, including its declared API dependency.
        $apiModule = if ($modules -contains 'rodel-ai') { 'rodel-ai' } elseif ($modules -contains 'bettertools') { 'bettertools' } else { $modules[0] }
        foreach ($module in $modules) {
            Build-FabricModule $module $gameTarget ($module -eq $apiModule)
            Publish-BundledArtifact (Get-FabricArchive $module $gameTarget) (Join-Path $targetDirectory "$module.jar")
        }
        if ($runtime.Group.Module -contains 'fabric-api') {
            $buildDirectory = if ($gameTarget -eq '26.3') { 'build' } else { "build/targets/$gameTarget" }
            Publish-BundledArtifact (Join-Path $repository "mods/$apiModule/$buildDirectory/bundled-dependencies/fabric-api.jar") (Join-Path $targetDirectory 'fabric-api.jar')
        }
    } elseif ($gameTarget -in @('26.3', '26.2', '26.1.2', '1.21.11') -and $loader -in @('forge', 'neoforge')) {
        # Native loader projects package the shared Minecraft implementation with their own lifecycle bindings.
        $commonLoader = if ($loader -eq 'forge' -or $gameTarget -eq '1.21.11') { 'forge' } else { 'fabric' }
        $nativeModules = if ($gameTarget -eq '26.3') { @('rodel-ai', 'world-reroll', 'bettertools') } else { @() }
        if (@($modules | Where-Object { $_ -notin $nativeModules }).Count -gt 0) { throw "Unimplemented native module registration: $gameTarget/$loader" }
        foreach ($module in $modules) { Build-FabricModule $module $gameTarget $false $commonLoader }
        & (Join-Path $repository 'mods/rodel-ai/gradlew.bat') -p (Join-Path $repository "mods/loader-build/$loader") @cleanTasks assemble "-Pminecraft_target=$gameTarget" "-Pbundled_modules=$($modules -join ',')" @releaseArguments @gradleNetworkArguments --no-daemon --console=plain
        if ($LASTEXITCODE -ne 0) { throw "Bundled mod build failed: $gameTarget/$loader" }
        foreach ($module in $modules) {
            $nativeBuildDirectory = if ($gameTarget -eq '26.3') { 'build' } else { "build/targets/$gameTarget" }
            Publish-BundledArtifact (Join-Path $repository "mods/loader-build/$loader/$nativeBuildDirectory/libs/$module-$version.jar") (Join-Path $targetDirectory "$module.jar")
        }
    } elseif ($gameTarget -eq '1.20.1' -and $loader -eq 'forge' -and $modules.Count -eq 1 -and $modules[0] -eq 'rodel-ai') {
        # ForgeGradle 6 uses Gradle 8/JDK 21. Reuse the tracked wrapper implementation without another binary in source.
        $wrapperDirectory = Join-Path $repository '.gradle/bundled-gradle8'
        [IO.Directory]::CreateDirectory($wrapperDirectory) | Out-Null
        Copy-Item -LiteralPath (Join-Path $repository 'mods/rodel-ai/gradle/wrapper/gradle-wrapper.jar') -Destination (Join-Path $wrapperDirectory 'gradle-wrapper.jar') -Force
        Copy-Item -LiteralPath (Join-Path $repository 'mods/rodel-ai/forge/gradle-wrapper.properties') -Destination (Join-Path $wrapperDirectory 'gradle-wrapper.properties') -Force
        $env:JAVA_HOME = Resolve-BuildJdk $LegacyJavaHome 21 'BundledModsLegacyJavaHome' 24
        try {
            & (Join-Path $env:JAVA_HOME 'bin/java.exe') -classpath (Join-Path $wrapperDirectory 'gradle-wrapper.jar') org.gradle.wrapper.GradleWrapperMain -p (Join-Path $repository 'mods/rodel-ai/forge') @cleanTasks assemble @releaseArguments @gradleNetworkArguments --no-daemon --console=plain
            if ($LASTEXITCODE -ne 0) { throw 'Bundled mod build failed: 1.20.1/forge' }
        } finally { $env:JAVA_HOME = $modernJdk }
        Publish-BundledArtifact (Join-Path $repository "mods/rodel-ai/forge/build/libs/rodel-ai-$version.jar") (Join-Path $targetDirectory 'rodel-ai.jar')
    } else {
        throw "No source build is implemented for registered bundled target: $gameTarget/$loader"
    }
}
$manifest = Join-Path $output 'catalog.json'
$temporaryManifest = $manifest + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
[IO.File]::WriteAllText($temporaryManifest, (@{ targets = @($targets | ForEach-Object {
    @{ minecraftVersion = $_.Group[0].MinecraftVersion; loader = $_.Group[0].Loader; modules = @($_.Group.Module) }
}) } | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
if ([IO.File]::Exists($manifest)) { [IO.File]::Replace($temporaryManifest, $manifest, [System.Management.Automation.Language.NullString]::Value) }
else { [IO.File]::Move($temporaryManifest, $manifest) }
