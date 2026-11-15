// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Ai;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.Rendering.Tests;

/// <summary>Measures real layout widgets without a window; input delivery and native composition require separate validation.</summary>
[TestClass]
public sealed class TranscriptLayoutTests
{
    /// <summary>Parts inside a turn need their actual expanded height in the first parent measure. This checks the pinned framework package rather than assuming a newer checkout's nested-layout behavior.</summary>
    [TestMethod]
    public void InlineParts_MeasureCompleteHeightBeforeArrange()
    {
        var source = new PartsSource();
        var panel = new VirtualizingPanel(new StackLayout { Spacing = 14f }, source);
        var measured = panel.Measure(new(Size.Empty, new(400f, float.PositiveInfinity)), LayoutContext.Measureless);
        Assert.AreEqual(614f, measured.Height);
    }

    private sealed class PartsSource : IVirtualizingSource
    {
        /// <inheritdoc/>
        public int Count => 2;
        /// <inheritdoc/>
        public Widget Realize(int index) { var item = new TurnWidget(); item.SetHeight(index == 0 ? 500f : 100f); return item; }
        /// <inheritdoc/>
        public void Recycle(int key, Widget element) { }
    }

    /// <summary>Learning one tall turn must not rescale all unvisited history slots. Running-average layouts change both the extent and later positions here, even on a pure scroll; only in-window widgets should be requested.</summary>
    [TestMethod]
    public void TallTurn_DoesNotRescaleUnseenHistoryOrRealizeAllTurns()
    {
        var context = new TurnsContext(100);
        var layout = new AiTurnLayout(32f);
        context.Items[0].SetHeight(1000f);
        layout.Arrange(context, Rect.FromXywh(0f, 0f, 400f, 20000f));
        Assert.HasCount(1, context.Requested);
        var extent = layout.Measure(context, new(Size.Empty, new(400f, float.PositiveInfinity))).Height;
        Assert.AreEqual(1000f + (99 * 160f) + (99 * 32f), extent);
        context.Window = Rect.FromXywh(0f, 1032f, 400f, 100f);
        context.Requested.Clear();
        layout.Arrange(context, Rect.FromXywh(0f, 0f, 400f, extent));
        Assert.HasCount(1, context.Requested);
        Assert.Contains(1, context.Requested);
        Assert.AreEqual(1032f, context.Items[1].Bounds.Top);
    }

    /// <summary>Expand/collapse followed by leaving and revisiting a turn must retain its measured slot and exact successor origin; unlike the tall-turn case this checks changed and cached heights, without an average correction moving the scroll.</summary>
    [TestMethod]
    public void ExpandedTurn_ReentryRetainsItsSlotAndCollapseUpdatesTheSuffix()
    {
        var context = new TurnsContext(3);
        var layout = new AiTurnLayout(32f);
        context.Items[0].SetHeight(1000f);
        layout.Arrange(context, Rect.FromXywh(0f, 0f, 400f, 3000f));
        context.Window = Rect.FromXywh(0f, 1032f, 400f, 100f);
        layout.Arrange(context, Rect.FromXywh(0f, 0f, 400f, 3000f));
        Assert.AreEqual(1032f, context.Items[1].Bounds.Top);
        Assert.IsTrue(layout.ReconcileFrom(new AiTurnLayout(32f)));
        context.Window = Rect.FromXywh(0f, 0f, 400f, 100f);
        context.Items[0].SetHeight(64f);
        layout.Arrange(context, Rect.FromXywh(0f, 0f, 400f, 3000f));
        Assert.AreEqual(96f, context.Items[1].Bounds.Top);
        Assert.AreEqual(64f + 320f + 64f, layout.Measure(context, new(Size.Empty, new(400f, float.PositiveInfinity))).Height);
    }

    private sealed class TurnsContext(int count) : VirtualizingLayoutContext
    {
        internal TurnWidget[] Items { get; } = [.. Enumerable.Range(0, count).Select(_ => new TurnWidget())];
        internal HashSet<int> Requested { get; } = [];
        internal Rect Window { get; set; } = Rect.FromXywh(0f, 0f, 400f, 100f);
        /// <inheritdoc/>
        public override int ItemCount => Items.Length;
        /// <inheritdoc/>
        public override Rect RealizationRect => Window;
        /// <inheritdoc/>
        public override LayoutContext LayoutContext => LayoutContext.Measureless;
        /// <inheritdoc/>
        public override Widget GetOrCreateElementAt(int index) { Requested.Add(index); return Items[index]; }
        /// <inheritdoc/>
        public override void RecycleElement(int index) { }
    }
    private sealed class TurnWidget : Widget
    {
        private float _height = 160f;
        internal void SetHeight(float height) { _height = height; Invalidate(WidgetDirty.Layout); }
        protected override Size MeasureOverride(BoxConstraints constraints, LayoutContext context) => new(400f, _height);
    }
}