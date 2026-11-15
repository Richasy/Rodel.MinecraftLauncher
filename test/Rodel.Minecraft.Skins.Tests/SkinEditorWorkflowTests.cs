// Copyright (c) Richasy. All rights reserved.

using StbImageSharp;
using System.Text.Json;

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Protects editor gestures and cross-configuration recovery that the original fixed-size drawing tests cannot express.</summary>
[TestClass]
public sealed class SkinEditorWorkflowTests
{
    private static readonly SkinSurface Front = new() { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };

    /// <summary>HD reduction must sample the visible composite without blending hard edges or losing outer alpha; existing PNG cases cover only native sizes. A colored 2x2 block distinguishes center sampling from top-left sampling or averaging, and serialized state detects destructive resizing/history loss.</summary>
    [TestMethod]
    [DataRow(SkinModel.Classic)]
    [DataRow(SkinModel.Slim)]
    public void HdExport64_SamplesCompositeAndPreservesEditableProject(SkinModel model)
    {
        var document = SkinEditor.Apply(SkinEditor.Create(model, "#000000", 128),
            [new() { Tool = SkinTool.Pencil, Surface = Front, Color = "#FFFFFF" }], 0, TestContext.CancellationToken);
        var layer = new uint[128 * 128];
        // Head front: output (8,8) covers source (16,16)..(17,17).
        layer[(16 * 128) + 16] = 0xFF0000FF;
        layer[(16 * 128) + 17] = 0x00FF00FF;
        layer[(17 * 128) + 16] = 0x0000FFFF;
        layer[(17 * 128) + 17] = 0x123456FF;
        // Head overlay has a transparent background, so the sampled alpha must survive.
        layer[(17 * 128) + 81] = 0xABCDEF80;
        document.Layers.Add(new("export", "Visible paint", layer));
        var hidden = new uint[128 * 128];
        hidden[(17 * 128) + 17] = 0xFFFFFFFF;
        document.Layers.Add(new("hidden", "Hidden paint", hidden, Visible: false));
        var before = JsonSerializer.Serialize(document, SkinJsonContext.Default.SkinDocument);

        var image = ImageResult.FromMemory(SkinPng.Encode(document, 64), ColorComponents.RedGreenBlueAlpha);
        Assert.AreEqual((64, 64), (image.Width, image.Height));
        CollectionAssert.AreEqual(new byte[] { 0x12, 0x34, 0x56, 0xFF }, image.Data.Skip(((8 * 64) + 8) * 4).Take(4).ToArray());
        CollectionAssert.AreEqual(new byte[] { 0xAB, 0xCD, 0xEF, 0x80 }, image.Data.Skip(((8 * 64) + 40) * 4).Take(4).ToArray());
        Assert.AreEqual(0, image.Data[3]);
        Assert.AreEqual(before, JsonSerializer.Serialize(document, SkinJsonContext.Default.SkinDocument));
        var original = ImageResult.FromMemory(SkinPng.Encode(document), ColorComponents.RedGreenBlueAlpha);
        Assert.AreEqual((128, 128), (original.Width, original.Height));
        var standard = SkinEditor.Create(model);
        CollectionAssert.AreEqual(SkinPng.Encode(standard), SkinPng.Encode(standard, 64));
    }

    /// <summary>Adjacent HD ray centers must change different PNG texels, rather than painting an enlarged 64px macro-pixel.</summary>
    [TestMethod]
    public void HdMicroPixels_IndependentRaysRemainIndependentInPng()
    {
        var document = SkinEditor.Create(SkinModel.Slim, "#000000", 128);
        SkinHit Pick(float x) => SkinGeometry.Pick(document, new(new(x, 31.75f, 20), new(0, 0, -1), SkinLayer.Base))!;
        var first = Pick(-3.75f);
        var second = Pick(-3.25f);
        Assert.AreEqual((0, 0), (first.X, first.Y));
        Assert.AreEqual((1, 0), (second.X, second.Y));
        document = SkinEditor.Apply(document,
            [new() { Tool = SkinTool.Pencil, Surface = Front, X = first.X, Y = first.Y, Color = "#FF0000" },
             new() { Tool = SkinTool.Pencil, Surface = Front, X = second.X, Y = second.Y, Color = "#00FF00" }], 0, TestContext.CancellationToken);
        var image = ImageResult.FromMemory(SkinPng.Encode(document), ColorComponents.RedGreenBlueAlpha);
        CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 0, 255 },
            image.Data.Skip(((16 * 128) + 16) * 4).Take(12).ToArray());
    }

    /// <summary>One HD edge texel is half a world unit; the cursor and raster must agree without skipping the first neighboring texel.</summary>
    [TestMethod]
    public void HdWrappedBrush_FootprintMatchesThePhysicalEdgeAndPreview()
    {
        var source = SkinEditor.Create(SkinModel.Classic, "#000000", 128);
        SkinEdit edit = new() { Tool = SkinTool.Pencil, Surface = Front, X = 15, Y = 4, BrushSize = 3, WrapFaces = true, Color = "#FFFFFF" };
        var marks = SkinEditor.BrushFootprint(source, edit, TestContext.CancellationToken);
        var preview = SkinEditor.Preview(source, [edit], 0, TestContext.CancellationToken);
        var left = Front with { Face = SkinFace.Left };
        Assert.AreEqual("#FFFFFFFF", SkinEditor.Sample(preview, left, 0, 4));
        Assert.AreEqual("#000000FF", SkinEditor.Sample(preview, left, 1, 4));
        foreach (var mark in marks)
            Assert.AreEqual("#FFFFFFFF", SkinEditor.Sample(preview, mark.Surface, mark.X, mark.Y));
        Assert.HasCount(9, marks);
        Assert.AreEqual(0L, source.Revision);
        Assert.IsEmpty(preview.Undo);
        Assert.AreEqual(0L, preview.Revision);
        Assert.AreEqual("#000000FF", SkinEditor.Sample(source, left, 0, 4));
        var committed = SkinEditor.Apply(source, [edit], 0, TestContext.CancellationToken);
        CollectionAssert.AreEqual(preview.Pixels, committed.Pixels);
        Assert.HasCount(1, committed.Undo);
    }

    /// <summary>Dense eraser samples must not erase overlapping pixels repeatedly within one gesture; prior brush coverage tests cover painting only.</summary>
    [TestMethod]
    [DataRow(2)]
    [DataRow(41)]
    public void EraserPath_SampleDensityDoesNotAccumulateAlphaLoss(int samples)
    {
        var outer = Front with { Layer = SkinLayer.Outer };
        var source = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic),
            [new() { Tool = SkinTool.Fill, Surface = outer, Color = "#FF0000", FillMode = SkinFillMode.Face }], 0, TestContext.CancellationToken);
        var points = Enumerable.Range(0, samples).Select(i => new SkinStrokePoint(2 + (2.0 * i / (samples - 1)), 3)).ToArray();
        var edited = SkinEditor.Apply(source,
            [new() { Tool = SkinTool.Eraser, Surface = outer, Points = points, BrushSize = 3, Opacity = 0.5 }], 1, TestContext.CancellationToken);
        Assert.AreEqual("#FF000080", SkinEditor.Sample(edited, outer, 3, 3));
        Assert.AreEqual("#FF000080", SkinEditor.Sample(edited, outer, 5, 3));
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(edited, outer, 6, 3));
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(SkinEditor.Restore(edited, 2, cancellationToken: TestContext.CancellationToken), outer, 3, 3));
    }

    /// <summary>A persisted timeline must restore model/size/layers together, retain future after a no-op, and fork only on actual new content.</summary>
    [TestMethod]
    public void ImportedConfiguration_HistoryJumpAndForkPreserveCompleteContent()
    {
        var original = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic, "#112233"),
            [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Set, Id = "background", Name = "Original" } }], 0, TestContext.CancellationToken);
        var imported = SkinEditor.ReplaceContent(original, SkinTemplates.Create("alex", SkinModel.Slim, 128), 1, cancellationToken: TestContext.CancellationToken);
        var reopened = JsonSerializer.Deserialize(JsonSerializer.Serialize(imported, SkinJsonContext.Default.SkinDocument), SkinJsonContext.Default.SkinDocument)!;
        reopened.Validate();
        var prior = SkinEditor.RestoreTo(reopened, 1, 2, TestContext.CancellationToken);
        Assert.AreEqual((SkinModel.Classic, 64, "Original"), (prior.Model, prior.Size, prior.BackgroundName));
        var unchanged = SkinEditor.Apply(prior, [new() { Tool = SkinTool.Pencil, Surface = Front, Color = "#112233" }], 3, TestContext.CancellationToken);
        Assert.AreSame(prior, unchanged);
        var future = SkinEditor.RestoreTo(unchanged, 2, 3, TestContext.CancellationToken);
        Assert.AreEqual((SkinModel.Slim, 128), (future.Model, future.Size));
        CollectionAssert.AreEqual(imported.Pixels, future.Pixels);
        var back = SkinEditor.RestoreTo(future, 1, 4, TestContext.CancellationToken);
        var fork = SkinEditor.Apply(back, [new() { Tool = SkinTool.Pencil, Surface = Front, Color = "#FFFFFF" }], 5, TestContext.CancellationToken);
        Assert.IsEmpty(fork.Redo);
        Assert.AreEqual(6L, fork.Revision);
        Assert.ThrowsExactly<InvalidOperationException>(() => SkinEditor.RestoreTo(fork, 0, 5, TestContext.CancellationToken));
    }

    /// <summary>Packaged person seeds must preserve chosen arm/size, original clothing colors and transparent outer surfaces in every supported configuration.</summary>
    [TestMethod]
    [DataRow(SkinModel.Classic, 64)]
    [DataRow(SkinModel.Slim, 64)]
    [DataRow(SkinModel.Classic, 128)]
    [DataRow(SkinModel.Slim, 128)]
    public void PersonTemplates_PackageIndependentEditablePixels(SkinModel model, int size)
    {
        var body = Front with { Part = SkinPart.Body };
        foreach (var id in SkinTemplates.Ids.Where(id => SkinTemplates.SuggestedModel(id) != SkinModel.Cape))
        {
            var document = SkinTemplates.Create(id, model, size);
            document.Validate();
            Assert.AreEqual((model, size), (document.Model, document.Size));
            Assert.AreEqual("#00000000", SkinEditor.Sample(document, body with { Layer = SkinLayer.Outer }, 0, 0));
            var reloaded = SkinPng.Decode(SkinPng.Encode(document), model);
            CollectionAssert.AreEqual(document.Pixels, reloaded.Pixels);
        }

        Assert.AreEqual("#269DA3FF", SkinEditor.Sample(SkinTemplates.Create("classic", model, size), body, 3 * size / 64, 4 * size / 64));
        Assert.AreEqual("#74A65EFF", SkinEditor.Sample(SkinTemplates.Create("alex", model, size), body, 3 * size / 64, 4 * size / 64));
        var changed = SkinTemplates.Create("alex", model, size);
        changed.Pixels[0] = 1;
        Assert.AreEqual(0u, SkinTemplates.Create("alex", model, size).Pixels[0]);
    }

    // Author review: one flat fill hides face boundaries in blank templates. Distinct face interiors
    // and a lighter logical-pixel edge detect that defect at both resolutions, while transparent
    // gutters, PNG round trips and fresh copies protect independently editable character/cape data.
    /// <summary>Blank character and cape templates distinguish faces and edges without losing editable pixels.</summary>
    [TestMethod]
    [DataRow("cape", SkinModel.Cape, 64)]
    [DataRow("cape", SkinModel.Cape, 128)]
    [DataRow("blank", SkinModel.Classic, 64)]
    [DataRow("blank", SkinModel.Classic, 128)]
    [DataRow("blank", SkinModel.Slim, 64)]
    [DataRow("blank", SkinModel.Slim, 128)]
    public void BlankTemplates_SeparateFacesAndRetainEditablePixels(string template, SkinModel model, int size)
    {
        Assert.AreEqual(SkinModel.Cape, SkinTemplates.SuggestedModel("cape"));
        var document = SkinTemplates.Create(template, model, size);
        document.Validate();
        Assert.AreEqual((model, size, model == SkinModel.Cape ? size / 2 : size), (document.Model, document.Size, document.Height));
        var outside = new SkinSurface { Part = model == SkinModel.Cape ? SkinPart.Cape : SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };
        var inset = 2 * size / 64;
        var center = SkinEditor.Sample(document, outside, inset, inset);
        Assert.AreNotEqual(center, SkinEditor.Sample(document, outside with { Face = SkinFace.Back }, inset, inset));
        Assert.AreNotEqual(center, SkinEditor.Sample(document, outside, 0, inset));
        Assert.AreEqual(0u, document.Pixels[0]);
        CollectionAssert.AreEqual(document.Pixels, SkinPng.Decode(SkinPng.Encode(document), model).Pixels);
        document.Pixels[0] = 1;
        Assert.AreEqual(0u, SkinTemplates.Create(template, model, size).Pixels[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => SkinTemplates.Create("cape", SkinModel.Classic, size));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkinTemplates.Create("classic", SkinModel.Cape, size));
    }

    /// <summary>Gets or sets test cancellation for isolated template and editing operations.</summary>
    public TestContext TestContext { get; set; }
}