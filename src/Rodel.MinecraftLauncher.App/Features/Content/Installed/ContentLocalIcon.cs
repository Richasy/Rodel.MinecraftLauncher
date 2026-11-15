// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Graphics;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

[View]
internal readonly partial record struct ContentLocalIcon(ContentItem Item, Color Ink, Color Edge)
{
    /// <summary>Creates an icon reader only for a row realized by the virtualized list.</summary>
    public Ui Body => new ContentLocalIconControl(Item, Ink, Edge);
}

internal sealed partial class ContentLocalIconControl : Control
{
    private CancellationTokenSource? _read;
    private bool _mounted;

    internal ContentLocalIconControl(ContentItem item, Color ink, Color edge)
    {
        Item = item;
        Ink = ink;
        Edge = edge;
        Bytes = item.IconBytes;
        Warning = item.IconWarning;
    }

    private partial ContentItem Item { get; set; }
    private partial Color Ink { get; set; }
    private partial Color Edge { get; set; }
    private partial byte[]? Bytes { get; set; }
    private partial string? Warning { get; set; }
    private partial bool Loading { get; set; }

    private Ui Body => When(Loading,
        new ContentIconLoading(),
        new ContentIconView(Bytes, Warning is null ? null : string.Format(Resources.ContentIconWarning, Warning), Resources.ContentIconUnavailable, Ink, Edge));

    protected override IDisposable? OnMounted()
    {
        _mounted = true;
        StartRead();
        return base.OnMounted();
    }

    private void StartRead()
    {
        if (Bytes is not null || Item.ReadIcon is not { } source) return;
        _read = new();
        _ = ReadAsync(source, _read);
    }

    private async Task ReadAsync(Func<CancellationToken, Task<(byte[]? Bytes, string? Warning)>> source, CancellationTokenSource reading)
    {
        var ct = reading.Token;
        Loading = true;
        try
        {
            var result = await Task.Run(() => source(ct), ct);
            if (ct.IsCancellationRequested || !ReferenceEquals(_read, reading)) return;
            Bytes = result.Bytes;
            Warning = result.Warning;
            Loading = false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is not ContentLocalIconControl other) return;
        var changed = Item.Sha1 != other.Item.Sha1 || Item.StoredPath != other.Item.StoredPath || Item.IconPath != other.Item.IconPath
            || (Item.Sha1 is null && Item.ReadIcon != other.Item.ReadIcon);
        Item = other.Item;
        Ink = other.Ink;
        Edge = other.Edge;
        if (!changed) return;
        _read?.Cancel();
        _read?.Dispose();
        _read = null;
        Bytes = Item.IconBytes;
        Warning = Item.IconWarning;
        Loading = false;
        if (_mounted) StartRead();
    }

    protected override void OnDispose()
    {
        _mounted = false;
        _read?.Cancel();
        _read?.Dispose();
        base.OnDispose();
    }
}