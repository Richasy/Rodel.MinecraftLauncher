param([switch]$Fix)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
Push-Location $repository
try {
    [string[]]$verify = if ($Fix) { @() } else { @('--verify-no-changes') }
    # Referenced submodules own their formatting rules; check only projects declared in this solution.
    [xml]$solution = Get-Content -LiteralPath (Join-Path $repository 'Rodel.MinecraftLauncher.slnx') -Raw
    foreach ($entry in $solution.SelectNodes('//Project')) {
        if ($entry.Path.Replace('\', '/').StartsWith('external/', [StringComparison]::OrdinalIgnoreCase)) { continue }
        $project = Join-Path $repository $entry.Path.Replace('/', '\')
        & dotnet format $project --no-restore --severity info @verify --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw "Code style does not match .editorconfig: $($entry.Path)" }
    }
}
finally { Pop-Location }
