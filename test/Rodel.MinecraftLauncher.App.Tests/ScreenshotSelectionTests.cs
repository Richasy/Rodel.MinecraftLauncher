// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Content;
using Rodel.MinecraftLauncher.Core.Screenshots;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects the gallery's exact selected-file mapping and its native selection-versus-open policy, not Core file operations.</summary>
[TestClass]
public sealed class ScreenshotSelectionTests
{
    /// <summary>
    /// Review: Enter must activate the focused image rather than a different selected image, without stealing Space,
    /// text-input or popup keys. This uses the production binding and real command/row dispatch; no window or OS input is created.
    /// </summary>
    [TestMethod]
    public async Task KeyboardActivation_UsesGalleryFocusAndPreservesOtherKeyOwners()
    {
        ScreenshotFile[] files = [File("a", "selected.png", 1), File("a", "focused.png", 2)];
        using var store = new ScreenshotStore((_, _) => Task.FromResult(new ScreenshotCatalog("a", true, files, [])));
        await store.SelectAsync("a");
        var opened = new List<ScreenshotFile>();
        var factory = new SelectableItemFactory<ScreenshotFile, TextView>(
            files, file => Array.IndexOf(files, file), static file => Sprout.Markup.Text(file.FileName));
        using var list = new ItemsView(factory, new StackLayout { ItemExtent = 40f });
        var controller = new ScreenshotSelectionController(store, list, opened.Add);
        controller.SetFiles(files);
        list.SetRootId(ElementId.Of(19));
        list.Mount();
        LayoutPass.Run(list.Render, BoxConstraints.Tight(new Size(400f, 200f)), LayoutContext.Measureless);
        list.SyncGeometry();
        list.OnItemPrimary(0, ctrl: false, shift: false);
        var focused = factory.GetItemInteraction(1);
        Assert.IsNotNull(focused);
        var commands = new CommandManager();
        using var binding = controller.BindKeyboard(commands, () => focused);
        var dispatcher = new CommandDispatcher(commands)
        {
            FocusPath = () =>
            {
                var path = new List<KeyContext>();
                for (var node = focused; node is not null; node = node.Parent)
                    if (node.KeyContext is { } context) path.Add(context);
                path.Reverse();
                return path;
            },
            FocusedBubble = key => focused?.OnKeyEvent(key).Handled == true,
        };
        var enter = new KeyEvent(KeyEventKind.Down, Key.Enter, false);
        Assert.AreEqual(KeyDispatchStage.ScopedCommand, dispatcher.Dispatch(enter));
        CollectionAssert.AreEqual(new[] { files[1] }, opened);
        CollectionAssert.AreEqual(new[] { files[0] }, store.Selected.ToArray());
        dispatcher.Dispatch(enter with { Kind = KeyEventKind.Up });
        dispatcher.Dispatch(new(KeyEventKind.Down, Key.Space, false));
        dispatcher.Dispatch(new(KeyEventKind.Up, Key.Space, false));
        CollectionAssert.AreEqual(new[] { files[1] }, store.Selected.ToArray());
        CollectionAssert.AreEqual(new[] { files[1] }, opened);
        dispatcher.Dispatch(enter with { Repeat = true });
        dispatcher.Dispatch(enter with { Kind = KeyEventKind.Up });
        CollectionAssert.AreEqual(new[] { files[1] }, opened);

        foreach (var context in new[] { KeyContexts.TextInput, KeyContexts.Popup })
        {
            focused.KeyContext = new(context);
            Assert.AreNotEqual(KeyDispatchStage.ScopedCommand, dispatcher.Dispatch(enter));
            dispatcher.Dispatch(enter with { Kind = KeyEventKind.Up });
        }

        CollectionAssert.AreEqual(new[] { files[1] }, opened);
        focused.KeyContext = null;
        binding.Dispose();
        Assert.AreNotEqual(KeyDispatchStage.ScopedCommand, dispatcher.Dispatch(enter));
        CollectionAssert.AreEqual(new[] { files[1] }, opened);
    }

    /// <summary>
    /// Review: single/modifier selection and Ctrl+A must never launch a viewer; only an unmodified native double press opens.
    /// This crosses the actual ItemsView gesture dispatcher and production bridge without a window or OS input;
    /// the native click count is supplied at the window boundary. Existing Store cases cannot detect activation miswiring.
    /// </summary>
    [TestMethod]
    public async Task NativeSelection_DoubleClickAndSelectAllPreserveRangeBehavior()
    {
        ScreenshotFile[] files = [File("a", "one.png", 1), File("a", "two.png", 2), File("a", "three.png", 3), File("a", "four.png", 4)];
        using var store = new ScreenshotStore((_, _) => Task.FromResult(new ScreenshotCatalog("a", true, files, [])));
        await store.SelectAsync("a");
        var opened = new List<ScreenshotFile>();
        var factory = new SelectableItemFactory<ScreenshotFile, TextView>(
            files, file => Array.IndexOf(files, file), static file => Sprout.Markup.Text(file.FileName));
        using var list = new ItemsView(factory, new StackLayout { ItemExtent = 40f });
        var controller = new ScreenshotSelectionController(store, list, opened.Add);
        controller.SetFiles(files);
        controller.Synchronize();
        list.SetRootId(ElementId.Of(17));
        list.Mount();

        list.OnItemPrimary(1, ctrl: false, shift: false);
        Assert.IsTrue(store.Selecting);
        CollectionAssert.AreEqual(new[] { files[1] }, store.Selected.ToArray());
        Assert.IsEmpty(opened);
        var press = new PointerEvent(PointerEventKind.Down, new(2f, 2f), PointerButton.Primary, PointerDeviceType.Mouse);
        Assert.IsFalse(controller.HandlePointer(files[1], press).Handled);
        Assert.IsTrue(controller.HandlePointer(files[1], press with { ClickCount = 2 }).Handled);
        Assert.IsFalse(controller.HandlePointer(files[1], press with { ClickCount = 2, Modifiers = KeyModifiers.Control }).Handled);
        CollectionAssert.AreEqual(new[] { files[1] }, opened);

        store.ClearSelection();
        controller.Synchronize();
        list.OnItemPrimary(0, ctrl: true, shift: false);
        controller.Synchronize();
        list.OnItemPrimary(3, ctrl: true, shift: false);
        controller.Synchronize();
        CollectionAssert.AreEqual(new[] { files[0], files[3] }, store.Selected.ToArray());
        list.OnItemPrimary(1, ctrl: false, shift: true);
        controller.Synchronize();
        CollectionAssert.AreEqual(new[] { files[1], files[2], files[3] }, store.Selected.ToArray());
        CollectionAssert.AreEqual(new[] { files[1] }, opened);

        list.OnItemPrimary(2, ctrl: false, shift: false);
        controller.Synchronize();
        CollectionAssert.AreEqual(new[] { files[2] }, store.Selected.ToArray());
        list.OnItemPrimary(2, ctrl: true, shift: false);
        controller.Synchronize();
        Assert.IsEmpty(store.Selected);
        CollectionAssert.AreEqual(new[] { files[1] }, opened);

        list.OnSelectAll();
        controller.Synchronize();
        CollectionAssert.AreEqual(files, store.Selected.ToArray());
        list.OnSelectAll();
        controller.Synchronize();
        Assert.IsEmpty(store.Selected);
        CollectionAssert.AreEqual(new[] { files[1] }, opened);

        store.ClearSelection();
        controller.Synchronize();
        list.OnItemPrimary(0, ctrl: false, shift: false);
        CollectionAssert.AreEqual(new[] { files[0] }, store.Selected.ToArray());
        CollectionAssert.AreEqual(new[] { files[1] }, opened);
    }

    /// <summary>
    /// Review: sorting must preserve selected files, while refreshed/changed files and a new filter must not silently remain destructive targets.
    /// The controlled inventory substitutes only the read boundary; the actual Store owns filtering and selection reconciliation.
    /// </summary>
    [TestMethod]
    public async Task RefreshAndFilter_KeepOnlyTheExactVisibleSelection()
    {
        var first = File("a", "first.png", 1);
        var second = File("a", "second.png", 2);
        ScreenshotCatalog catalog = new("a", true, [first, second], []);
        using var store = new ScreenshotStore((_, _) => Task.FromResult(catalog));
        await store.SelectAsync("a");
        store.SelectAll();
        store.ChangeSort((int)ScreenshotSort.Oldest);
        CollectionAssert.AreEquivalent(new[] { first, second }, store.Selected.ToArray());
        var changed = first with { Bytes = 2 };
        catalog = catalog with { Files = [changed, second] };
        await store.SelectAsync("a", true);
        CollectionAssert.AreEqual(new[] { second }, store.Selected.ToArray());
        store.ChangeQuery("FIRST");
        Assert.IsEmpty(store.Selected);
        store.SelectAll();
        CollectionAssert.AreEqual(new[] { changed }, store.Selected.ToArray());
        CollectionAssert.AreEqual(new[] { changed }, store.VisibleFiles.ToArray());
    }

    /// <summary>
    /// Review: an old refresh finishing after B has already been selected must neither restore A's identically named file
    /// nor clear B's new selection. Explicit read-entry signals establish this boundary without sleeps.
    /// </summary>
    [TestMethod]
    public async Task SwitchInstance_OldRefreshCannotReplaceOrClearNewSelection()
    {
        var oldRead = new TaskCompletionSource<ScreenshotCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var a = File("a", "same.png", 1);
        var b = File("b", "same.png", 1);
        var reads = 0;
        using var store = new ScreenshotStore((id, _) =>
        {
            if (id == "a" && Interlocked.Increment(ref reads) > 1)
            {
                entered.TrySetResult();
                return oldRead.Task;
            }

            return Task.FromResult(new ScreenshotCatalog(id, true, [id == "a" ? a : b], []));
        });
        await store.SelectAsync("a");
        store.SelectAll();
        var refreshing = store.SelectAsync("a", true);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
            await store.SelectAsync("b");
            Assert.IsEmpty(store.Selected);
            store.SelectAll();
            oldRead.SetResult(new("a", true, [a with { Bytes = 99 }], []));
            await refreshing;
            Assert.AreEqual("b", store.InstanceId);
            CollectionAssert.AreEqual(new[] { b }, store.Selected.ToArray());
            CollectionAssert.AreEqual(new[] { b }, store.VisibleFiles.ToArray());
        }
        finally
        {
            oldRead.TrySetResult(new("a", true, [], []));
            await refreshing;
        }
    }

    private static ScreenshotFile File(string instance, string name, int day) =>
        new(name, Path.Combine("C:\\screenshot-selection-fixture", instance, name), 1, new DateTimeOffset(2024, 1, day, 0, 0, 0, TimeSpan.Zero));

    /// <summary>Gets cancellation for the explicitly held read boundary.</summary>
    public TestContext TestContext { get; set; } = null!;
}