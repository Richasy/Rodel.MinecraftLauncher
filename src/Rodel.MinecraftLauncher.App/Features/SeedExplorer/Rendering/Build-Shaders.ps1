param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
$compiler = Get-ChildItem -LiteralPath $sdkBin -Directory | Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64/fxc.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$compiler) { throw 'Install the Windows SDK shader compiler (fxc.exe) to build the original block preview.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($stage in @('VS', 'PS')) {
    & $compiler /nologo /O3 /T ($stage.ToLowerInvariant() + '_5_0') /E $stage /Fo (Join-Path $OutputDirectory "$stage.cso") (Join-Path $PSScriptRoot 'SeedScene.hlsl')
    if ($LASTEXITCODE) { throw "Original block $stage shader compilation failed." }
}
