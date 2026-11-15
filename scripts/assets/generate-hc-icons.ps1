param([string]$DevTools = 'artifacts/native-ui/devtools/Sprout.DevTools.exe')

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$icons = Join-Path $repository 'src/Rodel.MinecraftLauncher.App/Assets/Icons'
$assets = Join-Path $repository 'src/Rodel.MinecraftLauncher.App/Assets/AppIcons'
$output = Join-Path $repository 'artifacts/native-ui/icon/high-contrast'
$null = New-Item -ItemType Directory -Force $output
Add-Type -AssemblyName System.Drawing

# Solid monochrome silhouette with transparent face seams and play cutout; no background plate.
$outline = '512,64 928,244 928,768 512,960 96,768 96,244'
$ridges = @('96,244 512,424 928,244', '512,424 512,960')
$grass = @('96,348 236,408 236,478 364,534 364,464 512,528',
    '512,528 660,464 660,534 788,478 788,408 928,348')
$play = '254,578 434,714 254,814'
# The central cut removes the bottom apex; fit the remaining silhouette to all four canvas edges.
$visibleHeight = 960 - 64 - (24 * 192 / 416)
function Points([string]$text) {
    [System.Drawing.PointF[]]@($text.Split(' ') | ForEach-Object {
        $xy = $_.Split(','); [System.Drawing.PointF]::new([float]$xy[0], [float]$xy[1])
    })
}
foreach ($contrast in @('black', 'white')) {
    $ink = if ($contrast -eq 'black') { '#ffffff' } else { '#000000' }
    $name = "launcher-hc-$contrast"
    $cuts = (($ridges + $grass | ForEach-Object { "<polyline points=`"$_`"/>" }) -join '')
    $height = $visibleHeight.ToString('R', [Globalization.CultureInfo]::InvariantCulture)
    $svg = "<svg xmlns=`"http://www.w3.org/2000/svg`" width=`"1024`" height=`"1024`" viewBox=`"96 64 832 $height`" preserveAspectRatio=`"none`"><defs><mask id=`"cutouts`" maskUnits=`"userSpaceOnUse`" x=`"0`" y=`"0`" width=`"1024`" height=`"1024`"><rect width=`"1024`" height=`"1024`" fill=`"white`"/><g fill=`"none`" stroke=`"black`" stroke-width=`"48`" stroke-linejoin=`"miter`">$cuts</g><polygon points=`"$play`" fill=`"black`"/></mask></defs><polygon points=`"$outline`" fill=`"$ink`" mask=`"url(#cutouts)`"/></svg>"
    [IO.File]::WriteAllText((Join-Path $icons "$name.svg"), $svg, [Text.UTF8Encoding]::new($false))
    $bitmap = [Drawing.Bitmap]::new(1024, 1024, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $color = [Drawing.ColorTranslator]::FromHtml($ink)
    $pen = [Drawing.Pen]::new([Drawing.Color]::Transparent, 48)
    $brush = [Drawing.SolidBrush]::new($color)
    $cutout = [Drawing.SolidBrush]::new([Drawing.Color]::Transparent)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform((1024 / 832), (1024 / $visibleHeight))
        $graphics.TranslateTransform(-96, -64)
        $graphics.FillPolygon($brush, [Drawing.PointF[]](Points $outline))
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        foreach ($line in ($ridges + $grass)) { $graphics.DrawLines($pen, [Drawing.PointF[]](Points $line)) }
        $graphics.FillPolygon($cutout, [Drawing.PointF[]](Points $play))
        # GDI+ cutout antialiasing may mix RGB with transparent black. Keep straight-alpha edge ink monochrome.
        $data = $bitmap.LockBits([Drawing.Rectangle]::new(0, 0, 1024, 1024), [Drawing.Imaging.ImageLockMode]::ReadWrite, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $pixels = [byte[]]::new($data.Stride * $data.Height)
            [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
            for ($offset = 0; $offset -lt $pixels.Length; $offset += 4) {
                $channel = if ($pixels[$offset + 3] -gt 0) { $color.R } else { 0 }
                $pixels[$offset] = $channel
                $pixels[$offset + 1] = $channel
                $pixels[$offset + 2] = $channel
            }
            [Runtime.InteropServices.Marshal]::Copy($pixels, 0, $data.Scan0, $pixels.Length)
        }
        finally { $bitmap.UnlockBits($data) }
        $bitmap.Save((Join-Path $icons "$name.png"), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $cutout.Dispose(); $brush.Dispose(); $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    $variant = Join-Path $output $contrast
    & (Join-Path $repository $DevTools) generate assets (Join-Path $icons "$name.png") --out $variant --full --ico (Join-Path $icons "$name.ico") --artifacts (Join-Path $output "$contrast-generation") --json > (Join-Path $output "$contrast-result.json")
    if ($LASTEXITCODE -ne 0) { throw "HC asset generation failed: $contrast" }
    foreach ($file in Get-ChildItem -LiteralPath $variant -Filter '*.png') {
        $qualified = if ($file.BaseName.Contains('.')) { "$($file.BaseName)_contrast-$contrast.png" }
            else { "$($file.BaseName).contrast-$contrast.png" }
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $assets $qualified)
    }
}
$preview = [Drawing.Bitmap]::new(1024, 512)
$canvas = [Drawing.Graphics]::FromImage($preview)
$light = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 250, 239))
try {
    $canvas.Clear([Drawing.Color]::FromArgb(32, 32, 32))
    $canvas.FillRectangle($light, 512, 0, 512, 512)
    foreach ($contrast in @('black', 'white')) {
        $source = [Drawing.Image]::FromFile((Join-Path $icons "launcher-hc-$contrast.png"))
        try { $canvas.DrawImage($source, $(if ($contrast -eq 'black') { 0 } else { 512 }), 0, 512, 512) }
        finally { $source.Dispose() }
    }
    $preview.Save((Join-Path $output 'preview.png'), [Drawing.Imaging.ImageFormat]::Png)
}
finally { $light.Dispose(); $canvas.Dispose(); $preview.Dispose() }
Write-Output 'HC-ICONS-GENERATED (two transparent monochrome sources, 114 qualified PNGs, two multi-size ICOs)'
