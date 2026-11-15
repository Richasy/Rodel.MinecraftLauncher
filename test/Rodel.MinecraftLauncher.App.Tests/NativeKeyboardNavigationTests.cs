// Copyright (c) Richasy. All rights reserved.

extern alias NativeApp;

using Sprout;
using Sprout.Backend.D2D;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Semantics;
using NativeImages = NativeApp::Rodel.MinecraftLauncher.App.Features.Images;
using NativeSettings = NativeApp::Rodel.MinecraftLauncher.App.Features.Settings;
using NativeSkins = NativeApp::Rodel.MinecraftLauncher.App.Features.Skins;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Exercises the actual App projections and callbacks, rather than substitutes for their keyboard wiring.</summary>
[TestClass]
public sealed class NativeKeyboardNavigationTests
{
    private static readonly string[] expected = ["launch"];

    /// <summary>Category arrows move within one group without navigating until activation, and reveal the final category.</summary>
    [TestMethod]
    public void SettingsCategories_ArrowsRevealWithoutInvoking_AndTabLeavesOnce()
    {
        // Framework tests cover traversal; this case protects the App's actual grouping, callback and scroll placement.
        var choices = new List<string>();
        WithScroll(new NativeSettings.SettingsNavigation(choices.Add), 200f, 160f, (scroll, keyboard, focus, after) =>
        {
            Focus(scroll, focus, "settings.category.general");
            Assert.IsTrue(keyboard.Dispatch(Down(Key.ArrowDown)));
            AssertFocused(scroll, focus, "settings.category.launch");
            Assert.IsEmpty(choices);
            Assert.IsTrue(keyboard.Dispatch(Down(Key.Enter)));
            Assert.IsTrue(keyboard.Dispatch(new(KeyEventKind.Up, Key.Enter, Repeat: false)));
            CollectionAssert.AreEqual(expected, choices);

            Assert.IsTrue(keyboard.Dispatch(Down(Key.End)));
            AssertFocused(scroll, focus, "settings.category.about");
            Assert.IsGreaterThan(0f, scroll.Offset.Y);
            Assert.IsLessThanOrEqualTo(scroll.Render.Bounds.Bottom + .5f, focus.Focused!.TransformedHitRegion.Bottom);
            Assert.IsTrue(keyboard.Dispatch(Down(Key.Tab)));
            Assert.AreSame(after, focus.Focused);
            Assert.IsTrue(keyboard.Dispatch(Down(Key.Tab) with { Modifiers = KeyModifiers.Shift }));
            AssertFocused(scroll, focus, "settings.category.about");
        });
    }

    /// <summary>Panel arrows must not silently switch editor state, and keyboard activation uses the real toggle callback.</summary>
    [TestMethod]
    public void SkinTabs_ArrowFocusAndActivationRemainDistinct()
    {
        var state = new NativeSkins.SkinEditorState { Height = 800f };
        WithScroll(new NativeSkins.SkinEditorTabs(state), 320f, 100f, (scroll, keyboard, focus, after) =>
        {
            Focus(scroll, focus, "skin.tab.edit");
            Assert.IsTrue(keyboard.Dispatch(Down(Key.ArrowRight)));
            AssertFocused(scroll, focus, "skin.tab.ai");
            Assert.AreEqual(0, state.Panel);
            Assert.IsTrue(keyboard.Dispatch(Down(Key.Space)));
            Assert.IsTrue(keyboard.Dispatch(new(KeyEventKind.Up, Key.Space, Repeat: false)));
            Assert.AreEqual(1, state.Panel);
            Assert.IsTrue(keyboard.Dispatch(Down(Key.Tab)));
            Assert.AreSame(after, focus.Focused);
        });
    }

    /// <summary>Crop arrows edit the current crop exactly once, without also scrolling its containing dialog.</summary>
    [TestMethod]
    public void ImageCrop_ConsumesItsArrowAndLetsTabLeave()
    {
        var moves = new List<(double X, double Y)>();
        WithScroll(new NativeImages.ImageCropSurface((x, y) => moves.Add((x, y))), 260f, 120f,
            (scroll, keyboard, focus, after) =>
            {
                Focus(scroll, focus, "image.crop");
                Assert.IsTrue(keyboard.Dispatch(Down(Key.ArrowDown)));
                CollectionAssert.AreEqual(new[] { (0d, .04d) }, moves);
                Assert.AreEqual(0f, scroll.Offset.Y, "The same arrow must not also pan the parent scroll view.");
                Assert.IsTrue(keyboard.Dispatch(Down(Key.Tab)));
                Assert.AreSame(after, focus.Focused);
            });
    }

    private static void WithScroll(Control content, float width, float height,
        Action<ScrollView, KeyboardRouter, FocusManager, InteractionNode> check)
    {
        var backend = new D2DBackend();
        using var device = backend.CreateDevice(new RenderDeviceOptions(PreferSoftware: true));
        var measurer = backend.CreateTextMeasurer(device);
        using var scroll = new ScrollView(content);
        var focus = new FocusManager();
        try
        {
            scroll.SetRootId(ElementId.Of(1));
            scroll.Mount();
            LayoutPass.Run(scroll.Render, BoxConstraints.Tight(new Size(width, height)), new LayoutContext(measurer));
            scroll.SyncGeometry();
            Assert.IsNotNull(scroll.Interaction);
            var root = new InteractionNode(ElementId.Of(20), default);
            _ = root.Add(scroll.Interaction);
            var after = root.Add(new InvokableInteraction(ElementId.Of(21), default));
            var keyboard = new KeyboardRouter(focus) { Root = root };
            check(scroll, keyboard, focus, after);
        }
        finally
        {
            focus.ClearFocus();
            (measurer as IDisposable)?.Dispose();
        }
    }

    private static KeyEvent Down(Key key) => new(KeyEventKind.Down, key, Repeat: false);

    private static void Focus(Control control, FocusManager focus, string id)
    {
        var node = FindInteraction(control.Interaction!, FindSemantic(control.Semantics!, id).StableId);
        Assert.IsNotNull(node);
        Assert.IsTrue(focus.SetFocus(node, FocusState.Programmatic));
    }

    private static void AssertFocused(Control control, FocusManager focus, string id) =>
        Assert.AreEqual(FindSemantic(control.Semantics!, id).StableId, focus.Focused?.StableId);

    private static SemanticsNode FindSemantic(SemanticsNode root, string id) =>
        Descendants(root).Single(node => node.AutomationId == id);

    private static IEnumerable<SemanticsNode> Descendants(SemanticsNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            foreach (var node in Descendants(child))
                yield return node;
        }
    }

    private static InteractionNode? FindInteraction(InteractionNode root, ElementId id) =>
        root.StableId == id ? root : root.Children.Select(child => FindInteraction(child, id)).FirstOrDefault(node => node is not null);
}