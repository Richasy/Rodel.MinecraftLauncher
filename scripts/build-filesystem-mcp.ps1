param(
    [Parameter(Mandatory)][string]$SourceArchive,
    [Parameter(Mandatory)][string]$OutputFile,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$RustToolchain
)

$ErrorActionPreference = 'Stop'
$SourceArchive = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($SourceArchive)
$OutputFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputFile)
if (-not (Get-Command rustup -ErrorAction SilentlyContinue)) {
    throw "ARM64 filesystem MCP requires Rust $RustToolchain and aarch64-pc-windows-msvc; install rustup on the build machine."
}
$build = Join-Path (Split-Path -Parent $OutputFile) ('filesystem-source-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $build -Force | Out-Null
$originalRustFlags = $env:CARGO_ENCODED_RUSTFLAGS
try {
    & tar.exe -xf $SourceArchive -C $build --strip-components=1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $build 'Cargo.lock'))) {
        throw 'The verified filesystem MCP source archive lacks its locked build.'
    }
    # Keep the player package independent of a machine-wide Visual C++ redistributable.
    $cargoHome = if ($env:CARGO_HOME) { $env:CARGO_HOME } else { Join-Path $env:USERPROFILE '.cargo' }
    $env:CARGO_ENCODED_RUSTFLAGS = @('-C', 'target-feature=+crt-static', '-C', 'link-arg=/Brepro',
        "--remap-path-prefix=$build=/_rodel/filesystem", "--remap-path-prefix=$cargoHome=/_cargo") -join [char]0x1f
    & rustup run $RustToolchain cargo build --manifest-path (Join-Path $build 'Cargo.toml') --locked --release --target aarch64-pc-windows-msvc --target-dir (Join-Path $build 'target')
    if ($LASTEXITCODE -ne 0) { throw "ARM64 filesystem MCP source build failed ($LASTEXITCODE)." }
    $temporary = $OutputFile + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        Copy-Item -LiteralPath (Join-Path $build 'target\aarch64-pc-windows-msvc\release\rust-mcp-filesystem.exe') -Destination $temporary
        Move-Item -LiteralPath $temporary -Destination $OutputFile -Force
    }
    finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
}
finally {
    $env:CARGO_ENCODED_RUSTFLAGS = $originalRustFlags
    Remove-Item -LiteralPath $build -Recurse -Force
}
