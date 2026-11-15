// Copyright (c) Richasy. All rights reserved.

using StbImageSharp;

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Protects editable texture semantics and the Java UV artifact contract, independently of MCP transport.</summary>
[TestClass]
public sealed class SkinDrawingTests
{
    private static readonly SkinSurface Front = new() { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };

    /// <summary>Legacy RGB skins must not acquire an opaque helmet; explicit old hat transparency and mirrored left limbs follow Java's import rules.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LegacyImport_NormalizesHatAndMirrorsIndependentLimbs(bool explicitHatTransparency)
    {
        var rgba = new byte[64 * 32 * 4];
        for (var i = 0; i < 64 * 32; i++)
        {
            rgba[i * 4] = 17;
            rgba[(i * 4) + 1] = 34;
            rgba[(i * 4) + 2] = 51;
            rgba[(i * 4) + 3] = 255;
        }

        rgba[((20 * 64) + 44) * 4] = 255;
        rgba[((20 * 64) + 47) * 4] = 128;
        if (explicitHatTransparency)
            rgba[(63 * 4) + 3] = 0;
        using var stream = new MemoryStream();
        new StbImageWriteSharp.ImageWriter().WritePng(rgba, 64, 32, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        var document = SkinPng.Decode(stream.ToArray(), SkinModel.Classic);
        Assert.AreEqual("#802233FF", SkinEditor.Sample(document, new() { Part = SkinPart.LeftArm, Layer = SkinLayer.Base, Face = SkinFace.Front }, 0, 0));
        Assert.AreEqual("#FF2233FF", SkinEditor.Sample(document, new() { Part = SkinPart.LeftArm, Layer = SkinLayer.Base, Face = SkinFace.Front }, 3, 0));
        var hat = SkinEditor.Sample(document, Front with { Layer = SkinLayer.Outer }, 0, 0);
        Assert.AreEqual(explicitHatTransparency ? "#112233FF" : "#11223300", hat);
        Assert.AreEqual("#00000000", SkinEditor.Sample(document, new() { Part = SkinPart.Body, Layer = SkinLayer.Outer, Face = SkinFace.Front }, 0, 0));
    }

    /// <summary>A partially valid AI batch must not change caller-owned pixels or consume an undo step.</summary>
    [TestMethod]
    public void InvalidBatch_DoesNotMutateDocumentOrHistory()
    {
        var document = SkinEditor.Create(SkinModel.Classic, "#112233");
        var before = SkinPng.Encode(document);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SkinEditor.Apply(document,
        [
            new() { Tool = SkinTool.Pencil, Surface = Front, X = 0, Y = 0, Color = "#FF0000" },
            new() { Tool = SkinTool.Line, Surface = Front, X = 0, Y = 0, X2 = 8, Y2 = 0, Color = "#FFFFFF" }
        ], 0, TestContext.CancellationToken));
        CollectionAssert.AreEqual(before, SkinPng.Encode(document));
        Assert.IsEmpty(document.Undo);
        Assert.AreEqual(0L, document.Revision);
    }

    /// <summary>Selections bound flood-fill and overlapping copy snapshots its source; undo/redo never resurrect stale revisions.</summary>
    [TestMethod]
    public void SelectedDrawingAndOverlappingCopy_PreserveUndoAndConcurrency()
    {
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic, "#000000"),
        [
            new() { Tool = SkinTool.Rectangle, Surface = Front, X = 1, Y = 1, X2 = 5, Y2 = 5, Color = "#FFFFFF" },
            new() { Tool = SkinTool.Fill, Surface = Front, X = 2, Y = 2, Color = "#123456" },
            new() { Tool = SkinTool.Pencil, Surface = Front, X = 1, Y = 1, Color = "#FF0000" },
            new() { Tool = SkinTool.Pencil, Surface = Front, X = 2, Y = 1, Color = "#0000FF" },
            new() { Tool = SkinTool.Pencil, Surface = Front, X = 3, Y = 1, Color = "#00FF00" },
            new() { Tool = SkinTool.Copy, Surface = Front, Source = Front, SourceSelection = new(1, 1, 4, 1), X = 2, Y = 1 },
            new() { Tool = SkinTool.Pencil, Surface = Front, X = 0, Y = 0, BrushSize = 5, Color = "#FF0000", Selection = new(0, 0, 1, 1) }
        ], 0, TestContext.CancellationToken);
        Assert.AreEqual("#123456FF", SkinEditor.Sample(document, Front, 3, 3));
        Assert.AreEqual("#000000FF", SkinEditor.Sample(document, Front, 0, 1));
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(document, Front, 0, 0));
        Assert.AreEqual("#FFFFFFFF", SkinEditor.Sample(document, Front, 5, 1));
        Assert.AreEqual("#0000FFFF", SkinEditor.Sample(document, Front, 3, 1));
        Assert.AreEqual("#00FF00FF", SkinEditor.Sample(document, Front, 4, 1));
        var undone = SkinEditor.Restore(document, 1, cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual("#000000FF", SkinEditor.Sample(undone, Front, 3, 3));
        Assert.ThrowsExactly<InvalidOperationException>(() => SkinEditor.Apply(undone,
            [new() { Tool = SkinTool.Pencil, Surface = Front, Color = "#FFFFFF" }], 0, TestContext.CancellationToken));
        var redone = SkinEditor.Restore(undone, 2, redo: true, TestContext.CancellationToken);
        CollectionAssert.AreEqual(document.Pixels, redone.Pixels);
        Assert.AreEqual(3L, redone.Revision);
    }

    /// <summary>Asymmetric arm marks must land at Java's independently specified atlas offsets, not mirrored or shifted between models.</summary>
    [TestMethod]
    [DataRow(SkinModel.Classic, 4, 44)]
    [DataRow(SkinModel.Slim, 3, 43)]
    public void ExportedArmsAndOuterLayer_UseStandardJavaAtlas(SkinModel model, int armWidth, int leftBackX)
    {
        var right = new SkinSurface { Part = SkinPart.RightArm, Layer = SkinLayer.Base, Face = SkinFace.Front };
        var left = new SkinSurface { Part = SkinPart.LeftArm, Layer = SkinLayer.Base, Face = SkinFace.Back };
        var outer = new SkinSurface { Part = SkinPart.Head, Layer = SkinLayer.Outer, Face = SkinFace.Front };
        var document = SkinEditor.Apply(SkinEditor.Create(model, "#000000"),
        [
            new() { Tool = SkinTool.Pencil, Surface = right, X = armWidth - 1, Y = 2, Color = "#FF0000" },
            new() { Tool = SkinTool.Pencil, Surface = left, X = 0, Y = 3, Color = "#00FF00" },
            new() { Tool = SkinTool.Pencil, Surface = outer, X = 2, Y = 1, Color = "#0000FF80" }
        ], 0, TestContext.CancellationToken);
        // Decode the artifact without using the editor's importer or UV helpers as an oracle.
        var image = ImageResult.FromMemory(SkinPng.Encode(document), ColorComponents.RedGreenBlueAlpha);
        Assert.AreEqual(64, image.Width);
        Assert.AreEqual(64, image.Height);
        CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255 }, Pixel(44 + armWidth - 1, 22));
        CollectionAssert.AreEqual(new byte[] { 0, 255, 0, 255 }, Pixel(leftBackX, 55));
        CollectionAssert.AreEqual(new byte[] { 0, 0, 255, 128 }, Pixel(42, 9));
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 0 }, Pixel(43, 9));
        CollectionAssert.AreEqual(document.Pixels, SkinPng.Decode(SkinPng.Encode(document), model).Pixels);

        byte[] Pixel(int x, int y) => image.Data.AsSpan(((y * 64) + x) * 4, 4).ToArray();
    }

    /// <summary>Shape interiors, replacement, shading and transforms must remain editable pixel operations, preserving outer alpha.</summary>
    [TestMethod]
    public void OuterShapesAndTransforms_PreserveExactColorsAndTransparency()
    {
        var surface = Front with { Layer = SkinLayer.Outer };
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic),
        [
            new() { Tool = SkinTool.Ellipse, Surface = surface, X = 1, Y = 1, X2 = 5, Y2 = 5, Filled = true, Color = "#10203080" },
            new() { Tool = SkinTool.Line, Surface = surface, X = 0, Y = 0, X2 = 7, Y2 = 0, Color = "#ABCDEF40" },
            new() { Tool = SkinTool.Replace, Surface = surface, MatchColor = "#10203080", Color = "#20304080" },
            new() { Tool = SkinTool.Shade, Surface = surface, Amount = 16, Selection = new(3, 3, 1, 1) },
            new() { Tool = SkinTool.FlipHorizontal, Surface = surface },
            new() { Tool = SkinTool.FlipVertical, Surface = surface },
            new() { Tool = SkinTool.Eraser, Surface = surface, X = 4, Y = 3 }
        ], 0, TestContext.CancellationToken);
        Assert.AreEqual("#30405080", SkinEditor.Sample(document, surface, 4, 4));
        Assert.AreEqual("#20304080", SkinEditor.Sample(document, surface, 3, 4));
        Assert.AreEqual("#00000000", SkinEditor.Sample(document, surface, 4, 3));
        Assert.AreEqual("#00000000", SkinEditor.Sample(document, surface, 7, 6));
        Assert.AreEqual("#ABCDEF40", SkinEditor.Sample(document, surface, 0, 7));
    }

    public TestContext TestContext { get; set; }
}