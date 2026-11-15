param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
$compiler = Get-ChildItem $sdkBin -Directory | Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'x64/fxc.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$compiler) { throw 'Install the Windows SDK shader compiler (fxc.exe) to build the native skin viewport.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($stage in @('VS', 'PS')) {
    & $compiler /nologo /O3 /T ($stage.ToLowerInvariant() + '_5_0') /E $stage /Fo (Join-Path $OutputDirectory "$stage.cso") (Join-Path $PSScriptRoot 'SkinViewport.hlsl')
    if ($LASTEXITCODE) { throw "Skin $stage shader compilation failed." }
}
& $compiler /nologo /O3 /T ps_5_0 /E Resolve /Fo (Join-Path $OutputDirectory 'Resolve.cso') (Join-Path $PSScriptRoot 'SkinViewport.hlsl')
if ($LASTEXITCODE) { throw 'Skin resolve shader compilation failed.' }
$sdkVersion = Split-Path (Split-Path $compiler -Parent) -Parent | Split-Path -Leaf
$shaderIncludes = Join-Path ${env:ProgramFiles(x86)} "Windows Kits/10/Include/$sdkVersion/um"
& $compiler /nologo /O3 /T ps_5_0 /D SKIN_PREVIEW_D2D=1 /D D2D_ENTRY=Preview /I $shaderIncludes /E Preview /Fo (Join-Path $OutputDirectory 'Preview.cso') (Join-Path $PSScriptRoot 'SkinViewport.hlsl')
if ($LASTEXITCODE) { throw 'Skin popup shader compilation failed.' }
