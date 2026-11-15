// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Skins;
using StbImageWriteSharp;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects original image samples, actual WIC extraction and the shared navigation transform; no desktop input is injected.</summary>
[TestClass]
public sealed class SkinPaletteReferenceTests
{
    /// <summary>
    /// The reference decoder must not reuse the avatar's reduced preview or sample premultiplied BGRA.
    /// An independently encoded HD PNG has isolated, literal RGBA markers at the far edge and inside
    /// the image. Reading them after creating the presentation bitmap detects resizing, channel swaps,
    /// lost alpha and destructive display conversion without testing the skin-atlas import path.
    /// </summary>
    [TestMethod]
    public async Task DecodeAndPreview_KeepFullResolutionStraightRgbaForSampling()
    {
        var root = Directory.CreateTempSubdirectory("rodel-palette-reference-").FullName;
        try
        {
            const int width = 1920, height = 1080;
            var pixels = new byte[width * height * 4];
            for (var index = 0; index < pixels.Length; index += 4)
                pixels.AsSpan(index, 4).Fill(255);
            byte[] edge = [49, 199, 131, 128];
            edge.CopyTo(pixels, pixels.Length - 4);
            byte[] inner = [23, 45, 67, 19];
            inner.CopyTo(pixels, ((50 * width) + 100) * 4);
            var path = Path.Combine(root, "reference.png");
            using (var output = File.Create(path))
                new ImageWriter().WritePng(pixels, width, height, ColorComponents.RedGreenBlueAlpha, output);

            var image = await SkinPaletteImage.ReadAsync(path, CancellationToken.None);
            Assert.AreEqual(width, image.Width);
            Assert.AreEqual(height, image.Height);
            Assert.IsTrue(image.CreateSource().TryGetDecodedPixels(out var preview));
            Assert.IsNotNull(preview);
            CollectionAssert.AreEqual(new byte[] { 66, 100, 25, 128 }, preview.Pixels[^4..]);
            Assert.AreEqual("#31C78380", image.Sample(1919, 1079));
            Assert.AreEqual("#172D4313", image.Sample(100, 50));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Transparent RGB must not add invisible swatches, and a simple image must not be padded to 16.
    /// The literal source bands are an independent oracle; real WIC is used, not a quantizer double.
    /// The all-transparent variant also protects the usable empty draft rather than a fabricated black color.
    /// </summary>
    [TestMethod]
    public void Extraction_IgnoresTransparentRgbAndReturnsOnlyAvailableColors()
    {
        var pixels = new byte[64 * 4];
        byte[][] colors = [[255, 0, 0, 255], [0, 0, 255, 255], [255, 255, 255, 128], [0, 255, 0, 0]];
        for (var index = 0; index < 64; index++)
            colors[index % colors.Length].CopyTo(pixels, index * 4);
        var image = new SkinPaletteImage(64, 1, pixels);
        CollectionAssert.AreEquivalent(ExpectedFlatColors, SkinPaletteQuantizer.Extract(image, TestContext.CancellationToken));
        for (var index = 3; index < pixels.Length; index += 4)
            pixels[index] = 0;
        Assert.IsEmpty(SkinPaletteQuantizer.Extract(image, TestContext.CancellationToken));
    }

    /// <summary>
    /// A multicolor reference must go through bounded quantization, not first-N distinct colors.
    /// Both warm and cool halves must remain represented, and extraction must leave exact sampling intact.
    /// This complements the few-color case with a real native median-cut run and its 16-color ceiling.
    /// </summary>
    [TestMethod]
    public void Extraction_ComplexReferenceKeepsBothColorFamiliesWithinSixteenSwatches()
    {
        var pixels = new byte[512 * 4];
        for (var index = 0; index < 512; index++)
        {
            pixels[index * 4] = index < 256 ? (byte)255 : (byte)0;
            pixels[(index * 4) + 1] = (byte)(index % 256);
            pixels[(index * 4) + 2] = index < 256 ? (byte)0 : (byte)255;
            pixels[(index * 4) + 3] = 255;
        }

        var image = new SkinPaletteImage(512, 1, pixels);
        var colors = SkinPaletteQuantizer.Extract(image, TestContext.CancellationToken);
        Assert.IsTrue(colors.Length is > 1 and <= 16);
        Assert.AreEqual(colors.Length, colors.Distinct().Count());
        Assert.Contains(color => Convert.ToByte(color.Substring(1, 2), 16) > 200 && Convert.ToByte(color.Substring(5, 2), 16) < 30, colors, string.Join(", ", colors));
        Assert.Contains(color => Convert.ToByte(color.Substring(5, 2), 16) > 200 && Convert.ToByte(color.Substring(1, 2), 16) < 30, colors, string.Join(", ", colors));
        Assert.IsTrue(colors.All(color => (color.StartsWith("#FF", StringComparison.Ordinal) && color.AsSpan(5, 2).SequenceEqual("00"))
            || (color.StartsWith("#00", StringComparison.Ordinal) && color.AsSpan(5, 2).SequenceEqual("FF"))),
            "Empty WIC box centers must not invent colors absent from the reference.");
        Assert.AreEqual("#FF7B00FF", image.Sample(123, 0));
        Assert.AreEqual("#007BFFFF", image.Sample(379, 0));
    }

    /// <summary>
    /// Fixed-grid sampling can miss every nontransparent pixel, or one complete color family, in
    /// ordinary periodic pixel art larger than the sampling budget. Both variants use real image
    /// pixels and WIC; the exact source stripes are the oracle, not the reservoir's chosen indices.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Extraction_PeriodicImageDoesNotLoseVisibleColors(bool transparentAlternate)
    {
        const int side = 512;
        var pixels = new byte[side * side * 4];
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var offset = ((y * side) + x) * 4;
                var red = x % 2 == 0;
                pixels[offset] = red ? (byte)255 : (byte)0;
                pixels[offset + 2] = red ? (byte)0 : (byte)255;
                pixels[offset + 3] = !red && transparentAlternate ? (byte)0 : (byte)255;
            }
        }

        var colors = SkinPaletteQuantizer.Extract(new(side, side, pixels), TestContext.CancellationToken);
        CollectionAssert.AreEquivalent(transparentAlternate ? ExpectedOpaqueStripeColors : ["#FF0000FF", "#0000FFFF"], colors);
    }

    /// <summary>
    /// Nearly identical background grays waste swatches, but equally bright skin tints are not gray.
    /// Unequal literal source populations require the dominant real gray, not an averaged new color.
    /// Existing extraction cases cover exact duplicates only; this catches RGB-only equality and
    /// perceptual merging that erases the distinct warm skin color.
    /// </summary>
    [TestMethod]
    public void Extraction_MergesCloseGraysButPreservesSkinTintsAndDominantSourceColor()
    {
        var image = ColorBands(
            ("#BCBEC4", 20),
            ("#E8D7CD", 40),
            ("#C4A294", 40),
            ("#ADA7A2", 60),
            ("#121919", 40),
            ("#C5CECE", 80));

        CollectionAssert.AreEquivalent(
            ExpectedMergedColors,
            SkinPaletteQuantizer.Extract(image, TestContext.CancellationToken));
        Assert.AreEqual("#BCBEC4FF", image.Sample(0, 0), "Palette cleanup must not change exact reference pixels.");
    }

    /// <summary>
    /// An unordered set of neutral, warm, green and blue tones must become consistent lightness ramps.
    /// The literal order is derived from those independent source families, not from quantizer output;
    /// reversing the input bands catches accidental dependence on WIC's palette or scan order.
    /// </summary>
    [TestMethod]
    public void Extraction_OrdersColorFamiliesAndShadesIndependentlyOfSourceOrder()
    {
        (string Color, int Count)[] bands =
        [
            ("#8EBCE5", 32),
            ("#E4A099", 32),
            ("#244C28", 32),
            ("#888888", 32),
            ("#15334F", 32),
            ("#9BD6A4", 32),
            ("#402020", 32),
        ];
        string[] expected = ["#888888FF", "#402020FF", "#E4A099FF", "#244C28FF", "#9BD6A4FF", "#15334FFF", "#8EBCE5FF"];
        CollectionAssert.AreEqual(expected, SkinPaletteQuantizer.Extract(ColorBands(bands), TestContext.CancellationToken));
        Array.Reverse(bands);
        CollectionAssert.AreEqual(expected, SkinPaletteQuantizer.Extract(ColorBands(bands), TestContext.CancellationToken));
    }

    /// <summary>
    /// Fit leaves 25 units of vertical letterbox in this independently chosen 1000x500/500x300 case.
    /// Cursor-anchored zoom and an explicit pan must keep the known source cell under the corresponding
    /// pointer, while letterbox and exclusive outer edges never clamp into a false image sample.
    /// </summary>
    [TestMethod]
    public void Navigation_ZoomPanAndResetShareTheOriginalPixelMapping()
    {
        var view = new SkinReferenceTransform(1000, 500, 500, 300);
        Assert.IsFalse(view.TryPixel(10, 10, out _, out _));
        Assert.IsFalse(view.TryPixel(500, 150, out _, out _));
        Assert.IsTrue(view.TryPixel(100.25f, 75.25f, out var x, out var y));
        Assert.AreEqual((200, 100), (x, y));

        view = view.ZoomAt(2, 100.25f, 75.25f);
        Assert.IsTrue(view.TryPixel(100.25f, 75.25f, out x, out y));
        Assert.AreEqual((200, 100), (x, y));
        view = view.Move(50, -25);
        Assert.IsTrue(view.TryPixel(150.25f, 50.25f, out x, out y));
        Assert.AreEqual((200, 100), (x, y));
        view = view.Reset();
        Assert.IsTrue(view.TryPixel(499.75f, 274.75f, out x, out y));
        Assert.AreEqual((999, 499), (x, y));
        Assert.IsFalse(view.TryPixel(250, 275, out _, out _));
    }

    private static SkinPaletteImage ColorBands(params (string Color, int Count)[] bands)
    {
        var pixels = new byte[bands.Sum(band => band.Count) * 4];
        var offset = 0;
        foreach (var (color, count) in bands)
        {
            var rgb = Convert.FromHexString(color[1..]);
            for (var index = 0; index < count; index++, offset += 4)
            {
                rgb.CopyTo(pixels, offset);
                pixels[offset + 3] = 255;
            }
        }

        return new(pixels.Length / 4, 1, pixels);
    }

    /// <summary>Receives the current test's cancellation lifetime from MSTest for native palette extraction.</summary>
    public TestContext TestContext { get; set; }

    private static readonly string[] ExpectedMergedColors = ["#121919FF", "#ADA7A2FF", "#C5CECEFF", "#C4A294FF", "#E8D7CDFF"];
    private static readonly string[] ExpectedOpaqueStripeColors = ["#FF0000FF"];
    private static readonly string[] ExpectedFlatColors = ["#FF0000FF", "#0000FFFF", "#FFFFFFFF"];
}