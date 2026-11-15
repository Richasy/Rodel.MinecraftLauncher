// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

internal sealed record SkinLayerRowData(
    int Key,
    SkinLayerInfo Layer,
    ImageSource Thumbnail,
    bool Selected,
    bool Enabled);

/// <summary>Keeps stable layer identities and captures reordering on the list so scrolling may recycle the source row.</summary>
internal sealed partial class SkinEditorLayerList : Control
{
    private readonly SelectableItemFactory<SkinLayerRowData, SkinLayerRowHost> _factory;
    private readonly ItemsView _list;
    private readonly Dictionary<string, int> _keys = [];
    private int _nextKey;
    private SkinLayerRowData[] _items = [];
    private string[]? _pendingOrder;
    private string? _document;
    private long _revision;
    private bool _tracking;

    internal SkinEditorLayerList(SkinEditorState editor)
    {
        Editor = editor;
        _factory = new(
            [],
            data => data.Key,
            data => new SkinLayerRowHost(data, this),
            nameSelector: data => data.Layer.Name,
            automationSelector: data => new()
            {
                Name = data.Layer.Name,
                AutomationId = "skin.layer." + data.Layer.Id
            },
            dragSpanSelector: data => data.Enabled && (data.Layer.Id != SkinDocument.BackgroundLayerId) ? 1 : 0);
        _list = new ItemsView(_factory, new StackLayout { EstimatedItemExtent = 56f }).ItemContentInteraction(ItemsViewItemContentInteraction.Interactive).ReorderVisualMode(ItemsViewReorderVisualMode.DraggedItemOnly).ResolveDropIndex(ResolveDrop).OnReorder(Reorder).AnimateItems().OnSelectionChanged(indices =>
            {
                if ((indices.Count > 0) && (indices[0] < _items.Length))
                    Editor.Controller.SelectLayer(_items[indices[0]].Layer.Id);
            });
    }

    internal SkinEditorState Editor { get; }

    internal SkinEditorLayerList WithLayers(
        SkinLayerInfo[] layers,
        Dictionary<string, ImageSource> thumbnails,
        string selected,
        bool enabled)
    {
        var visual = layers.Reverse().ToArray();
        if (_pendingOrder is { } order)
            visual = [.. order.Select(id => visual.First(layer => layer.Id == id))];
        _items = [..visual.Select(layer =>
                {
                    if (!_keys.TryGetValue(layer.Id, out var key))
                        _keys[layer.Id] = key = ++_nextKey;
                    return new SkinLayerRowData(
                        key,
                        layer,
                        thumbnails[layer.Id],
                        selected == layer.Id,
                        enabled || Editor.LayerDragging);
                })];
        _factory.UpdateItems(_items);
        _list.SelectedIndex(Array.FindIndex(_items, item => item.Layer.Id == selected));
        return this;
    }

    private Ui Body => _list.Enabled(!Editor.Busy && !Editor.FileActive)
        .ItemChrome(new(
            SkinEditorChrome.QuietPalette,
            LauncherButtonPalette.Create(false, true),
            Colors.Transparent,
            new CornerRadius(0f),
            1f))
        .Automation(new()
        {
            Name = Resources.SkinLayers,
            AutomationId = "skin.layers"
        });

    private int ResolveDrop(ItemsDropProposal proposal)
    {
        if (!_tracking && Editor.Controller.Session is { } session)
        {
            _document = session.Id;
            _revision = session.Revision;
            _tracking = true;
            Editor.LayerDragging = true;
            Editor.Controller.Input.ClearHover();
            WatchDrag();
        }

        return proposal.Count < 2 ? proposal.SourceIndex : Math.Clamp(proposal.ProposedIndex, 0, proposal.Count - 2);
    }

    private void WatchDrag() => Editor.Controller.Window.RequestAnimationFrame(_ =>
        {
            if (!_tracking)
                return;
            if (((Sprout.Semantics.IDragProviderSource)_list).IsGrabbed)
            {
                WatchDrag();
            }
            else
            {
                _tracking = false;
                Editor.LayerDragging = false;
            }
        });

    private void Reorder(int from, int to)
    {
        _tracking = false;
        Editor.LayerDragging = false;
        if ((from == to) || (from < 0) || (to < 0) || (to >= (_items.Length - 1)) || Editor.Controller.Session is not { } session || (session.Id != _document) || (session.Revision != _revision))
        {
            return;
        }

        var source = _items[from];
        var items = _items.ToList();
        items.RemoveAt(from);
        items.Insert(to, source);
        _pendingOrder = [.. items.Select(item => item.Layer.Id)];
        _items = [.. items];
        _factory.UpdateItems(_items);
        _ = SaveReorderAsync(source.Layer.Id, _items.Length - 2 - to);
    }

    private async Task SaveReorderAsync(string id, int index)
    {
        try
        {
            await Editor.Controller.LayerActions.ApplyAsync(new()
            {
                Operation = SkinLayerOperation.Reorder,
                Id = id,
                Index = index
            });
        }
        finally
        {
            _pendingOrder = null;
            WithLayers(Editor.Layers, Editor.LayerThumbnails, Editor.ActiveLayerId, Editor.Controller.LayerActions.CanChange);
        }
    }

    internal void CancelDrag()
    {
        _tracking = false;
        _list.CancelItemDrag();
        Editor.LayerDragging = false;
    }

    internal Task<bool> MoveAsync(string id, int visualDirection)
    {
        var index = Array.FindIndex(Editor.Layers, layer => layer.Id == id) - 1;
        var destination = index - visualDirection;
        return (id != SkinDocument.BackgroundLayerId) && (destination >= 0) && (destination < (Editor.Layers.Length - 1)) ? Editor.Controller.LayerActions.ApplyAsync(new()
        {
            Operation = SkinLayerOperation.Reorder,
            Id = id,
            Index = destination
        }) : Task.FromResult(false);
    }

    private void Deactivated(object? sender, EventArgs args) => CancelDrag();

    private void KeyDown(KeyEvent key)
    {
        if (key.IsImeOwned || key.Repeat)
            return;
        if (key.Key == Key.Escape)
        {
            if (Editor.LayerDragging)
            {
                CancelDrag();
            }
            else if (Editor.LayerEditing && !Editor.Busy)
            {
                Editor.Controller.LayerActions.Discard();
            }
        }
    }

    protected override IDisposable? OnMounted()
    {
        Editor.Controller.LayerActions.CancelDrag += CancelDrag;
        Editor.Controller.Window.Deactivated += Deactivated;
        Editor.Controller.Window.KeyDown += KeyDown;
        return null;
    }

    protected override void OnDispose()
    {
        CancelDrag();
        Editor.Controller.LayerActions.CancelDrag -= CancelDrag;
        Editor.Controller.Window.Deactivated -= Deactivated;
        Editor.Controller.Window.KeyDown -= KeyDown;
        base.OnDispose();
    }
}