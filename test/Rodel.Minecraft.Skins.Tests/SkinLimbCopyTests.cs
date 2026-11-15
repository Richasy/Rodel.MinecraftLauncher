// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Protects whole-limb mirror mapping and flattened, one-layer replacement rather than clipboard placement.</summary>
[TestClass]
public sealed class SkinLimbCopyTests
{
    // Author review: existing face clipboard cases do not check limb pairing, shell visibility or
    // composite-to-one-layer replacement. Asymmetric corner colors and a sentinel destination
    // distinguish wrong faces, missing flips, source-over blending and hidden-layer leakage.
    /// <summary>All six faces reflect correctly at both arm widths and resolutions, without copying hidden shells or layer structure.</summary>
    [TestMethod]
    [DataRow(SkinModel.Classic, 64, SkinPart.RightArm, SkinPart.LeftArm, true)]
    [DataRow(SkinModel.Slim, 128, SkinPart.LeftArm, SkinPart.RightArm, false)]
    [DataRow(SkinModel.Classic, 128, SkinPart.RightLeg, SkinPart.LeftLeg, true)]
    [DataRow(SkinModel.Slim, 64, SkinPart.LeftLeg, SkinPart.RightLeg, false)]
    public void CopyToOpposite_FlattensVisiblePixelsAndReplacesOnlyTheSelectedLayer(
        SkinModel model, int size, SkinPart source, SkinPart target, bool includeOuter)
    {
        var document = SkinEditor.Create(model, "#0000FF", size);
        var paint = new uint[size * size];
        var visible = new uint[paint.Length];
        var hidden = new uint[paint.Length];
        uint[] colors = [0xFF0000FF, 0x00FF00FF, 0x0000FFFF, 0x00FFFFFF, 0xFF00FFFF, 0xFFFF00FF];
        foreach (var face in SkinLayout.Faces(model, size))
        {
            if (face.Surface.Part == target)
                FillFace(paint, face, size, 0x11223388);
            if (face.Surface.Part != source)
                continue;
            visible[Index(face, size, 0, 1)] = colors[(int)face.Surface.Face];
            visible[Index(face, size, face.Width - 1, face.Height - 2)] = 0xFFFFFFFF;
            FillFace(hidden, face, size, 0x123456FF);
        }

        document = document with
        {
            Layers = [
                new("paint", "Selected", paint),
                new("visible", "Half opacity", visible, Opacity: .5),
                new("hidden", "Hidden", hidden, Visible: false),
            ],
        };
        var expected = (uint[])paint.Clone();
        uint[] baseColors = [0x800080FF, 0x008080FF, 0x0000FFFF, 0x0080FFFF, 0x8000FFFF, 0x808080FF];
        // Literal face pairs are independent of the production mirror resolver.
        (SkinFace Source, SkinFace Target)[] pairs = [
            (SkinFace.Front, SkinFace.Front), (SkinFace.Back, SkinFace.Back),
            (SkinFace.Right, SkinFace.Left), (SkinFace.Left, SkinFace.Right),
            (SkinFace.Top, SkinFace.Top), (SkinFace.Bottom, SkinFace.Bottom),
        ];
        foreach (var shell in includeOuter ? new[] { SkinLayer.Base, SkinLayer.Outer } : [SkinLayer.Base])
        {
            foreach (var pair in pairs)
            {
                var face = SkinLayout.Face(model, new() { Part = target, Layer = shell, Face = pair.Target }, size);
                FillFace(expected, face, size, shell == SkinLayer.Base ? 0x0000FFFFu : 0u);
                expected[Index(face, size, face.Width - 1, 1)] = shell == SkinLayer.Base
                    ? baseColors[(int)pair.Source] : (colors[(int)pair.Source] & 0xFFFFFF00) | 0x80;
                expected[Index(face, size, 0, face.Height - 2)] = shell == SkinLayer.Base ? 0x8080FFFF : 0xFFFFFF80;
            }
        }

        var copied = SkinEditor.CopyPartToOpposite(document, source, document.Revision, includeOuter, "paint", TestContext.CancellationToken);

        CollectionAssert.AreEqual(expected, copied.Layers[0].Pixels, "Every other texel, including the source limb and a hidden outer shell, must stay unchanged.");
        CollectionAssert.AreEqual(paint, document.Layers[0].Pixels, "Copy must not mutate its input snapshot.");
        CollectionAssert.AreEqual(document.Pixels, copied.Pixels);
        CollectionAssert.AreEqual(visible, copied.Layers[1].Pixels);
        CollectionAssert.AreEqual(hidden, copied.Layers[2].Pixels);
        CollectionAssert.AreEqual(document.Layers.Select(layer => layer.Id).ToArray(), copied.Layers.Select(layer => layer.Id).ToArray());
        Assert.AreEqual(.5, copied.Layers[1].Opacity);
        Assert.IsFalse(copied.Layers[2].Visible);
    }

    // A masked source exposes the protected background-alpha boundary after earlier faces may
    // already have been staged. Rejection must preserve the entire source, not a partly copied limb.
    /// <summary>A transparent visible pixel cannot silently damage the protected background or leave a partial copy.</summary>
    [TestMethod]
    public void CopyToBackground_WithMaskedSource_RejectsWithoutPartialChanges()
    {
        var source = new SkinSurface { Part = SkinPart.LeftArm, Layer = SkinLayer.Base, Face = SkinFace.Front };
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic, "#0000FF"),
        [
            new() { Tool = SkinTool.Pencil, Surface = source, Color = "#FF0000" },
            new() { Tool = SkinTool.Pencil, Surface = source with { Face = SkinFace.Bottom }, TransparencyMask = true },
        ], 0, TestContext.CancellationToken);
        var pixels = (uint[])document.Pixels.Clone();
        var mask = document.TransparencyMask!.ToArray();

        Assert.ThrowsExactly<ArgumentException>(() =>
            SkinEditor.CopyPartToOpposite(document, SkinPart.LeftArm, document.Revision, cancellationToken: TestContext.CancellationToken));

        CollectionAssert.AreEqual(pixels, document.Pixels);
        CollectionAssert.AreEqual(mask, document.TransparencyMask);
        Assert.AreEqual(1L, document.Revision);
    }

    private static int Index(SkinFaceInfo face, int size, int x, int y) => ((face.Y + y) * size) + face.X + x;

    private static void FillFace(uint[] pixels, SkinFaceInfo face, int size, uint color)
    {
        for (var y = 0; y < face.Height; y++)
            Array.Fill(pixels, color, Index(face, size, 0, y), face.Width);
    }

    /// <summary>Gets or sets the runner-owned cancellation context.</summary>
    public TestContext TestContext { get; set; } = null!;
}
