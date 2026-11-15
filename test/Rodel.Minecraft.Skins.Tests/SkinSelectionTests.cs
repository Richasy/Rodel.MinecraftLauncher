// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Protects geometric and color-selection footprints independently of clipboard transforms and color arithmetic.</summary>
[TestClass]
public sealed class SkinSelectionTests
{
    private static readonly SkinSurface Front = new() { Part = SkinPart.Head, Layer = SkinLayer.Outer, Face = SkinFace.Front };

    /// <summary>At either texture stride, color selection must distinguish disconnected matches from a contiguous region, with inversion covering the rest of the face.</summary>
    [TestMethod]
    [DataRow(64)]
    [DataRow(128)]
    public void ColorSelections_DistinguishConnectedMatchesAndInversion(int size)
    {
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic, size: size),
        [
            new() { Tool = SkinTool.Rectangle, Surface = Front, X = 0, Y = 0, X2 = 1, Y2 = 1, Filled = true, Color = "#FF0000" },
            new() { Tool = SkinTool.Pencil, Surface = Front, X = 6, Y = 6, Color = "#FF0000" }
        ], 0, TestContext.CancellationToken);
        var request = new SkinSelectionRequest { Surface = Front, Kind = SkinSelectionKind.Connected, X = 0, Y = 0 };
        var connected = SkinSelections.Create(document, request);
        var matching = SkinSelections.Create(document, request with { Kind = SkinSelectionKind.Matching });
        Assert.IsTrue(SkinSelections.Contains(connected, 1, 1));
        Assert.IsFalse(SkinSelections.Contains(connected, 6, 6));
        Assert.IsTrue(SkinSelections.Contains(matching, 6, 6));
        document = SkinEditor.Apply(document,
            [new() { Tool = SkinTool.AdjustColor, Surface = Front, Hue = 120, Selection = matching }], 1, TestContext.CancellationToken);
        Assert.AreEqual("#00FF00FF", SkinEditor.Sample(document, Front, 0, 0));
        Assert.AreEqual("#00FF00FF", SkinEditor.Sample(document, Front, 6, 6));
        document = SkinEditor.Apply(document,
            [new() { Tool = SkinTool.Fill, Surface = Front, FillMode = SkinFillMode.Face, Color = "#0000FF", Selection = matching with { Inverted = true } }], 2, TestContext.CancellationToken);
        Assert.AreEqual("#0000FFFF", SkinEditor.Sample(document, Front, 7, 7));
        Assert.AreEqual("#00FF00FF", SkinEditor.Sample(document, Front, 6, 6));
    }

    /// <summary>Geometric selection and clipboard capture must retain their actual footprint, not silently treat the bounding box as selected.</summary>
    [TestMethod]
    [DataRow(SkinSelectionKind.Ellipse, false)]
    [DataRow(SkinSelectionKind.Polygon, true)]
    public void GeometricSelection_FillAndCaptureRespectTheFootprint(SkinSelectionKind kind, bool cornerSelected)
    {
        var document = SkinEditor.Create(SkinModel.Classic);
        var selection = SkinSelections.Create(document, new()
        {
            Surface = Front,
            Kind = kind,
            Width = 4,
            Height = 4,
            Points = kind == SkinSelectionKind.Polygon ? [new(0, 0), new(4, 0), new(0, 4)] : null
        });
        document = SkinEditor.Apply(document,
            [new() { Tool = SkinTool.Fill, Surface = Front, FillMode = SkinFillMode.Face, Color = "#FF0000", Selection = selection }], 0, TestContext.CancellationToken);
        Assert.AreEqual(cornerSelected ? "#FF0000FF" : "#00000000", SkinEditor.Sample(document, Front, 0, 0));
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(document, Front, 1, 1));
        Assert.AreEqual("#00000000", SkinEditor.Sample(document, Front, 7, 7));
        var block = SkinEditor.Capture(document, Front, selection);
        Assert.IsNotNull(block.Mask);
        Assert.AreEqual(cornerSelected, block.Mask[0]);
        Assert.IsFalse(block.Mask[^1]);
    }

    public TestContext TestContext { get; set; }
}