// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Protects pen coverage, model-edge transport and actual model-space picking, not UI pointer handling.</summary>
[TestClass]
public sealed class SkinBrushAndSurfaceTests
{
    private static readonly SkinSurface Front = new() { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };

    /// <summary>Dense pointer events must not darken one stroke; circular falloff and alpha locking must preserve predictable RGBA.</summary>
    [TestMethod]
    [DataRow(2)]
    [DataRow(41)]
    public void SoftCircularStroke_EventDensityDoesNotAccumulateOpacity(int samples)
    {
        var outer = Front with { Layer = SkinLayer.Outer };
        var points = Enumerable.Range(0, samples).Select(i => new SkinStrokePoint(3 + (2.0 * i / (samples - 1)), 3)).ToArray();
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic),
        [
            new() { Tool = SkinTool.Stroke, Surface = outer, Points = points, Color = "#FF0000", BrushShape = SkinBrushShape.Circle, BrushSize = 5, Softness = 1, Opacity = 0.5 }
        ], 0, TestContext.CancellationToken);
        Assert.AreEqual("#FF000080", SkinEditor.Sample(document, outer, 4, 3));
        Assert.AreEqual("#FF00001A", SkinEditor.Sample(document, outer, 3, 1));
        Assert.AreEqual("#00000000", SkinEditor.Sample(document, outer, 1, 1));
        document = SkinEditor.Apply(document,
        [
            new() { Tool = SkinTool.Eraser, Surface = outer, X = 4, Y = 3, Opacity = 0.5 },
            new() { Tool = SkinTool.Pencil, Surface = outer, X = 4, Y = 3, BrushSize = 7, Color = "#FFFFFF", LockAlpha = true }
        ], 1, TestContext.CancellationToken);
        Assert.AreEqual("#FFFFFF40", SkinEditor.Sample(document, outer, 4, 3));
        Assert.AreEqual("#00000000", SkinEditor.Sample(document, outer, 1, 0), "Alpha lock must not create paint on empty pixels.");
    }

    /// <summary>Half-opacity source-over paint must mix color while keeping Java's required background base opaque.</summary>
    [TestMethod]
    public void BasePainting_OpacityBlendsInsteadOfMakingThePlayerTransparent()
    {
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic, "#0000FF"),
            [new() { Tool = SkinTool.Pencil, Surface = Front, Color = "#FF0000", Opacity = 0.5 }], 0, TestContext.CancellationToken);
        Assert.AreEqual("#800080FF", SkinEditor.Sample(document, Front, 0, 0));
    }

    /// <summary>Wrapped color-tolerant flood fill must cross model edges but preserve dissimilar pixels and unrelated body parts.</summary>
    [TestMethod]
    public void WrappedBucket_UsesToleranceWithoutEscapingTheCuboid()
    {
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic, "#000000"),
        [
            new() { Tool = SkinTool.Fill, Surface = Front, FillMode = SkinFillMode.Part, Color = "#101010" },
            new() { Tool = SkinTool.Pencil, Surface = Front, X = 2, Y = 2, Color = "#121212" },
            new() { Tool = SkinTool.Pencil, Surface = Front, X = 3, Y = 3, Color = "#202020" }
        ], 0, TestContext.CancellationToken);
        document = SkinEditor.Apply(document,
            [new() { Tool = SkinTool.Fill, Surface = Front, Color = "#FF0000", Tolerance = 2, WrapFaces = true }], 1, TestContext.CancellationToken);
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(document, Front with { Face = SkinFace.Back }, 7, 7));
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(document, Front, 2, 2));
        Assert.AreEqual("#202020FF", SkinEditor.Sample(document, Front, 3, 3));
        Assert.AreEqual("#000000FF", SkinEditor.Sample(document, Front with { Part = SkinPart.Body }, 0, 0));
    }

    /// <summary>Adjacent-face samples must connect across the physical head edge, and symmetry must reflect faces rather than atlas neighbors.</summary>
    [TestMethod]
    public void WrappedStrokeAndMirror_TransportAcrossTheActualCuboid()
    {
        var left = Front with { Face = SkinFace.Left };
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic, "#000000"),
        [
            new()
            {
                Tool = SkinTool.Stroke, Surface = Front, Color = "#FF0000", WrapFaces = true, Mirror = SkinMirrorMode.Body,
                Points = [new(6, 4), new(2, 4, left)]
            }
        ], 0, TestContext.CancellationToken);
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(document, Front, 7, 4));
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(document, left, 0, 4));
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(document, left, 1, 4));
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(document, left, 2, 4));
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(document, Front with { Face = SkinFace.Right }, 5, 4));
        Assert.AreEqual("#000000FF", SkinEditor.Sample(document, Front with { Face = SkinFace.Back }, 0, 4));
    }

    /// <summary>The same brush footprint must wrap to the correct top/bottom/side texel, without painting arbitrary adjacent PNG regions.</summary>
    [TestMethod]
    [DataRow(0, 3, SkinFace.Right, 7, 3, 64)]
    [DataRow(7, 3, SkinFace.Left, 0, 3, 64)]
    [DataRow(3, 0, SkinFace.Top, 3, 7, 64)]
    [DataRow(3, 7, SkinFace.Bottom, 3, 0, 64)]
    [DataRow(0, 3, SkinFace.Right, 15, 3, 128)]
    [DataRow(15, 3, SkinFace.Left, 0, 3, 128)]
    [DataRow(3, 0, SkinFace.Top, 3, 15, 128)]
    [DataRow(3, 15, SkinFace.Bottom, 3, 0, 128)]
    public void EdgeBrush_WrapsToTheCorrectNeighbor(int x, int y, SkinFace neighbor, int neighborX, int neighborY, int size)
    {
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic, "#000000", size),
            [new() { Tool = SkinTool.Pencil, Surface = Front, X = x, Y = y, Color = "#FFFFFF", BrushSize = 3, WrapFaces = true }], 0, TestContext.CancellationToken);
        Assert.AreEqual("#FFFFFFFF", SkinEditor.Sample(document, Front with { Face = neighbor }, neighborX, neighborY));
        Assert.AreEqual("#000000FF", SkinEditor.Sample(document, Front with { Part = SkinPart.Body }, 0, 0));
    }

    /// <summary>Character symmetry must swap arm identity and reverse the model-specific width, including slim arms.</summary>
    [TestMethod]
    [DataRow(SkinModel.Classic, 3, 64)]
    [DataRow(SkinModel.Classic, 7, 128)]
    [DataRow(SkinModel.Slim, 2, 64)]
    [DataRow(SkinModel.Slim, 5, 128)]
    public void MirroredArm_UsesTheOppositeArmsActualWidth(SkinModel model, int mirroredX, int size)
    {
        var right = Front with { Part = SkinPart.RightArm };
        var document = SkinEditor.Apply(SkinEditor.Create(model, "#000000", size),
            [new() { Tool = SkinTool.Pencil, Surface = right, X = 0, Y = 5, Color = "#00FF00", Mirror = SkinMirrorMode.Body }], 0, TestContext.CancellationToken);
        Assert.AreEqual("#00FF00FF", SkinEditor.Sample(document, right with { Part = SkinPart.LeftArm }, mirroredX, 5));
        Assert.AreEqual("#000000FF", SkinEditor.Sample(document, right with { Part = SkinPart.LeftArm }, 0, 5));
    }

    // Author review: unscoped mirror tests cannot detect a hidden partner receiving a fill.
    // The full face result and its preview must agree on the same local or paired destination.
    /// <summary>Visible-part symmetry keeps fill and its footprint on the addressed cuboid or its visible counterpart.</summary>
    [TestMethod]
    [DataRow(false, SkinFace.Left, SkinFace.Right)]
    [DataRow(true, SkinFace.Left, SkinFace.Right)]
    [DataRow(false, SkinFace.Front, SkinFace.Back)]
    [DataRow(true, SkinFace.Front, SkinFace.Front)]
    [DataRow(false, SkinFace.Top, SkinFace.Bottom)]
    [DataRow(true, SkinFace.Top, SkinFace.Top)]
    public void VisiblePartMirror_FillAndFootprintUseOnlyTheDisplayedCounterpart(bool paired, SkinFace face, SkinFace mirroredFace)
    {
        var source = Front with { Part = SkinPart.RightArm, Face = face };
        var document = SkinEditor.Create(SkinModel.Slim, "#000000", 128);
        var edit = new SkinEdit
        {
            Tool = SkinTool.Fill,
            Surface = source,
            Color = "#FF0000",
            Mirror = SkinMirrorMode.Body,
            MirrorParts = paired ? [SkinPart.Head, SkinPart.Body, SkinPart.RightArm, SkinPart.LeftArm] : [SkinPart.Head, SkinPart.Body, SkinPart.RightArm],
        };
        var target = source with { Part = paired ? SkinPart.LeftArm : SkinPart.RightArm, Face = mirroredFace };
        var excluded = source with { Part = paired ? SkinPart.RightArm : SkinPart.LeftArm, Face = SkinFace.Back };
        var rectangle = SkinLayout.Face(document.Model, target, document.Size);

        var footprint = SkinEditor.BrushFootprint(document, edit, TestContext.CancellationToken);
        document = SkinEditor.Apply(document, [edit], 0, TestContext.CancellationToken);

        CollectionAssert.AreEquivalent(new[] { source, target }, footprint.Select(mark => mark.Surface).Distinct().ToArray());
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(document, target, rectangle.Width - 1, rectangle.Height - 1));
        Assert.AreEqual("#000000FF", SkinEditor.Sample(document, excluded, 0, 0));
        Assert.AreEqual("#000000FF", SkinEditor.Sample(document, Front with { Part = SkinPart.Body, Face = SkinFace.Bottom }, 0, 0));
    }

    /// <summary>Picking must distinguish the visible base from a blank editable outer shell, and its coordinates must drive the same drawing engine.</summary>
    [TestMethod]
    public void RayPick_RespectsTransparencyAndPartsAndFeedsStrokeCoordinates()
    {
        var document = SkinEditor.Create(SkinModel.Slim, "#000000");
        var request = new SkinPickRequest(new(0, 28, 20), new(0, 0, -1));
        var visible = SkinGeometry.Pick(document, request);
        Assert.IsNotNull(visible);
        Assert.AreEqual(Front, visible.Surface);
        Assert.AreEqual(16f, visible.Distance);
        var outer = SkinGeometry.Pick(document, request with { Layer = SkinLayer.Outer, SkipTransparent = false });
        Assert.IsNotNull(outer);
        Assert.AreEqual(SkinLayer.Outer, outer.Surface.Layer);
        Assert.AreEqual(15.5f, outer.Distance);
        document = SkinEditor.Apply(document,
        [
            new() { Tool = SkinTool.Stroke, Surface = outer.Surface, Points = [new(outer.LocalX, outer.LocalY)], Color = "#FF0000" }
        ], 0, TestContext.CancellationToken);
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(document, outer.Surface, outer.X, outer.Y));
        Assert.AreEqual(outer.Surface, SkinGeometry.Pick(document, request)!.Surface);
        Assert.IsNull(SkinGeometry.Pick(document, request with { Parts = [SkinPart.Body] }));
        Assert.ThrowsExactly<ArgumentException>(() => SkinGeometry.Pick(document, request with { Direction = new(0, 0, 0) }));
    }

    /// <summary>A ray on a closed face edge must return usable stroke coordinates, regardless of nonzero direction magnitude.</summary>
    [TestMethod]
    [DataRow(1f)]
    [DataRow(1e-20f)]
    public void RayOnFaceEdge_ReturnsPaintableCoordinates(float magnitude)
    {
        var document = SkinEditor.Create(SkinModel.Classic);
        var hit = SkinGeometry.Pick(document, new(new(4, 28, 20), new(0, 0, -magnitude), SkinLayer.Base));
        Assert.IsNotNull(hit);
        Assert.AreEqual(7, hit.X);
        document = SkinEditor.Apply(document,
            [new() { Tool = SkinTool.Stroke, Surface = hit.Surface, Points = [new(hit.LocalX, hit.LocalY)], Color = "#FF0000" }], 0, TestContext.CancellationToken);
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(document, hit.Surface, hit.X, hit.Y));
    }

    public TestContext TestContext { get; set; }
}