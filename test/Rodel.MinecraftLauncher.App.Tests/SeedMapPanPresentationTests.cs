// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.SeedExplorer;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Semantics;
using Sprout.Widgets;
using System.Numerics;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Crosses the App's transient transform with real Sprout paint, input and accessibility; geographic glyphs and fixed chrome are synthetic.</summary>
[TestClass]
public sealed class SeedMapPanPresentationTests
{
    /// <summary>Moving only pixels leaves hit regions and automation behind. The actual App helper must move all three projections together, preserve retained widgets, and restore them on cancel while a fixed sibling stays put.</summary>
    [TestMethod]
    public void Drag_WorldContentAndHitGeometryStayTogether_FixedControlsAndRetainedIdentityStayPut()
    {
        var world = new[]
        {
            new Glyph(1, new(10, 11, 12, 255), 20),
            new Glyph(2, new(20, 21, 22, 255), 30),
            new Glyph(3, new(30, 31, 32, 255), 40),
            new Glyph(4, new(40, 41, 42, 255), 50),
            new Glyph(5, new(50, 51, 52, 255), 60),
        };
        using var fixedControl = new Glyph(6, new(60, 61, 62, 255), 4);
        using var content = new WorldFixture(world);
        fixedControl.Mount(); content.Mount();
        var layer = new TransformWidget(content.Render);
        var scene = new StackWidget();
        scene.InsertChild(0, layer); scene.InsertChild(1, fixedControl.Render);
        var hits = new InteractionNode(ElementId.Of(100), Rect.FromXywh(0, 0, 200, 160), HitTestBehavior.DeferToChild);
        hits.Add(content.Interaction!); hits.Add(fixedControl.Interaction!);
        LayoutPass.Run(scene, BoxConstraints.Tight(new(200, 160)));
        var retainedTerrain = world[0].Render;
        var retainedMarker = world[2].Render;
        var initial = Paint(scene);
        Assert.HasCount(6, initial, "All five geographic roles and the fixed control must be in their intended paint trees.");

        SeedMapPanPresentation.Offset(layer, content.Interaction, content.Semantics, new(24, -10));
        var moved = Paint(scene);
        foreach (var part in world)
        {
            Assert.AreEqual(initial[part.Color].Left + 24, moved[part.Color].Left);
            Assert.AreEqual(initial[part.Color].Top - 10, moved[part.Color].Top);
        }

        Assert.AreEqual(initial[fixedControl.Color], moved[fixedControl.Color]);
        Assert.AreSame(retainedTerrain, world[0].Render);
        Assert.AreSame(retainedMarker, world[2].Render);
        Assert.AreSame(world[2].Interaction, hits.HitTest(new(68, 34)));
        Assert.AreEqual(Rect.FromXywh(64, 30, 8, 8), world[2].Semantics!.TransformedBounds);

        SeedMapPanPresentation.Offset(layer, content.Interaction, content.Semantics, default);
        var canceled = Paint(scene);
        foreach (var part in world) Assert.AreEqual(initial[part.Color], canceled[part.Color]);

        Assert.AreSame(world[2].Interaction, hits.HitTest(new(44, 44)));
        Assert.AreEqual(Rect.FromXywh(40, 40, 8, 8), world[2].Semantics!.TransformedBounds);
    }

    /// <summary>Pre-clamping an off-screen selection edge creates a false viewport-edge anchor: the temporary drag shifts it, then commit clamps it again and jumps. The actual selection projection must remain linear; the outer map Clip owns clipping.</summary>
    [TestMethod]
    public void SelectionSpanningViewport_PreservesWorldAnchorsWhenPanCommits()
    {
        var left = SeedMapPanPresentation.Project(-400, 0, 2, 200);
        var right = SeedMapPanPresentation.Project(400, 0, 2, 200);
        Assert.AreEqual(-100, left); Assert.AreEqual(300, right);

        // A rightward 40-DIP drag commits a center 80 world blocks to the left.
        Assert.AreEqual(left + 40, SeedMapPanPresentation.Project(-400, -80, 2, 200));
        Assert.AreEqual(right + 40, SeedMapPanPresentation.Project(400, -80, 2, 200));
    }

    private static Dictionary<Color, Rect> Paint(Widget control)
    {
        var renderer = new MockSceneRenderer();
        control.Paint(renderer);
        var result = new Dictionary<Color, Rect>();
        var transforms = new Stack<Matrix3x2>();
        var current = Matrix3x2.Identity;
        foreach (var command in renderer.Commands)
        {
            switch (command)
            {
                case PushTransformCommand push:
                    transforms.Push(current); current = push.Transform * current; break;
                case PopTransformCommand:
                    current = transforms.Pop(); break;
                case FillRectangleCommand fill when fill.Brush.AsSolid is { } color:
                    var point = Vector2.Transform(new(fill.Rect.Left, fill.Rect.Top), current);
                    result.Add(color, Rect.FromXywh(point.X, point.Y, fill.Rect.Width, fill.Rect.Height));
                    break;
            }
        }

        return result;
    }

    private sealed class WorldFixture(Glyph[] parts) : Control
    {
        /// <inheritdoc/>
        protected override void OnMount()
        {
            var render = new StackWidget();
            var bounds = Rect.FromXywh(0, 0, 200, 160);
            var interaction = new InteractionNode(ElementId.Of(1000), bounds, HitTestBehavior.DeferToChild);
            var semantics = new SemanticsNode(ElementId.Of(1000), SemanticsRole.Group, "Geographic overlays", bounds);
            for (var index = 0; index < parts.Length; index++)
            {
                var part = parts[index]; part.Mount();
                render.InsertChild(index, part.Render);
                interaction.Add(part.Interaction!); semantics.Add(part.Semantics!);
            }

            SetProjection(render, interaction, semantics);
        }

        /// <inheritdoc/>
        protected override void OnDispose() { foreach (var part in parts) part.Dispose(); }
    }

    private sealed class Glyph(int id, Color color, float anchor) : Control
    {
        internal Color Color => color;
        /// <inheritdoc/>
        protected override void OnMount()
        {
            var bounds = Rect.FromXywh(anchor, anchor, 8, 8);
            SetProjection(new GlyphWidget(color, bounds), new InteractionNode(ElementId.Of(id), bounds),
                new SemanticsNode(ElementId.Of(id), SemanticsRole.Button, "Geographic test glyph", bounds));
        }
    }

    private sealed class GlyphWidget(Color color, Rect anchor) : Widget
    {
        /// <inheritdoc/>
        protected override Size MeasureOverride(BoxConstraints constraints, LayoutContext context) => constraints.Constrain(new(200, 160));
        /// <inheritdoc/>
        public override void Paint(ISceneRenderer renderer) => renderer.FillRectangle(Rect.FromXywh(Bounds.Left + anchor.Left, Bounds.Top + anchor.Top, anchor.Width, anchor.Height), color);
    }
}