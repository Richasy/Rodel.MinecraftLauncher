// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Protects captured-pixel transforms, overlap safety and destructive transfer guards.</summary>
[TestClass]
public sealed class SkinClipboardTests
{
    private static readonly SkinSurface Front = new() { Part = SkinPart.Head, Layer = SkinLayer.Outer, Face = SkinFace.Front };

    /// <summary>At either texture stride, a rotated overlapping move must capture before clearing; a clipped destination must fail rather than discard source pixels.</summary>
    [TestMethod]
    [DataRow(64)]
    [DataRow(128)]
    public void ClipboardMoveRotationAndResize_PreserveAsymmetricSelectedPixels(int size)
    {
        string[] colors = ["#FF0000", "#0000FF", "#00FF00", "#FFFFFF", "#FFFF00", "#FF00FF"];
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic, size: size), [.. colors.Select((color, i) =>
            new SkinEdit { Tool = SkinTool.Pencil, Surface = Front, X = 1 + (i % 2), Y = 1 + (i / 2), Color = color })], 0, TestContext.CancellationToken);
        var move = new SkinEdit { Tool = SkinTool.Move, Surface = Front, SourceSelection = new(1, 1, 2, 3), X = 2, Y = 2, QuarterTurns = 1 };
        Assert.ThrowsExactly<ArgumentException>(() => SkinEditor.Apply(document,
            [move with { Selection = new(2, 2, 1, 1) }], 1, TestContext.CancellationToken));
        document = SkinEditor.Apply(document, [move], 1, TestContext.CancellationToken);
        Assert.AreEqual("#FFFF00FF", SkinEditor.Sample(document, Front, 2, 2));
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(document, Front, 4, 2));
        Assert.AreEqual("#FF00FFFF", SkinEditor.Sample(document, Front, 2, 3));
        Assert.AreEqual("#0000FFFF", SkinEditor.Sample(document, Front, 4, 3));
        Assert.AreEqual("#00000000", SkinEditor.Sample(document, Front, 1, 1));
        var clipboard = SkinEditor.Capture(document, Front, new(2, 2, 3, 2));
        var pasted = SkinEditor.Apply(SkinEditor.Create(SkinModel.Slim),
            [new() { Tool = SkinTool.Paste, Surface = Front, Clipboard = clipboard, DestinationWidth = 6, DestinationHeight = 4 }], 0, TestContext.CancellationToken);
        Assert.AreEqual("#FFFF00FF", SkinEditor.Sample(pasted, Front, 1, 1));
        Assert.AreEqual("#0000FFFF", SkinEditor.Sample(pasted, Front, 5, 3));
    }

    /// <summary>Moves and masked flips must reject brush settings that could clear the source without placing its pixels; copies remain available for partial painting.</summary>
    [TestMethod]
    [DataRow(SkinTool.Move, 0.0, false)]
    [DataRow(SkinTool.Move, 0.5, false)]
    [DataRow(SkinTool.Move, 1.0, true)]
    [DataRow(SkinTool.FlipHorizontal, 0.0, false)]
    public void DestructiveTransfer_WithPartialOpacityOrAlphaLock_RejectsWithoutLosingPixels(SkinTool tool, double opacity, bool lockAlpha)
    {
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic),
            [new() { Tool = SkinTool.Pencil, Surface = Front, Color = "#FF0000" }], 0, TestContext.CancellationToken);
        var source = new SkinSelection(0, 0, 2, 1) { Shape = SkinSelectionShape.Mask, Mask = [true, false] };
        Assert.ThrowsExactly<ArgumentException>(() => SkinEditor.Apply(document,
        [
            new() { Tool = tool, Surface = Front, SourceSelection = source, X = 2, Y = 0, Opacity = opacity, LockAlpha = lockAlpha }
        ], 1, TestContext.CancellationToken));
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(document, Front, 0, 0));
        Assert.AreEqual(1L, document.Revision);
    }

    public TestContext TestContext { get; set; }
}