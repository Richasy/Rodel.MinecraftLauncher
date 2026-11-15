param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$kitRoot = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits/10'
$compiler = Get-ChildItem -LiteralPath (Join-Path $kitRoot 'bin') -Directory |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64/fxc.exe' } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
if (-not $compiler) { throw 'Install the Windows SDK shader compiler (fxc.exe).' }
$version = Split-Path (Split-Path (Split-Path $compiler -Parent) -Parent) -Leaf
$include = Join-Path $kitRoot "Include/$version/um"
$source = Join-Path $PSScriptRoot '../src/Rodel.MinecraftLauncher.App/Assets/Shaders/WallpaperScene.hlsl'
New-Item -ItemType Directory -Path (Split-Path $OutputPath -Parent) -Force | Out-Null
& $compiler /nologo /T ps_4_0 /E Execute /D D2D_FULL_SHADER /D D2D_ENTRY=Execute /O3 /I $include /Fo $OutputPath $source
if ($LASTEXITCODE -ne 0) { throw 'Wallpaper shader compilation failed.' }

