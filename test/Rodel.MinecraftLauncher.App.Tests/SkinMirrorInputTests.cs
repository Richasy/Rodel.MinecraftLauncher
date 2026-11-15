// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Features.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using Rodel.MinecraftLauncher.Core.Storage;
using Sprout.Graphics;
using Sprout.Input;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects visibility-driven mirror routing through actual shared editor pointer input.</summary>
[TestClass]
public sealed class SkinMirrorInputTests
{
    // Author review: library Body/Part tests cannot catch the App always sending Body.
    // Real Down/Up, draft commit and raw layer pixels show which cuboid was painted.
    // Only the existing packaged controller/window peers are substituted.
    /// <summary>Single visible limbs paint and erase their own opposite face; showing their partner restores paired symmetry.</summary>
    [TestMethod]
    [DataRow(SkinModel.Classic, 64, SkinPart.RightArm, SkinPart.LeftArm)]
    [DataRow(SkinModel.Slim, 128, SkinPart.LeftArm, SkinPart.RightArm)]
    [DataRow(SkinModel.Classic, 128, SkinPart.RightLeg, SkinPart.LeftLeg)]
    [DataRow(SkinModel.Slim, 64, SkinPart.LeftLeg, SkinPart.RightLeg)]
    public async Task IsolatedLimb_ChangesTheActualStrokeTargetWithoutPaintingTheHiddenLimb(
        SkinModel model, int size, SkinPart part, SkinPart opposite)
    {
        using var scope = new StateTestScope();
        var (session, state, host, input) = await CreateAsync(scope, model, size);
        state.Parts = 1 << (int)part;
        var left = new SkinSurface { Part = part, Layer = SkinLayer.Base, Face = SkinFace.Left };
        var right = left with { Face = SkinFace.Right };
        var hidden = right with { Part = opposite };
        var width = SkinLayout.Face(model, right, size).Width;

        await StrokeAsync(input, host, session, [new(1, 5, left)]);
        Assert.AreEqual(0xFF2040FFu, Pixel(session, right, width - 2, 5));
        Assert.AreEqual(0u, Pixel(session, hidden, width - 2, 5), "An isolated limb must not write the hidden opposite limb.");
        state.Tool = 1;
        state.Parts |= (1 << (int)SkinPart.Head) | (1 << (int)SkinPart.Body);
        await StrokeAsync(input, host, session, [new(1, 5, left)]);
        Assert.AreEqual(0u, Pixel(session, left, 1, 5));
        Assert.AreEqual(0u, Pixel(session, right, width - 2, 5), "Showing the head and torso must not change an unpaired limb's local mirror.");

        state.Parts = (1 << (int)part) | (1 << (int)opposite);
        state.Tool = 0;
        await StrokeAsync(input, host, session, [new(1, 7, left)]);
        Assert.AreEqual(0xFF2040FFu, Pixel(session, hidden, width - 2, 7), "Two visible counterparts must mirror each other.");
        Assert.AreEqual(0u, Pixel(session, right, width - 2, 7));
    }

    // Author review: side-to-side probes passed the old bilateral fallback, which placed two
    // cursors on the same front/top face. Literal opposite faces and texel coordinates protect
    // physical reflection, including the different UV axis of top/bottom, in hover and persistence.
    /// <summary>An unpaired limb mirrors through the addressed cuboid's opposite face, never across that same face.</summary>
    [TestMethod]
    [DataRow(SkinModel.Classic, 64, SkinFace.Front, SkinFace.Back, 3, 1)]
    [DataRow(SkinModel.Slim, 128, SkinFace.Back, SkinFace.Front, 5, 1)]
    [DataRow(SkinModel.Classic, 128, SkinFace.Left, SkinFace.Right, 7, 1)]
    [DataRow(SkinModel.Slim, 64, SkinFace.Right, SkinFace.Left, 3, 1)]
    [DataRow(SkinModel.Classic, 64, SkinFace.Top, SkinFace.Bottom, 0, 2)]
    [DataRow(SkinModel.Slim, 128, SkinFace.Bottom, SkinFace.Top, 0, 6)]
    public async Task UnpairedLimb_HoverAndStrokeReachThePhysicalOppositeFace(
        SkinModel model, int size, SkinFace face, SkinFace oppositeFace, int mirroredX, int mirroredY)
    {
        using var scope = new StateTestScope();
        var (session, state, host, input) = await CreateAsync(scope, model, size);
        state.Parts = (1 << (int)SkinPart.RightArm) | (1 << (int)SkinPart.Body);
        var surface = new SkinSurface { Part = SkinPart.RightArm, Layer = SkinLayer.Base, Face = face };
        var opposite = surface with { Face = oppositeFace };
        var snapshot = session.Render();
        var rectangle = SkinLayout.Face(model, surface, size);
        var hit = snapshot.PickAtlas(rectangle.X + .5, rectangle.Y + 1.5, SkinLayer.Base);
        Assert.IsNotNull(hit);
        input.Hover(new object(), hit);

        var marks = input.Overlay(snapshot).Marks;
        CollectionAssert.AreEquivalent(
            new[] { new SkinBrushMark(surface, 0, 1, 1), new SkinBrushMark(opposite, mirroredX, mirroredY, 1) },
            marks);

        await StrokeAsync(input, host, session, [new(0, 1, surface)]);

        Assert.AreEqual(0xFF2040FFu, Pixel(session, surface, 0, 1));
        Assert.AreEqual(0xFF2040FFu, Pixel(session, opposite, mirroredX, mirroredY));
        Assert.AreEqual(0u, Pixel(session, surface, rectangle.Width - 1, 1), "The old same-face cursor must not leave paint on that face's other edge.");
        Assert.AreEqual(0u, Pixel(session, opposite with { Part = SkinPart.LeftArm }, mirroredX, mirroredY));
    }

    // Per-gesture mode selection can pass isolated-limb cases yet send a later sample to a
    // hidden partner. One actual gesture crosses a head, torso, unpaired arm and paired legs.
    /// <summary>Mixed visible parts resolve symmetry per hit and never mirror between unrelated body parts.</summary>
    [TestMethod]
    public async Task MixedVisibleParts_KeepPerPartBoundariesThroughoutOneStroke()
    {
        using var scope = new StateTestScope();
        var (session, state, host, input) = await CreateAsync(scope, SkinModel.Classic, 64);
        state.Parts = 63 & ~(1 << (int)SkinPart.LeftArm);
        var head = new SkinSurface { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Top };
        var torso = head with { Part = SkinPart.Body, Face = SkinFace.Front };
        var arm = head with { Part = SkinPart.RightArm, Face = SkinFace.Left };
        var leg = arm with { Part = SkinPart.LeftLeg };
        var revision = session.Revision;

        await StrokeAsync(input, host, session, [new(1, 2, head), new(1, 2, torso), new(1, 2, arm), new(1, 2, leg)]);

        Assert.AreEqual(revision + 1, session.Revision);
        Assert.AreEqual(0xFF2040FFu, Pixel(session, head, 6, 2));
        Assert.AreEqual(0xFF2040FFu, Pixel(session, torso, 6, 2));
        Assert.AreEqual(0u, Pixel(session, torso with { Face = SkinFace.Bottom }, 6, 2), "The head's top never maps to the torso's bottom.");
        Assert.AreEqual(0xFF2040FFu, Pixel(session, arm with { Face = SkinFace.Right }, 2, 2));
        Assert.AreEqual(0u, Pixel(session, arm with { Part = SkinPart.LeftArm, Face = SkinFace.Right }, 2, 2));
        Assert.AreEqual(0xFF2040FFu, Pixel(session, leg with { Part = SkinPart.RightLeg, Face = SkinFace.Right }, 2, 2));
        Assert.AreEqual(0u, Pixel(session, leg with { Face = SkinFace.Right }, 2, 2));
    }

    private async Task<(SkinEditingSession Session, SkinEditorState State, SkinEditorController Host, SkinEditorInput Input)> CreateAsync(
        StateTestScope scope, SkinModel model, int size)
    {
        var service = new SkinService(new LauncherPaths(scope.Root));
        var id = await service.CreateAsync(model, size: size, cancellationToken: TestContext.CancellationToken);
        await service.EditAsync(id, 0,
            [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "paint", Name = "Paint" } }],
            TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        session.SelectLayer("paint");
        var state = new SkinEditorState { DocumentModel = model, Mirror = true, Foreground = "#FF2040FF" };
        var host = new SkinEditorController(session, state);
        return (session, state, host, new(host, state));
    }

    private static async Task StrokeAsync(SkinEditorInput input, SkinEditorController host, SkinEditingSession session, SkinStrokePoint[] points)
    {
        var snapshot = session.Render();
        var owner = new object();
        SkinHit? last = null;
        for (var index = 0; index < points.Length; index++)
        {
            var point = points[index];
            var surface = point.Surface!;
            var face = SkinLayout.Face(snapshot.Model, surface, snapshot.Size);
            last = snapshot.PickAtlas(face.X + point.X + .5, face.Y + point.Y + .5, surface.Layer, surface.Part);
            Assert.IsNotNull(last);
            input.Handle(owner, new(index == 0 ? PointerEventKind.Down : PointerEventKind.Move,
                new Point(0, 0), PointerButton.Primary, PointerDeviceType.Mouse), last, atlas: false);
        }

        input.Handle(owner, new(PointerEventKind.Up, new Point(0, 0), PointerButton.Primary, PointerDeviceType.Mouse), last, atlas: false);
        await host.Commit;
    }

    private static uint Pixel(SkinEditingSession session, SkinSurface surface, int x, int y)
    {
        var snapshot = session.Render();
        var face = SkinLayout.Face(snapshot.Model, surface, snapshot.Size);
        return session.LayerPixels("paint")[((face.Y + y) * snapshot.Size) + face.X + x];
    }

    /// <summary>Gets or sets the runner-owned cancellation context.</summary>
    public TestContext TestContext { get; set; } = null!;
}