// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Protects saved editor state and transaction boundaries without relying on launcher storage or CLI processes.</summary>
[TestClass]
public sealed class SkinDocumentTests
{
    private static readonly SkinSurface Front = new() { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };

    /// <summary>A real file round trip must retain redo pixels, labels, visibility, opacity and locks, not only the currently visible PNG.</summary>
    [TestMethod]
    public async Task ProjectRoundTrip_RetainsEditableRedoStateAndLayerProtection()
    {
        using var sandbox = new SkinSandbox();
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Slim, "#112233"),
        [
            new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "coat", Name = "Working" } },
            new() { Tool = SkinTool.Pencil, Surface = Front, PaintLayerId = "coat", Color = "#FF000080" }
        ], 0, TestContext.CancellationToken);
        document = SkinEditor.Apply(document,
            [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Set, Id = "coat", Name = "Saved", Opacity = 0.25, Visible = false, Locked = true } }], 1, TestContext.CancellationToken);
        document = SkinEditor.Restore(document, 2, cancellationToken: TestContext.CancellationToken);
        var path = Path.Combine(sandbox.Root, "editable.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document, SkinJsonContext.Default.SkinDocument), TestContext.CancellationToken);
        var restored = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, TestContext.CancellationToken), SkinJsonContext.Default.SkinDocument);
        Assert.IsNotNull(restored);
        restored.Validate();
        var replayed = SkinEditor.Restore(restored, 3, redo: true, TestContext.CancellationToken);
        var layer = replayed.Layers.Single();
        Assert.AreEqual("Saved", layer.Name);
        Assert.AreEqual(0.25, layer.Opacity);
        Assert.IsFalse(layer.Visible);
        Assert.IsTrue(layer.Locked);
        Assert.AreEqual("#FF000080", SkinEditor.Sample(replayed, Front, 0, 0, "coat"));
        Assert.AreEqual("#112233FF", SkinEditor.Sample(replayed, Front, 0, 0));
        Assert.ThrowsExactly<InvalidOperationException>(() => SkinEditor.Apply(replayed,
            [new() { Tool = SkinTool.Pencil, Surface = Front, PaintLayerId = "coat", Color = "#FFFFFF" }], 4, TestContext.CancellationToken));
        var undone = SkinEditor.Restore(replayed, 4, cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(undone.Layers.Single().Visible);
        Assert.IsFalse(undone.Layers.Single().Locked);
    }

    /// <summary>Cancellation requested during batch input must prevent publication of a partially processed texture/layer state.</summary>
    [TestMethod]
    public void CancellationBetweenActions_DoesNotCommitPixelsOrLayerState()
    {
        using var cancellation = new CancellationTokenSource();
        var document = SkinEditor.Create(SkinModel.Classic, "#112233");
        var edits = new CancelBetweenEdits(
            new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "temporary" } },
            new() { Tool = SkinTool.Pencil, Surface = Front, Color = "#FF0000" }, cancellation);
        Assert.ThrowsExactly<OperationCanceledException>(() => SkinEditor.Apply(document, edits, 0, cancellation.Token));
        Assert.IsTrue(edits.BoundaryReached, "Cancellation must happen during enumeration, not before the transaction is invoked.");
        Assert.IsEmpty(document.Layers);
        Assert.IsEmpty(document.Undo);
        Assert.AreEqual("#112233FF", SkinEditor.Sample(document, Front, 0, 0));
        Assert.AreEqual(0L, document.Revision);
    }

    /// <summary>Malformed retained history and duplicate layer identities must be rejected before a later edit or undo can consume them.</summary>
    [TestMethod]
    [DataRow("history")]
    [DataRow("duplicate-layer")]
    [DataRow("base-alpha")]
    public void DamagedProjectState_IsRejectedRatherThanDefaulted(string damage)
    {
        var document = SkinEditor.Create(SkinModel.Classic);
        if (damage == "history")
            document.Undo.Add(new(new uint[1], [], SkinModel.Classic, 64, "Background", new("create", "system")));
        else if (damage == "duplicate-layer")
            document.Layers.AddRange([new("same", "First", new uint[4096]), new("same", "Second", new uint[4096])]);
        else
            document.Pixels[(8 * 64) + 8] = 0;
        Assert.ThrowsExactly<InvalidDataException>(document.Validate);
    }

    /// <summary>Gets or sets the test context supplying cooperative cancellation for file operations.</summary>
    public TestContext TestContext { get; set; }
}