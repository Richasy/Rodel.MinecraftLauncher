param([switch]$Check)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../src/Rodel.MinecraftLauncher.App'
Push-Location $project
try {
    [string[]]$verify = if ($Check) { @('--verify-no-changes') } else { @() }
    & dotnet format Rodel.MinecraftLauncher.App.csproj --no-restore --severity info @verify --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Native UI formatting check failed.' }
}
finally { Pop-Location }
