// Copyright (c) Richasy. All rights reserved.

extern alias NativeApp;
using Rodel.Minecraft.Skins;
using Sprout;
using Sprout.Backend.D2D;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Semantics;
using Sprout.Widgets;
using System.Reflection;
using NativeAi = NativeApp::Rodel.MinecraftLauncher.App.Features.Ai;
using NativeButtons = NativeApp::Rodel.MinecraftLauncher.App.Controls.Buttons;
using NativeHome = NativeApp::Rodel.MinecraftLauncher.App.Features.Home;
using NativeShell = NativeApp::Rodel.MinecraftLauncher.App.Features.Shell;
using NativeSkins = NativeApp::Rodel.MinecraftLauncher.App.Features.Skins;
using NativeTheme = NativeApp::Rodel.MinecraftLauncher.App.Theming;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Measures real App row and navigation projections with DirectWrite, without opening application windows.</summary>
[TestClass]
public sealed class NativeTextSizingTests
{
    /// <summary>Skin panel tabs must fill the strip above its bottom indicator at ordinary and enlarged text sizes.</summary>
    [TestMethod]
    [DataRow(1f, 800f)]
    [DataRow(1f, 720f)]
    [DataRow(2.25f, 800f)]
    [DataRow(2.25f, 720f)]
    public void SkinTabs_FillHeaderAndKeepIndicatorAtBottom(float factor, float windowHeight)
    {
        // Existing row cases protect clipping, not empty space inside a minimum-height navigation strip.
        // Mount the actual tabs: an outer-only minimum leaves the indicator, hit areas and text at the top.
        using var tabs = new NativeSkins.SkinEditorTabs(new() { Height = windowHeight });
        var backend = new D2DBackend();
        using var device = backend.CreateDevice(new RenderDeviceOptions(PreferSoftware: true));
        var measurer = backend.CreateTextMeasurer(device);
        try
        {
            tabs.Mount();
            Arrange(tabs.Render, 320f, new LayoutContext(measurer, textScaleFactor: factor, defaultFontFamily: "Microsoft YaHei UI"));
            tabs.SyncGeometry();
            var indicators = Descendants(tabs.Render).OfType<RectangleWidget>()
                .Where(shape => shape.Bounds.Height is > 0f and <= 2f).ToArray();
            Assert.HasCount(3, indicators);
            foreach (var indicator in indicators)
            {
                Assert.AreEqual(tabs.Render.Bounds.Bottom - 1f, indicator.Bounds.Bottom, .5f,
                    "The selection indicator must touch the strip's bottom border, not float above blank space.");
            }

            var tabBottom = indicators[0].Bounds.Top;
            var center = (tabs.Render.Bounds.Top + tabBottom) / 2f;
            Assert.IsNotNull(tabs.Semantics);
            string[] ids = ["skin.tab.edit", "skin.tab.ai", "skin.tab.history"];
            foreach (var id in ids)
            {
                var button = Descendants(tabs.Semantics).Single(node => node.AutomationId == id);
                Assert.IsTrue(button.Bounds.Top <= tabs.Render.Bounds.Top + .5f && button.Bounds.Bottom >= tabBottom - .5f,
                    "Each tab's interaction area must fill the row above the selection indicator.");
            }

            var labels = Descendants(tabs.Render).OfType<TextWidget>().ToArray();
            Assert.HasCount(3, labels);
            foreach (var label in labels)
            {
                Assert.AreEqual(center, label.Bounds.Top + (label.Bounds.Height / 2f), .5f,
                    "Text must remain centered in the complete tab, including an icon-and-text tab.");
                var metrics = label.GetLayoutMetrics();
                Assert.IsNotNull(metrics);
                Assert.IsLessThanOrEqualTo(label.Bounds.Height + .5f, metrics.Value.ContentBounds.Height,
                    "The tab must still grow enough for enlarged text.");
            }
        }
        finally
        {
            (measurer as IDisposable)?.Dispose();
        }
    }

    /// <summary>Availability-aware selectors must ellipsize their real item projection rather than clip the label.</summary>
    [TestMethod]
    [DataRow(1f)]
    [DataRow(2.25f)]
    public void ChoiceItems_LongUnavailableVersionUsesEllipsis(float factor)
    {
        // The native string-source defaults do not cover the App's separate availability-aware template.
        // Measure that actual projection at the version menu's finite width; a plain clipped Text cannot pass.
        const string label = "1.21.11 · No installed instance is available for this version";
        using var choice = NativeButtons.LauncherControls.Choice(
            NativeTheme.LauncherAppearance.Capture(), [label], [false]);
        var sourceField = typeof(ComboBox).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(sourceField);
        var source = sourceField.GetValue(choice) as ComboBoxItemSource;
        Assert.IsNotNull(source);
        var backend = new D2DBackend();
        using var device = backend.CreateDevice(new RenderDeviceOptions(PreferSoftware: true));
        var measurer = backend.CreateTextMeasurer(device);
        try
        {
            WithView(source.ContentViewAt(0), render =>
            {
                Arrange(render, 92f, new LayoutContext(measurer, textScaleFactor: factor, defaultFontFamily: "Microsoft YaHei UI"));
                var text = Descendants(render).OfType<TextWidget>().Single(item => item.Text == label);
                var metrics = text.GetLayoutMetrics();
                Assert.IsNotNull(metrics);
                Assert.IsTrue(metrics.Value.IsTrimmed, "A long disabled choice must indicate omitted text, not clip its last glyph.");
                Assert.AreEqual(1, metrics.Value.LineCount);
            });
        }
        finally
        {
            (measurer as IDisposable)?.Dispose();
        }
    }

    /// <summary>A long real AI transaction label must fit beside the current-history marker with an ellipsis.</summary>
    [TestMethod]
    [DataRow(1f)]
    [DataRow(2.25f)]
    public void SkinHistory_LongTransactionLabelUsesEllipsis(float factor)
    {
        // Recent-project and AI-conversation row tests protect height, not this history template's star column.
        // Keep the current marker visible so the label must obey its remaining width at both text scales.
        const string label = "Repaint the right sleeve and preserve the original shading and transparent outer layer";
        var entry = new SkinHistoryEntry(3, true, SkinModel.Slim, 128, new("brush", "ai", label));
        var view = new NativeSkins.SkinHistoryRow(NativeTheme.LauncherAppearance.Capture(), entry, false);
        var backend = new D2DBackend();
        using var device = backend.CreateDevice(new RenderDeviceOptions(PreferSoftware: true));
        var measurer = backend.CreateTextMeasurer(device);
        try
        {
            WithView(view, render =>
            {
                Arrange(render, 240f, new LayoutContext(measurer, textScaleFactor: factor, defaultFontFamily: "Microsoft YaHei UI"));
                var text = Descendants(render).OfType<TextWidget>().Single(item => item.Text == label);
                var metrics = text.GetLayoutMetrics();
                Assert.IsNotNull(metrics);
                Assert.IsTrue(metrics.Value.IsTrimmed, "The transaction must show an ellipsis before the current-state marker.");
                Assert.AreEqual(1, metrics.Value.LineCount);
                Assert.IsLessThanOrEqualTo(render.Bounds.Right, text.Bounds.Right);
            });
        }
        finally
        {
            (measurer as IDisposable)?.Dispose();
        }
    }

    /// <summary>The real checkbox template must give a long confirmation label finite width and enough height at each text size.</summary>
    [TestMethod]
    [DataRow(1f)]
    [DataRow(2.25f)]
    public void Checkbox_LongConfirmationWrapsInsideItsContainer(float factor)
    {
        // Review: Wrap alone cannot help when the outer checkbox measures its content unbounded.
        // Real App projection and DirectWrite expose that boundary without opening or accepting a delete dialog.
        const string label = "Delete this instance's game files as well. This action cannot be undone.";
        var backend = new D2DBackend();
        using var device = backend.CreateDevice(new RenderDeviceOptions(PreferSoftware: true));
        var measurer = backend.CreateTextMeasurer(device);
        try
        {
            var view = new NativeButtons.LauncherCheckBox(NativeTheme.LauncherAppearance.Capture(),
                label, false, static () => { }, true, "checkbox.confirmation");
            WithView(view, render =>
            {
                Arrange(render, 320f, new LayoutContext(measurer, textScaleFactor: factor, defaultFontFamily: "Microsoft YaHei UI"));
                var text = Descendants(render).OfType<TextWidget>().Single(item => item.Text == label);
                var metrics = text.GetLayoutMetrics();
                Assert.IsNotNull(metrics);
                Assert.IsGreaterThanOrEqualTo(2, metrics.Value.LineCount, "The complete confirmation must wrap, not paint outside the dialog.");
                Assert.IsFalse(metrics.Value.IsTrimmed);
                Assert.IsTrue(text.Bounds.Left >= render.Bounds.Left && text.Bounds.Right <= render.Bounds.Right + .5f);
                Assert.IsTrue(text.Bounds.Top >= render.Bounds.Top && text.Bounds.Bottom <= render.Bounds.Bottom + .5f);
                Assert.IsTrue(metrics.Value.ContentBounds.Width <= text.Bounds.Width + .5f
                    && metrics.Value.ContentBounds.Height <= text.Bounds.Height + .5f);
            });
        }
        finally
        {
            (measurer as IDisposable)?.Dispose();
        }
    }

    /// <summary>Scaled titles and subtitles must both remain inside naturally sized history and recent-project rows.</summary>
    [TestMethod]
    [DataRow(1f)]
    [DataRow(2.25f)]
    public void RecordRows_ContainBothTextLines(float factor) => StateTestScope.Run(async () =>
    {
        // Review: business-state tests cannot detect a fixed outer button cropping its real text. Use actual App
        // projections and native font metrics; the two rows have independent owners and previously fixed heights.
        using var scope = new StateTestScope();
        await scope.Client.Skins.CreateAsync(SkinModel.Classic, template: "classic", name: "Long character project", cancellationToken: TestContext.CancellationToken);
        var backend = new D2DBackend();
        using var device = backend.CreateDevice(new RenderDeviceOptions(PreferSoftware: true));
        var measurer = backend.CreateTextMeasurer(device);
        try
        {
            var context = new LayoutContext(measurer, textScaleFactor: factor, defaultFontFamily: "Microsoft YaHei UI");
            using var history = new NativeAi.AiHistoryRow(
                new("chat", "chat", "Long Minecraft conversation", "instance", "Minecraft instance",
                    false, true, NativeAi.AiHistoryBadge.None, false),
                static _ => { }, static _ => null, 184f);
            history.Mount();
            Arrange(history.Render, 184f, context);
            AssertTextContained(history.Render);

            var project = scope.Client.Skins.List().Single();
            var view = new NativeSkins.SkinRecentProject(NativeTheme.LauncherAppearance.Capture(), null!, project, DateTimeOffset.UtcNow);
            WithView(view, render =>
            {
                Arrange(render, 280f, context);
                AssertTextContained(render);
            });
        }
        finally
        {
            (measurer as IDisposable)?.Dispose();
        }
    });

    /// <summary>Navigation keeps short words intact and bounds zoom instead of wrapping characters or shrinking long translations indefinitely.</summary>
    [TestMethod]
    [DataRow("游戏", true)]
    [DataRow("AI Assistant", false)]
    [DataRow("مساعد الذكاء الاصطناعي", false)]
    public void Navigation_UsesAtMostTwoLinesAndBoundedZoom(string label, bool mustFitWhole)
    {
        // Review: checking MaxLines in source would miss shaping and the outer Viewbox transform.
        // Read actual DirectWrite lines and the final zoom at the same 225% setting as the reported failure.
        var backend = new D2DBackend();
        using var device = backend.CreateDevice(new RenderDeviceOptions(PreferSoftware: true));
        var measurer = backend.CreateTextMeasurer(device);
        try
        {
            var appearance = NativeTheme.LauncherAppearance.Capture();
            var view = new NativeShell.NavItem(appearance, label, "home", false, static () => { }, appearance.Buttons.Navigation(false));
            WithView(view, render =>
            {
                Arrange(render, 58f, new LayoutContext(measurer, textScaleFactor: 2.25f, defaultFontFamily: "Microsoft YaHei UI"));
                var text = Descendants(render).OfType<TextWidget>().Single(item => item.Text == label);
                var metrics = text.GetLayoutMetrics();
                Assert.IsNotNull(metrics);
                Assert.IsTrue(metrics.Value.LineCount is >= 1 and <= 2, "Navigation must never become a tall column of split characters.");
                var zoom = Descendants(render).OfType<ViewboxWidget>().Single().ChildScaleTransform.M11;
                Assert.IsTrue(zoom is >= .799f and <= 1f, "Zoom is bounded at 80% of the requested text size.");
                if (mustFitWhole)
                {
                    Assert.AreEqual(1, metrics.Value.LineCount);
                    Assert.IsFalse(metrics.Value.IsTrimmed, "A short navigation word must remain complete on one line.");
                }
            });
        }
        finally
        {
            (measurer as IDisposable)?.Dispose();
        }
    }

    /// <summary>Home selection scrolling follows the current realized row after scale changes and recycling, not a fixed or average pitch.</summary>
    [TestMethod]
    public void HomeSelection_RevealsVariableRowsAfterTextScaleChanges()
    {
        // Review: row-only checks do not cross the scroller's estimated-to-realized handoff. Unequal row heights,
        // a live factor change and an out/back selection require the actual selected rectangle to fit the viewport.
        var backend = new D2DBackend();
        using var device = backend.CreateDevice(new RenderDeviceOptions(PreferSoftware: true));
        var measurer = backend.CreateTextMeasurer(device);
        var rows = new PoolingItemFactory<int, IView>([.. Enumerable.Range(0, 20)], static index => index,
            static index => Markup.Text($"Row {index}: " + new string('x', 5 + (index % 3 * 12))).FontSize(12f + (index % 3 * 3f)).Wrap(),
            autoProject: true);
        var repeater = new ItemsRepeater(rows, new StackLayout { EstimatedItemExtent = 79f, Spacing = 8f });
        using var scroll = new ScrollView(repeater);
        scroll.Mount();
        using var reveal = new NativeHome.HomeListSelectionReveal(repeater, scroll);
        reveal.Attach();
        try
        {
            float[] factors = [1f, 2.25f];
            foreach (var factor in factors)
            {
                var context = new LayoutContext(measurer, textScaleFactor: factor, defaultFontFamily: "Microsoft YaHei UI");
                var size = new Size(240f, 260f);
                scroll.Render.Measure(BoxConstraints.Tight(size), context);
                scroll.Render.Arrange(Rect.FromXywh(0f, 0f, size.Width, size.Height));
                scroll.SyncGeometry();
                int[] targets = [17, 0, 17];
                foreach (var index in targets)
                {
                    Assert.IsTrue(reveal.TryReveal(index, index), "The selected row must be realized and revealed.");
                    var target = Descendants(repeater.Render).OfType<TextWidget>()
                        .Single(text => text.Text.StartsWith($"Row {index}: ", StringComparison.Ordinal));
                    var top = target.Bounds.Top - repeater.Render.Bounds.Top;
                    Assert.IsTrue(top >= scroll.Offset.Y - .5f
                        && top + target.Bounds.Height <= scroll.Offset.Y + scroll.ViewportSize.Height + .5f,
                        "The measured selected row must fit wholly inside the viewport after the handoff.");
                }
            }
        }
        finally
        {
            (measurer as IDisposable)?.Dispose();
        }
    }

    private static void WithView<TView>(TView view, Action<Widget> check) where TView : IView
    {
        var factory = new PoolingItemFactory<int, TView>([0], _ => view, autoProject: true);
        var item = factory.Realize(0, ElementId.Of(1));
        try { check(item.Render); }
        finally { factory.Recycle(0, item); }
    }

    private static void Arrange(Widget render, float width, LayoutContext context)
    {
        var desired = render.Measure(BoxConstraints.Loose(new Size(width, float.PositiveInfinity)), context);
        render.Arrange(Rect.FromXywh(0f, 0f, width, desired.Height));
    }

    private static void AssertTextContained(Widget render)
    {
        var texts = Descendants(render).OfType<TextWidget>().Where(text => text.Text.Length > 0).ToArray();
        Assert.IsGreaterThanOrEqualTo(2, texts.Length, "The real title and secondary text must both be present.");
        foreach (var text in texts)
        {
            Assert.IsTrue(text.Bounds.Top >= render.Bounds.Top && text.Bounds.Bottom <= render.Bounds.Bottom + .5f,
                $"Text '{text.Text}' must remain within its row.");
            var metrics = text.GetLayoutMetrics();
            Assert.IsNotNull(metrics);
            Assert.IsTrue(metrics.Value.LineCount > 0 && metrics.Value.ContentBounds.Height <= text.Bounds.Height + .5f,
                $"Text '{text.Text}' must have enough height for its complete shaped line.");
        }
    }

    private static IEnumerable<Widget> Descendants(Widget root)
    {
        yield return root;
        var children = new List<Widget>();
        root.VisitChildren(children.Add);
        foreach (var child in children)
        {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static IEnumerable<SemanticsNode> Descendants(SemanticsNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    /// <summary>Provides runner cancellation for the isolated Core setup.</summary>
    public TestContext TestContext { get; set; } = null!;
}