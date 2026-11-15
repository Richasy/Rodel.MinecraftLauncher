// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Protects atomic cross-face atlas transfers, separately from the legacy face-local clipboard tests.</summary>
[TestClass]
public sealed class SkinAtlasTransferTests
{
    // Author review: frozen cross-face overlap plus an included transparent pixel and an excluded hole
    // distinguish correct replacement from source-over, rectangular copying, and reading partially moved pixels.
    /// <summary>Overlapping moves across a UV seam preserve exact source RGBA and nonrectangular holes.</summary>
    [TestMethod]
    [DataRow(1)]
    [DataRow(-1)]
    public void CrossFaceMove_FreezesOverlapAndTransparentSelection(int offset)
    {
        var document = SkinEditor.Create(SkinModel.Classic);
        var row = 9 * 64;
        document.Pixels[row + 39] = 0xFF0000FF; document.Pixels[row + 40] = 0;
        document.Pixels[row + 41] = 0x00FF00FF; document.Pixels[row + 42] = 0x0000FFFF;
        var original = document.Pixels.ToArray(); var mask = new bool[4096];
        mask[row + 39] = mask[row + 40] = mask[row + 42] = true;
        var captured = SkinEditor.CaptureAtlas(document, mask);
        Assert.AreEqual(39, captured.X); Assert.AreEqual(4, captured.Block.Width);
        CollectionAssert.AreEqual(expectedArray, captured.Block.Mask!);
        var edit = new SkinEdit { Tool = SkinTool.Move, AtlasTransfer = new() { SourceMask = mask, X = 39 + offset, Y = 9, Layer = SkinLayer.Outer } };
        var moved = SkinEditor.Apply(document, [edit], document.Revision, TestContext.CancellationToken);
        var expected = original.ToArray();
        expected[row + 39] = expected[row + 40] = expected[row + 42] = 0;
        expected[row + 39 + offset] = 0xFF0000FF; expected[row + 40 + offset] = 0; expected[row + 42 + offset] = 0x0000FFFF;
        CollectionAssert.AreEqual(expected, moved.Pixels);
        CollectionAssert.AreEqual(original, document.Pixels);
        Assert.HasCount(1, moved.Undo);
        CollectionAssert.AreEqual(original, SkinEditor.Restore(moved, moved.Revision, cancellationToken: TestContext.CancellationToken).Pixels);
    }

    // Author review: updates return to a prior legal destination after a rejected hole and caller-array mutation.
    // Sampling known source/target cells proves previews restart from the original baseline, not the previous frame.
    /// <summary>Invalid moves preserve the last legal draft; returning to the origin is an exact no-op.</summary>
    [TestMethod]
    public void MovePreview_RejectsLossAndDoesNotAccumulate()
    {
        var document = SkinEditor.Create(SkinModel.Classic);
        document.Pixels[(9 * 64) + 40] = 0xFF0000FF;
        var mask = new bool[4096]; mask[(9 * 64) + 40] = true;
        var preview = new SkinTransferPreview(document, new() { Tool = SkinTool.Move, AtlasTransfer = new() { SourceMask = mask, X = 40, Y = 9, Layer = SkinLayer.Outer } });
        Array.Fill(mask, false);
        preview.Update(41, 9); var legal = preview.Document.Pixels.ToArray();
        Assert.Throws<ArgumentException>(() => preview.Update(0, 0));
        CollectionAssert.AreEqual(legal, preview.Document.Pixels);
        var escaped = preview.ToEdit(); Array.Fill(escaped.AtlasTransfer!.SourceMask!, false);
        preview.Update(42, 9); preview.Update(41, 9);
        CollectionAssert.AreEqual(legal, preview.Document.Pixels);
        preview.Update(40, 9);
        CollectionAssert.AreEqual(document.Pixels, preview.Document.Pixels);
        Assert.AreSame(document, SkinEditor.Apply(document, [preview.ToEdit()], document.Revision, TestContext.CancellationToken));
    }

    // Author review: the opaque-base validation must inspect the entire planned clear/write set before publishing.
    // A valid source clear paired with a partially transparent paste catches partial writes and silent alpha coercion.
    /// <summary>Protected base deletion needs explicit opaque replacement, while other surfaces clear transparently.</summary>
    [TestMethod]
    public void ProtectedBase_RejectsTransparencyBeforeAnyWrite()
    {
        var document = SkinEditor.Create(SkinModel.Classic, "#112233");
        var mask = new bool[4096]; mask[(9 * 64) + 9] = mask[(9 * 64) + 40] = true;
        var original = document.Pixels.ToArray();
        var cut = new SkinEdit { Tool = SkinTool.Cut, AtlasTransfer = new() { SourceMask = mask } };
        Assert.Throws<ArgumentException>(() => SkinEditor.Apply(document, [cut], 0, TestContext.CancellationToken));
        CollectionAssert.AreEqual(original, document.Pixels);
        var cleared = SkinEditor.Apply(document, [cut with { ClearColor = "#AABBCC" }], 0, TestContext.CancellationToken);
        Assert.AreEqual(0xAABBCCFFu, cleared.Pixels[(9 * 64) + 9]); Assert.AreEqual(0u, cleared.Pixels[(9 * 64) + 40]);
        var paste = new SkinEdit { Tool = SkinTool.Paste, AtlasTransfer = new() { X = 9, Y = 9, Block = new(2, 1, [0xFF0000FF, 0x00FF0080]) } };
        Assert.Throws<ArgumentException>(() => SkinEditor.Apply(document, [paste], 0, TestContext.CancellationToken));
        CollectionAssert.AreEqual(original, document.Pixels);
    }

    // Author review: a 128-wide block on a 64 Slim document establishes raw 1:1 clipping, not resize or model remap.
    // One in-bounds selected pixel and an out-of-bounds selected pixel make a fully-cropped no-op separately observable.
    /// <summary>Paste clips to real destination topology, preserves holes, and never scales the clipboard.</summary>
    [TestMethod]
    public void Paste_ClipsAcrossSizeWithoutScaling()
    {
        var document = SkinEditor.Create(SkinModel.Slim);
        var pixels = new uint[128]; var mask = new bool[128];
        pixels[0] = 0xFF0000FF; pixels[1] = 0x00FF00FF; pixels[127] = 0x0000FFFF;
        mask[0] = mask[127] = true;
        var edit = new SkinEdit { Tool = SkinTool.Paste, AtlasTransfer = new() { X = 40, Y = 9, Layer = SkinLayer.Outer, Block = new(128, 1, pixels, mask) } };
        var pasted = SkinEditor.Apply(document, [edit], 0, TestContext.CancellationToken);
        Assert.AreEqual(0xFF0000FFu, pasted.Pixels[(9 * 64) + 40]); Assert.AreEqual(0u, pasted.Pixels[(9 * 64) + 41]);
        Assert.HasCount(1, pasted.Pixels.Zip(document.Pixels).Where(pair => pair.First != pair.Second).ToArray());
        Assert.AreSame(document, SkinEditor.Apply(document, [edit with { AtlasTransfer = edit.AtlasTransfer with { X = -200 } }], 0, TestContext.CancellationToken));
    }

    /// <summary>Gets or sets cancellation supplied by the test runner.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static readonly bool[] expectedArray = [true, true, false, true];
}