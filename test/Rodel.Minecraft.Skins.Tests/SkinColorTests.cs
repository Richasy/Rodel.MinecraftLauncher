// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Protects gradient endpoint/alpha arithmetic and alpha-preserving color adjustments.</summary>
[TestClass]
public sealed class SkinColorTests
{
    private static readonly SkinSurface Front = new() { Part = SkinPart.Head, Layer = SkinLayer.Outer, Face = SkinFace.Front };

    /// <summary>Linear gradients must use inclusive color endpoints and respect selection boundaries.</summary>
    [TestMethod]
    public void LinearGradient_UsesEndpointsWithoutPaintingOutsideSelection()
    {
        var surface = Front with { Layer = SkinLayer.Base };
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic, "#0000FF"),
        [
            new() { Tool = SkinTool.Gradient, Surface = surface, X = 0, Y = 0, X2 = 7, Y2 = 0,
                Color = "#000000", SecondaryColor = "#FFFFFF", Selection = new(0, 0, 8, 1) }
        ], 0, TestContext.CancellationToken);
        Assert.AreEqual("#000000FF", SkinEditor.Sample(document, surface, 0, 0));
        Assert.AreEqual("#6D6D6DFF", SkinEditor.Sample(document, surface, 3, 0));
        Assert.AreEqual("#FFFFFFFF", SkinEditor.Sample(document, surface, 7, 0));
        Assert.AreEqual("#0000FFFF", SkinEditor.Sample(document, surface, 3, 1));
    }

    /// <summary>Transparent gradient endpoints must not introduce hidden-color halos, and HSV adjustment must preserve pixel alpha.</summary>
    [TestMethod]
    public void RadialGradientAndColorAdjustment_PreserveMeaningfulAlpha()
    {
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic),
        [
            new() { Tool = SkinTool.Gradient, Surface = Front, X = 0, Y = 0, X2 = 2, Y2 = 0,
                Color = "#FF000000", SecondaryColor = "#0000FFFF", GradientKind = SkinGradientKind.Radial, Selection = new(0, 0, 3, 1) }
        ], 0, TestContext.CancellationToken);
        Assert.AreEqual("#00000000", SkinEditor.Sample(document, Front, 0, 0));
        Assert.AreEqual("#0000FF80", SkinEditor.Sample(document, Front, 1, 0));
        document = SkinEditor.Apply(document,
        [
            new() { Tool = SkinTool.AdjustColor, Surface = Front, Hue = 120, Selection = new(1, 0, 1, 1) }
        ], 1, TestContext.CancellationToken);
        Assert.AreEqual("#FF000080", SkinEditor.Sample(document, Front, 1, 0));
    }

    public TestContext TestContext { get; set; }
}