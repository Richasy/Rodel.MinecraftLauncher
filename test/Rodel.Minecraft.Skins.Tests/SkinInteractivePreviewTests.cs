// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Protects incremental rendering from accumulating opacity, connecting gaps or mutating frozen display snapshots.</summary>
[TestClass]
public sealed class SkinInteractivePreviewTests
{
    /// <summary>Known atlas coordinates catch half-texel drift, accidental Classic mapping for Slim holes and clamped outside clicks; the ray witness shares the addressed texel.</summary>
    [TestMethod]
    [DataRow(SkinModel.Classic, 64)]
    [DataRow(SkinModel.Classic, 128)]
    [DataRow(SkinModel.Slim, 64)]
    [DataRow(SkinModel.Slim, 128)]
    public void AtlasAddressing_UsesExactCentersAndRejectsUnusedTexels(SkinModel model, int size)
    {
        var snapshot = new SkinRenderSnapshot(SkinEditor.Create(model, size: size));
        var scale = size / 64;
        var hit = snapshot.PickAtlas((8 * scale) + .5, (8 * scale) + .5, SkinLayer.Base)!;
        Assert.AreEqual(new SkinSurface { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front }, hit.Surface);
        Assert.AreEqual(0, hit.X); Assert.AreEqual(0, hit.Y);
        Assert.AreEqual(0, hit.LocalX); Assert.AreEqual(0, hit.LocalY);
        var ray = snapshot.Pick(new(new(-4 + (.5f / scale), 32 - (.5f / scale), 64), new(0, 0, -1), SkinLayer.Base));
        Assert.AreEqual(hit.Surface, ray!.Surface); Assert.AreEqual(hit.X, ray.X); Assert.AreEqual(hit.Y, ray.Y);
        Assert.AreEqual(1, snapshot.PickAtlas((8 * scale) + 1.5, (8 * scale) + .5, SkinLayer.Base)!.X);
        Assert.AreEqual(SkinFace.Left, snapshot.PickAtlas(16 * scale, (8 * scale) + .5, SkinLayer.Base)!.Surface.Face);
        var arm = snapshot.PickAtlas((54 * scale) + .5, (20 * scale) + .5, SkinLayer.Base);
        Assert.AreEqual(model == SkinModel.Classic, arm is not null);
        Assert.IsNull(snapshot.PickAtlas(-.001, 8, SkinLayer.Base));
        Assert.IsNull(snapshot.PickAtlas(size, 8, SkinLayer.Base));
        Assert.IsNull(snapshot.PickAtlas(8, size, SkinLayer.Base));
        Assert.IsNull(snapshot.PickAtlas((8 * scale) + .5, (8 * scale) + .5, SkinLayer.Outer));
        var pixels = snapshot.Pixels.ToArray();
        var marks = snapshot.BrushFootprint(new() { Tool = SkinTool.Pencil, Surface = hit.Surface, Color = "#FF0000", BrushSize = 3 });
        Assert.HasCount(4, marks, "A 3x3 stamp centered on the first face texel clips to 2x2 without wrapping.");
        CollectionAssert.AreEqual(pixels, snapshot.Pixels.ToArray(), "Cursor queries must never paint the shared display snapshot.");
    }

    /// <summary>Coplanar shoulder shells must composite the same earlier part in front as picking, rather than reverse stable order.</summary>
    [TestMethod]
    public void CoplanarShells_PreviewAndPickKeepEarlierGeometryInFront()
    {
        var body = new SkinSurface { Part = SkinPart.Body, Layer = SkinLayer.Outer, Face = SkinFace.Front };
        var arm = body with { Part = SkinPart.RightArm };
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic, "#204080"),
            [new() { Tool = SkinTool.Fill, Surface = body, Color = "#E0204080" }, new() { Tool = SkinTool.Fill, Surface = arm, Color = "#20E04080" }], 0, TestContext.CancellationToken);
        var hit = SkinGeometry.Pick(document, new(new(-4, 20, 64), new(0, 0, -1), SkinLayer.Outer));
        Assert.AreEqual(body, hit!.Surface);
        var image = StbImageSharp.ImageResult.FromMemory(SkinPreview.Render(document, new() { Size = 512, Yaw = 0, Pitch = 0 }, cancellationToken: TestContext.CancellationToken), StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        var offset = ((204 * 512) + 204) * 4;
        CollectionAssert.AreEqual(new byte[] { 128, 88, 80, 255 }, image.Data.AsSpan(offset, 4).ToArray(), "Body red over arm green over opaque blue, using independent source-over arithmetic.");
    }
    /// <summary>Repeated hits must match a single canonical gesture, including erasure and an explicit missed-hit gap.</summary>
    [TestMethod]
    [DataRow(SkinTool.Stroke, 64)]
    [DataRow(SkinTool.Eraser, 128)]
    public void IncrementalGesture_PreservesOriginalCoverageAndGap(SkinTool tool, int size)
    {
        var surface = new SkinSurface { Part = SkinPart.Head, Layer = SkinLayer.Outer, Face = SkinFace.Front };
        var document = SkinEditor.Create(SkinModel.Slim, size: size);
        if (tool == SkinTool.Eraser)
            document = SkinEditor.Apply(document, [new() { Tool = SkinTool.Fill, Surface = surface, Color = "#FF0000" }], document.Revision, TestContext.CancellationToken);
        var edit = new SkinEdit { Tool = tool, Surface = surface, Color = "#FF0000", Opacity = 0.5 };
        var preview = new SkinStrokePreview(document, edit);
        SkinStrokePoint[] points = [new(1, 1), new(1, 1), new(1.2, 1), new(6, 1, BreakBefore: true), new(6, 1)];
        foreach (var point in points) preview.Append(point);
        var canonical = SkinEditor.Apply(document, [edit with { Points = points }], document.Revision, TestContext.CancellationToken);
        CollectionAssert.AreEqual(canonical.Pixels, preview.Document.Pixels);
        Assert.AreEqual("#FF000080", SkinEditor.Sample(preview.Document, surface, 1, 1), "The same texel must receive half coverage once, for paint or erase.");
        Assert.AreEqual(tool == SkinTool.Eraser ? "#FF0000FF" : "#00000000", SkinEditor.Sample(preview.Document, surface, 3, 1), "A missed-hit gap must not be interpolated.");
        Assert.AreEqual(document.Revision, preview.Document.Revision);
        var frozen = new SkinRenderSnapshot(preview.Document);
        var pixels = frozen.Pixels.ToArray();
        preview.Append(new(4, 4, BreakBefore: true));
        CollectionAssert.AreEqual(pixels, frozen.Pixels.ToArray(), "Later pointer samples cannot change an already submitted frame.");
        // Moving a soft stamp increases coverage of an already touched texel; reusing preview pixels would accumulate alpha.
        var softEdit = edit with { BrushSize = 3, Softness = 1 };
        var soft = new SkinStrokePreview(document, softEdit);
        soft.Append(new(1, 1)); soft.Append(new(2, 1));
        Assert.AreEqual("#FF000080", SkinEditor.Sample(soft.Document, surface, 2, 1));
        CollectionAssert.AreEqual(SkinEditor.Apply(document, [soft.ToEdit()], document.Revision, TestContext.CancellationToken).Pixels, soft.Document.Pixels);
    }

    public TestContext TestContext { get; set; }
}