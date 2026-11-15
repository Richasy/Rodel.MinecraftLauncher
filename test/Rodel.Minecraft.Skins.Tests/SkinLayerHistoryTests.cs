// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Protects non-destructive paint-layer composition and complete transaction-history restoration.</summary>
[TestClass]
public sealed class SkinLayerHistoryTests
{
    private static readonly SkinSurface Front = new() { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };

    /// <summary>Layer opacity/visibility must affect export without changing source pixels; merge undo must restore pixels and metadata together.</summary>
    [TestMethod]
    public void LayerCompositionAndMergeUndo_RestoreSourcePixelsAndSettings()
    {
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic, "#0000FF"),
        [
            new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "coat" } },
            new() { Tool = SkinTool.Pencil, Surface = Front, PaintLayerId = "coat", Color = "#FF0000", Opacity = 0.5 }
        ], 0, TestContext.CancellationToken);
        Assert.AreEqual("#FF000080", SkinEditor.Sample(document, Front, 0, 0, "coat"));
        Assert.AreEqual("#80007FFF", SkinEditor.Sample(document, Front, 0, 0));
        document = SkinEditor.Apply(document,
            [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Set, Id = "coat", Opacity = 0.5 } }], 1, TestContext.CancellationToken);
        Assert.AreEqual("#4000BFFF", SkinEditor.Sample(document, Front, 0, 0));
        var hidden = SkinEditor.Apply(document,
            [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Set, Id = "coat", Visible = false } }], 2, TestContext.CancellationToken);
        Assert.AreEqual("#0000FFFF", SkinEditor.Sample(hidden, Front, 0, 0));
        document = SkinEditor.Restore(hidden, 3, cancellationToken: TestContext.CancellationToken);
        var merged = SkinEditor.Apply(document,
            [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.MergeDown, Id = "coat" } }], 4, TestContext.CancellationToken);
        Assert.IsEmpty(merged.Layers);
        Assert.AreEqual("#4000BFFF", SkinEditor.Sample(merged, Front, 0, 0));
        var restored = SkinEditor.Restore(merged, 5, cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual(0.5, restored.Layers.Single().Opacity);
        Assert.AreEqual("#FF000080", SkinEditor.Sample(restored, Front, 0, 0, "coat"));
        Assert.AreEqual("#0000FFFF", SkinEditor.Sample(restored, Front, 0, 0, SkinLayers.BackgroundId));
        Assert.AreEqual("#4000BFFF", SkinEditor.Sample(SkinEditor.Restore(restored, 6, redo: true, TestContext.CancellationToken), Front, 0, 0));
    }

    /// <summary>A batch that locks a layer and then writes it must fail atomically, without leaking the lock into caller-owned state.</summary>
    [TestMethod]
    public void LockedLayerInMixedBatch_RejectsWithoutLeakingMetadata()
    {
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Slim),
            [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "details" } }], 0, TestContext.CancellationToken);
        Assert.ThrowsExactly<InvalidOperationException>(() => SkinEditor.Apply(document,
        [
            new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Set, Id = "details", Locked = true } },
            new() { Tool = SkinTool.Pencil, Surface = Front, PaintLayerId = "details", Color = "#FF0000" }
        ], 1, TestContext.CancellationToken));
        Assert.IsFalse(document.Layers.Single().Locked);
        Assert.AreEqual("#00000000", SkinEditor.Sample(document, Front, 0, 0, "details"));
        Assert.AreEqual(1L, document.Revision);
    }

    /// <summary>Duplicate, reorder and delete must preserve stack order rather than accidentally binding layers by list position.</summary>
    [TestMethod]
    public void LayerIdentity_SurvivesDuplicationReorderAndRemoval()
    {
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Classic, "#000000"),
        [
            new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "red" } },
            new() { Tool = SkinTool.Pencil, Surface = Front, PaintLayerId = "red", Color = "#FF0000" },
            new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "blue" } },
            new() { Tool = SkinTool.Pencil, Surface = Front, PaintLayerId = "blue", Color = "#0000FF" },
            new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Duplicate, Id = "copy", SourceId = "red" } }
        ], 0, TestContext.CancellationToken);
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(document, Front, 0, 0));
        document = SkinEditor.Apply(document,
            [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Reorder, Id = "copy", Index = 0 } }], 1, TestContext.CancellationToken);
        Assert.AreEqual("#0000FFFF", SkinEditor.Sample(document, Front, 0, 0));
        document = SkinEditor.Apply(document,
            [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Remove, Id = "blue" } }], 2, TestContext.CancellationToken);
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(document, Front, 0, 0));
        Assert.AreEqual("#00000000", SkinEditor.Sample(document, Front, 1, 0, "copy"));
    }

    public TestContext TestContext { get; set; }
}