param()

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$issues = [Collections.Generic.List[string]]::new()

function Read-ResourceMap([string]$Path) {
    if (-not [IO.File]::Exists($Path)) { throw "Missing language resource: $Path" }
    [xml]$document = [IO.File]::ReadAllText($Path)
    $map = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $document.root.data) {
        if (-not $map.TryAdd($entry.name, [string]$entry.value)) { throw "Duplicate resource $($entry.name): $Path" }
    }
    return ,$map
}

function Check-ResourceMap([string]$Path, $Source) {
    $map = Read-ResourceMap $Path
    foreach ($key in $Source.Keys) {
        if (-not $map.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($map[$key])) {
            $issues.Add("${Path}: missing or empty $key")
            continue
        }
        # Preserve format tokens (including alignment/format) and executable option names, not translated prose.
        foreach ($pattern in @('(?<!\{)\{\d+(?:,[^{}]+)?(?::[^{}]+)?\}(?!\})', '--[a-z][a-z0-9-]*', '(?<![\w-])--(?![a-zA-Z0-9-])')) {
            $expected = (@([regex]::Matches($Source[$key], $pattern).Value) | Sort-Object) -join '|'
            $actual = (@([regex]::Matches($map[$key], $pattern).Value) | Sort-Object) -join '|'
            if ($expected -cne $actual) { $issues.Add("${Path}: token mismatch in $key ($expected / $actual)") }
        }
    }
    foreach ($key in $map.Keys) {
        if (-not $Source.ContainsKey($key)) { $issues.Add("${Path}: unknown key $key") }
    }
    Write-Verbose "$Path : $($map.Count) keys"
}

$catalog = [IO.File]::ReadAllText((Join-Path $repository 'src/Rodel.MinecraftLauncher.Core/Application/LauncherLanguages.cs'))
$declaration = [regex]::Match($catalog, 'Array.AsReadOnly<string>\(\s*\[([^\]]+)\]').Groups[1].Value
$languages = @([regex]::Matches($declaration, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
if ($languages.Count -ne 15) { throw 'The launcher must declare the agreed fifteen languages.' }
$app = Join-Path $repository 'src/Rodel.MinecraftLauncher.App/Strings'
$cli = Join-Path $repository 'src/Rodel.MinecraftLauncher.Cli/Strings'
$appSource = Read-ResourceMap (Join-Path $app 'en/Resources.resw')
$cliSource = Read-ResourceMap (Join-Path $cli 'CliStrings.resx')
$aiSource = Read-ResourceMap (Join-Path $cli 'AssistantStrings.resx')
foreach ($language in $languages) {
    Check-ResourceMap (Join-Path $app "$language/Resources.resw") $appSource
    $suffix = if ($language -eq 'en') { '' } else { ".$language" }
    Check-ResourceMap (Join-Path $cli "CliStrings$suffix.resx") $cliSource
    Check-ResourceMap (Join-Path $cli "AssistantStrings$suffix.resx") $aiSource
}
if ($issues.Count) { throw ($issues -join [Environment]::NewLine) }
Write-Host 'All fifteen App and CLI languages have matching keys, format tokens and command options.'
