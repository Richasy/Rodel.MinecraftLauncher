// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Exercises a bounded mixed editing session and inverse history behavior beyond the retained window.</summary>
[TestClass]
public sealed class SkinComplexHistoryTests
{
    private static readonly SkinSurface Front = new() { Part = SkinPart.Head, Layer = SkinLayer.Outer, Face = SkinFace.Front };

    /// <summary>Mixed pixels/layers, periodic reopening and history rollover must return exact retained states, not just a plausible final preview.</summary>
    [TestMethod]
    public async Task MixedSession_ReopenUndoAndRedoPreserveTheRetainedHistoryWindow()
    {
        using var sandbox = new SkinSandbox();
        var document = SkinEditor.Apply(SkinEditor.Create(SkinModel.Slim, "#112233"),
        [
            new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "ink" } },
            new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "shade" } }
        ], 0, TestContext.CancellationToken);
        var states = new List<SkinSnapshot> { Record(document) };
        var path = Path.Combine(sandbox.Root, "session.json");
        for (var step = 0; step < 48; step++)
        {
            // Changing real layer opacity makes each transaction observable even when its geometric selection is empty.
            var edits = new List<SkinEdit>
            {
                new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Set, Id = "ink", Opacity = (step + 1) / 64.0 } }
            };
            edits.AddRange(SessionAction(step));
            document = SkinEditor.Apply(document, edits, document.Revision, TestContext.CancellationToken);
            document.Validate();
            states.Add(Record(document));
            if ((step + 1) % 8 == 0)
                document = await ReopenAsync(document, path);
        }

        var finalRevision = document.Revision;
        var finalPng = SkinPng.Encode(document);
        for (var step = 0; step < 32; step++)
            document = SkinEditor.Restore(document, document.Revision, cancellationToken: TestContext.CancellationToken);
        AssertContent(states[16], document);
        Assert.ThrowsExactly<InvalidOperationException>(() => SkinEditor.Restore(document, document.Revision, cancellationToken: TestContext.CancellationToken));
        document = await ReopenAsync(document, path);
        for (var step = 0; step < 32; step++)
            document = SkinEditor.Restore(document, document.Revision, redo: true, TestContext.CancellationToken);
        AssertContent(states[^1], document);
        CollectionAssert.AreEqual(finalPng, SkinPng.Encode(document));
        Assert.IsGreaterThan(finalRevision, document.Revision, "Undo/redo must not resurrect a revision already held by another client.");
        Assert.ThrowsExactly<InvalidOperationException>(() => SkinEditor.Apply(document, SessionAction(1), finalRevision, TestContext.CancellationToken));
    }

    private static SkinEdit[] SessionAction(int step) => (step % 6) switch
    {
        0 => [new() { Tool = SkinTool.Stroke, Surface = Front, PaintLayerId = "ink", Points = [new(0, 1), new(7, 6)],
            Color = $"#{(0x204080u + (uint)(step * 997)):X6}", BrushShape = SkinBrushShape.Circle, BrushSize = 3, Opacity = 0.5, Mirror = SkinMirrorMode.Body }],
        1 => [new() { Tool = SkinTool.Gradient, Surface = Front, PaintLayerId = "shade", X2 = 7, Y2 = 7,
            Color = "#FFCC4480", SecondaryColor = "#0044FF20", Selection = new(1, 1, 6, 6) { Shape = SkinSelectionShape.Ellipse } }],
        2 => [new() { Tool = SkinTool.Copy, Surface = Front with { Face = SkinFace.Back }, Source = Front,
            PaintLayerId = "ink", SourceSelection = new(1, 1, 3, 2), X = 2, Y = 2, QuarterTurns = 1 }],
        3 => [new() { Tool = SkinTool.AdjustColor, Surface = Front, PaintLayerId = "shade", Hue = 35, Brightness = -0.02 }],
        4 => [
            new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Duplicate, Id = "copy", SourceId = "ink" } },
            new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.MergeDown, Id = "copy" } }
        ],
        _ => [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Set, Id = "shade", Visible = step % 12 == 5 } }]
    };

    private async Task<SkinDocument> ReopenAsync(SkinDocument document, string path)
    {
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document, SkinJsonContext.Default.SkinDocument), TestContext.CancellationToken);
        var restored = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, TestContext.CancellationToken), SkinJsonContext.Default.SkinDocument);
        Assert.IsNotNull(restored);
        restored.Validate();
        return restored;
    }

    // Recorded states are an oracle for inverse-history behavior, not expected results for the drawing algorithms.
    private static SkinSnapshot Record(SkinDocument document) => new((uint[])document.Pixels.Clone(),
        [.. document.Layers.Select(layer => layer with { Pixels = (uint[])layer.Pixels.Clone() })],
        document.Model, document.Size, document.BackgroundName, document.Transaction);

    private static void AssertContent(SkinSnapshot expected, SkinDocument actual)
    {
        Assert.AreEqual((expected.Model, expected.Size, expected.BackgroundName), (actual.Model, actual.Size, actual.BackgroundName));
        CollectionAssert.AreEqual(expected.Pixels, actual.Pixels);
        Assert.HasCount(expected.Layers.Length, actual.Layers);
        for (var index = 0; index < expected.Layers.Length; index++)
        {
            var first = expected.Layers[index];
            var second = actual.Layers[index];
            Assert.AreEqual((first.Id, first.Name, first.Visible, first.Opacity, first.Locked),
                (second.Id, second.Name, second.Visible, second.Opacity, second.Locked));
            CollectionAssert.AreEqual(first.Pixels, second.Pixels);
        }
    }

    /// <summary>Gets or sets the test context supplying cooperative cancellation.</summary>
    public TestContext TestContext { get; set; }
}