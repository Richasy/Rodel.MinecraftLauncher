function Resolve-BuildJdk([string]$RequestedHome, [int]$MinimumVersion, [string]$ConfigurationProperty, [int]$MaximumVersion = [int]::MaxValue) {
    $candidates = if ($RequestedHome) { @($RequestedHome) } else {
        @($env:JAVA_HOME) +
            @(Get-Command javac -All -ErrorAction SilentlyContinue | ForEach-Object { Split-Path -Parent (Split-Path -Parent $_.Source) }) +
            @(foreach ($root in @(
                (Join-Path $env:USERPROFILE '.jdks'),
                (Join-Path $env:ProgramFiles 'Java'),
                (Join-Path $env:ProgramFiles 'Eclipse Adoptium'),
                (Join-Path $env:ProgramFiles 'Microsoft')
            )) {
                if (Test-Path -LiteralPath $root -PathType Container) {
                    Get-ChildItem -LiteralPath $root -Directory | Select-Object -ExpandProperty FullName
                }
            })
    }
    foreach ($candidate in $candidates) {
        if (-not $candidate) { continue }
        $javacPath = Join-Path $candidate 'bin/javac.exe'
        $jarPath = Join-Path $candidate 'bin/jar.exe'
        if (-not ([IO.File]::Exists($javacPath) -and [IO.File]::Exists($jarPath))) { continue }
        $releasePath = Join-Path $candidate 'release'
        if (-not [IO.File]::Exists($releasePath)) { continue }
        $release = [IO.File]::ReadAllText($releasePath)
        $majorVersion = if ($release -match '(?m)^JAVA_VERSION="1\.(\d+)(?:[."]|_)') { [int]$Matches[1] }
            elseif ($release -match '(?m)^JAVA_VERSION="(\d+)(?:[."]|-)') { [int]$Matches[1] }
            else { 0 }
        if ($majorVersion -ge $MinimumVersion -and $majorVersion -le $MaximumVersion) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }
    $requested = if ($RequestedHome) { " Requested directory: $RequestedHome." } else { '' }
    $versionRange = if ($MaximumVersion -eq [int]::MaxValue) { "$MinimumVersion or newer" } else { "$MinimumVersion through $MaximumVersion" }
    throw "Building requires JDK $versionRange with javac and jar. Set $ConfigurationProperty or JAVA_HOME to its JDK directory.$requested"
}
