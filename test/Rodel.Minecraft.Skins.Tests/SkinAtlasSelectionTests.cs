// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Protects absolute selection clipping independently of legacy face-local reflected selections.</summary>
[TestClass]
public sealed class SkinAtlasSelectionTests
{
    // Author review: ordinary symmetry tests permit mirrored writes outside the source selection.
    // A two-cell atlas mask and a wide brush distinguish absolute final-write clipping from that contract.
    /// <summary>A large reflected brush clips both sides to selected atlas cells on either model and resolution.</summary>
    [TestMethod]
    [DataRow(SkinModel.Classic, 64)]
    [DataRow(SkinModel.Classic, 128)]
    [DataRow(SkinModel.Slim, 64)]
    [DataRow(SkinModel.Slim, 128)]
    public void WideMirroredBrush_ClipsEveryFinalWrite(SkinModel model, int size)
    {
        var document = SkinEditor.Create(model, "#000000", size);
        var right = new SkinSurface { Part = SkinPart.RightArm, Layer = SkinLayer.Base, Face = SkinFace.Front };
        var left = right with { Part = SkinPart.LeftArm };
        var mask = new bool[size * size];
        var source = Index(model, size, right, 1, 2);
        var mirrored = Index(model, size, left, SkinLayout.Face(model, left, size).Width - 2, 2);
        mask[source] = mask[mirrored] = true;
        var edit = new SkinEdit { Tool = SkinTool.Pencil, Surface = right, X = 1, Y = 2, BrushSize = 5, Color = "#FF0000", Mirror = SkinMirrorMode.Body, WrapFaces = true, PixelMask = mask };
        Assert.HasCount(2, SkinEditor.BrushFootprint(document, edit, TestContext.CancellationToken));
        var changed = SkinEditor.Apply(document, [edit], 0, TestContext.CancellationToken);
        for (var i = 0; i < mask.Length; i++)
            Assert.AreEqual(i == source || i == mirrored ? 0xFF0000FFu : document.Pixels[i], changed.Pixels[i]);
        mask[mirrored] = false;
        var clipped = SkinEditor.Apply(document, [edit], 0, TestContext.CancellationToken);
        Assert.AreEqual(document.Pixels[mirrored], clipped.Pixels[mirrored], "Symmetry must not escape the atlas mask.");
        mask[source] = false;
        Assert.AreSame(document, SkinEditor.Apply(document, [edit], 0, TestContext.CancellationToken), "An empty mask is a no-op, not unrestricted painting.");
    }

    // Author review: a selected island separated by an unselected column catches traversal across forbidden pixels;
    // a selected neighbor face catches accidental fill-scope expansion. Preview/commit equality alone would miss both.
    /// <summary>Fill follows selected connectivity without expanding to another face or disconnected selected island.</summary>
    [TestMethod]
    public void FillMask_RestrictsConnectivityAndOriginalScope()
    {
        var document = SkinEditor.Create(SkinModel.Classic, "#000000");
        var front = new SkinSurface { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };
        var back = front with { Face = SkinFace.Back };
        var mask = new bool[4096];
        foreach (var surface in new[] { front, back })
        {
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                    if (x != 3) mask[Index(SkinModel.Classic, 64, surface, x, y)] = true;
            }
        }

        var edit = new SkinEdit { Tool = SkinTool.Fill, Surface = front, X = 1, Y = 1, Color = "#00FF00", PixelMask = mask };
        var marks = SkinEditor.BrushFootprint(document, edit, TestContext.CancellationToken);
        Assert.HasCount(24, marks);
        Assert.IsTrue(marks.All(mark => mark.Surface == front && mark.X < 3));
        var changed = SkinEditor.Apply(document, [edit], 0, TestContext.CancellationToken);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                Assert.AreEqual(x < 3 ? "#00FF00FF" : "#000000FF", SkinEditor.Sample(changed, front, x, y));
                Assert.AreEqual("#000000FF", SkinEditor.Sample(changed, back, x, y));
            }
        }
    }

    // Author review: mutate both externally held arrays after Begin/ToEdit, then sample another face.
    // This fails if a queued preview borrows the mask or if returning a commit edit exposes its private mask.
    /// <summary>A cross-face gesture freezes the mask while preserving one undoable canonical transaction.</summary>
    [TestMethod]
    public void Preview_FreezesMaskAndClipsCrossFaceSamples()
    {
        var document = SkinEditor.Create(SkinModel.Classic, "#000000");
        var front = new SkinSurface { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };
        var back = front with { Face = SkinFace.Back };
        var mask = new bool[4096];
        mask[Index(SkinModel.Classic, 64, front, 1, 1)] = mask[Index(SkinModel.Classic, 64, back, 2, 2)] = true;
        var preview = new SkinStrokePreview(document, new() { Tool = SkinTool.Stroke, Surface = front, Color = "#0000FF", PixelMask = mask });
        Array.Fill(mask, false);
        preview.Append(new(1, 1));
        var exported = preview.ToEdit();
        Array.Fill(exported.PixelMask!, false);
        preview.Append(new(2, 2, back, true));
        preview.Append(new(4, 4, back, true));
        Assert.AreEqual("#0000FFFF", SkinEditor.Sample(preview.Document, back, 2, 2));
        Assert.AreEqual("#000000FF", SkinEditor.Sample(preview.Document, back, 4, 4));
        var committed = SkinEditor.Apply(document, [preview.ToEdit()], 0, TestContext.CancellationToken);
        CollectionAssert.AreEqual(preview.Document.Pixels, committed.Pixels);
        Assert.HasCount(1, committed.Undo);
        CollectionAssert.AreEqual(document.Pixels, SkinEditor.Restore(committed, committed.Revision, cancellationToken: TestContext.CancellationToken).Pixels);
    }

    /// <summary>Invalid dimensions and destructive transfers must fail before any source pixel can be removed.</summary>
    [TestMethod]
    public void InvalidMask_RejectsMalformedAndTransferEdits()
    {
        var document = SkinEditor.Create(SkinModel.Classic);
        var edit = new SkinEdit { Tool = SkinTool.Pencil, Surface = new() { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front }, Color = "#FF0000", PixelMask = new bool[1] };
        Assert.Throws<ArgumentException>(() => SkinEditor.Apply(document, [edit], 0, TestContext.CancellationToken));
        Assert.Throws<ArgumentException>(() => SkinEditor.Apply(document, [edit with { Tool = SkinTool.Cut, PixelMask = new bool[4096] }], 0, TestContext.CancellationToken));
        Assert.AreEqual(0L, document.Revision);
    }

    private static int Index(SkinModel model, int size, SkinSurface surface, int x, int y)
    {
        var face = SkinLayout.Face(model, surface, size);
        return ((face.Y + y) * size) + face.X + x;
    }

    /// <summary>Gets or sets cancellation supplied by the test runner.</summary>
    public TestContext TestContext { get; set; } = null!;
}