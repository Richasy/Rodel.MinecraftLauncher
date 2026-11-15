// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Features.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using Rodel.MinecraftLauncher.Core.Storage;
using Sprout.Graphics;
using Sprout.Input;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects paint-click selection dismissal at the actual shared pointer owner, not just the library mask.</summary>
[TestClass]
public sealed class SkinSelectionInputTests
{
    // Author review: existing mask tests cannot detect a new draft, accidental fill dispatch or
    // a drag that paints after dismissal. Real Down/Move/Up and Core RGBA/history distinguish
    // the old masked stroke from consuming the click. Only packaged UI peers are substituted.
    /// <summary>The first outside gesture only dismisses; a fresh press edits normally in atlas and model input.</summary>
    [TestMethod]
    [DataRow(0, false, false)]
    [DataRow(1, false, false)]
    [DataRow(2, false, false)]
    [DataRow(0, true, false)]
    [DataRow(1, true, false)]
    [DataRow(2, true, false)]
    [DataRow(0, false, true)]
    public async Task OutsidePaintPress_DismissesWithoutEditingOrReusingTheGesture(int tool, bool atlas, bool background)
    {
        using var scope = new StateTestScope();
        var (session, state, host, input) = await CreateAsync(scope);
        var source = new object();
        Select(input, source, session, state, atlas);
        state.Tool = tool;
        var pixels = session.LayerPixels("paint");
        var revision = session.Revision;
        var history = session.History().Entries.Length;

        if (background)
            input.Handle(source, new(PointerEventKind.Down, new Point(0, 0), PointerButton.Primary, PointerDeviceType.Mouse), null, atlas);
        else
            Send(input, source, session, PointerEventKind.Down, 12, 9, atlas);
        Assert.IsNull(state.Selection);
        Assert.IsFalse(input.HasGesture, "Dismissal cannot capture a paint stroke.");
        Assert.IsFalse(session.HasDraft, "Even an unchanged masked draft would consume the wrong gesture.");
        Send(input, source, session, PointerEventKind.Move, 9, 9, atlas);
        Send(input, source, session, PointerEventKind.Up, 9, 9, atlas);
        CollectionAssert.AreEqual(pixels, session.LayerPixels("paint"));
        Assert.AreEqual(revision, session.Revision);
        Assert.HasCount(history, session.History().Entries);
        Assert.AreEqual(0, host.FillRequests);

        Send(input, source, session, PointerEventKind.Down, 12, 9, atlas);
        Send(input, source, session, PointerEventKind.Up, 12, 9, atlas);
        if (tool == 2)
        {
            Assert.AreEqual(1, host.FillRequests, "A fresh fill press must reach the controller; actual filling is tested separately.");
        }
        else
        {
            await host.Commit;
            Assert.AreEqual(tool == 1 ? 0u : 0xFF2040FFu, session.LayerPixels("paint")[(9 * 64) + 12]);
            Assert.AreEqual(revision + 1, session.Revision);
            Assert.HasCount(history + 1, session.History().Entries);
        }
    }

    /// <summary>Inside strokes retain the mask, while alternate buttons, navigation and picking do not dismiss it.</summary>
    [TestMethod]
    public async Task InsidePaintingAndNonPaintPresses_PreserveSelectionAndItsWriteBoundary()
    {
        using var scope = new StateTestScope();
        var (session, state, host, input) = await CreateAsync(scope);
        var source = new object();
        Select(input, source, session, state, atlas: true);
        var selection = state.Selection;
        var before = session.LayerPixels("paint");
        state.Tool = 0;
        Send(input, source, session, PointerEventKind.Down, 12, 9, true, PointerButton.Secondary);
        Send(input, source, session, PointerEventKind.Down, 12, 9, true, modifiers: KeyModifiers.Alt);
        state.Tool = 3;
        Send(input, source, session, PointerEventKind.Down, 12, 9, true);
        Assert.AreSame(selection, state.Selection);
        CollectionAssert.AreEqual(before, session.LayerPixels("paint"));
        Assert.IsFalse(session.HasDraft);

        state.Tool = 0;
        state.Foreground = "#FF2040FF";
        Send(input, source, session, PointerEventKind.Down, 9, 9, true);
        Send(input, source, session, PointerEventKind.Move, 12, 9, true);
        Send(input, source, session, PointerEventKind.Up, 12, 9, true);
        await host.Commit;
        Assert.AreSame(selection, state.Selection);
        var after = session.LayerPixels("paint");
        Assert.AreEqual(0xFF2040FFu, after[(9 * 64) + 9]);
        Assert.AreEqual(before[(9 * 64) + 12], after[(9 * 64) + 12], "Starting inside must not turn a later boundary crossing into unrestricted paint.");
    }

    private static async Task<(SkinEditingSession Session, SkinEditorState State, SkinEditorController Host, SkinEditorInput Input)> CreateAsync(StateTestScope scope)
    {
        var service = new SkinService(new LauncherPaths(scope.Root));
        var id = await service.CreateAsync(SkinModel.Classic, color: "#234567");
        await service.EditAsync(id, 0,
        [
            new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "paint", Name = "Paint" } },
            new() { Tool = SkinTool.Fill, PaintLayerId = "paint", Surface = new() { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front }, Color = "#234567" }
        ]);
        var session = service.OpenEditor(id);
        session.SelectLayer("paint");
        var state = new SkinEditorState { Foreground = "#FF2040FF" };
        var host = new SkinEditorController(session, state);
        return (session, state, host, new(host, state));
    }

    private static void Select(SkinEditorInput input, object source, SkinEditingSession session, SkinEditorState state, bool atlas)
    {
        // Literal UV coordinates lie within the head front (8..15, 8..15), not face-local coordinates.
        // This exercises actual rectangle routing before switching tools.
        state.Tool = 4;
        Send(input, source, session, PointerEventKind.Down, 9, 9, atlas);
        Send(input, source, session, PointerEventKind.Up, 10, 10, atlas);
        Assert.AreEqual(4, state.Selection?.Count);
    }

    private static void Send(SkinEditorInput input, object source, SkinEditingSession session, PointerEventKind kind, int x, int y, bool atlas,
        PointerButton button = PointerButton.Primary, KeyModifiers modifiers = KeyModifiers.None)
    {
        var snapshot = session.Render();
        var hit = snapshot.PickAtlas(x + .5f, y + .5f, SkinLayer.Base);
        Assert.IsNotNull(hit, "The fixture must address a real mapped texel.");
        input.Handle(source, new(kind, new Point(x, y), button, PointerDeviceType.Mouse, Modifiers: modifiers),
            hit, atlas, new(x, y, hit.Surface));
    }
}