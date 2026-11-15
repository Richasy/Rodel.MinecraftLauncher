// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Screenshots;
using Sprout.Reactive;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal enum ScreenshotSort
{
    Newest,
    Oldest,
    Name
}

/// <summary>Owns instance-bound inventories and exact selections; obsolete reads cannot select files in a different instance.</summary>
[Store]
internal sealed partial class ScreenshotStore
{
    private readonly Func<string, CancellationToken, Task<ScreenshotCatalog>> _read;
    private readonly IScheduler? _scheduler;
    private readonly CancellationToken _lifetime;

    internal ScreenshotStore(Func<string, CancellationToken, Task<ScreenshotCatalog>> read, IScheduler? scheduler = null)
    {
        _read = read;
        _scheduler = scheduler;
        _lifetime = Lifetime;
        Context = new(string.Empty, read, scheduler, _lifetime);
        Selection = [];
        Query = string.Empty;
    }

    private partial ReadContext Context { get; set; }
    private partial ScreenshotFile[] Selection { get; set; }
    internal partial string Query { get; private set; }
    internal partial ScreenshotSort Sort { get; private set; }
    internal partial bool Selecting { get; private set; }
    internal string InstanceId => Context.InstanceId;
    internal ScreenshotCatalog? Catalog => Context.Operation.State is { HasValue: true, Value: { } value } ? value : null;
    internal IReadOnlyList<ScreenshotFile> Files => Catalog?.Files ?? [];
    internal IReadOnlyList<ScreenshotFile> Selected => [.. Selection.Where(Files.Contains)];
    internal bool Loading => InstanceId.Length > 0 && Context.Operation.State.IsLoading;
    internal string Error => InstanceId.Length == 0 ? string.Empty : Context.Operation.State.Error?.Message ?? string.Empty;

    internal IReadOnlyList<ScreenshotFile> VisibleFiles
    {
        get
        {
            var files = Files.Where(file => file.FileName.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase));
            return Sort switch
            {
                ScreenshotSort.Oldest => files.OrderBy(file => file.ModifiedUtc).ThenBy(file => file.FileName, StringComparer.OrdinalIgnoreCase).ToArray(),
                ScreenshotSort.Name => [.. files.OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)],
                _ => [.. files.OrderByDescending(file => file.ModifiedUtc).ThenBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)],
            };
        }
    }

    internal async Task SelectAsync(string instanceId, bool force = false)
    {
        if (_lifetime.IsCancellationRequested || (!force && instanceId == InstanceId)) return;
        var context = Context;
        if (instanceId != context.InstanceId)
        {
            context.Operation.Cancel();
            ClearSelection();
            Query = string.Empty;
            context = new(instanceId, _read, _scheduler, _lifetime);
            Context = context;
        }

        if (instanceId.Length == 0) return;
        await context.Operation.Run();
        if (!_lifetime.IsCancellationRequested && ReferenceEquals(Context, context))
            SetSelection(Selected);
    }

    internal void Suspend() => Context.Operation.Cancel();

    internal void ChangeQuery(string value)
    {
        if (Query == value) return;
        Selection = [];
        Query = value;
    }

    internal void ChangeSort(int value)
    {
        if (value is >= 0 and <= 2) Sort = (ScreenshotSort)value;
    }

    internal void BeginSelection() => Selecting = true;

    internal void ClearSelection()
    {
        Selection = [];
        Selecting = false;
    }

    internal void SelectAll()
    {
        Selecting = true;
        SetSelection(VisibleFiles);
    }

    internal void Toggle(ScreenshotFile file)
    {
        if (!Files.Contains(file)) return;
        Selecting = true;
        var selected = Selected;
        SetSelection(selected.Contains(file) ? selected.Where(item => item != file) : selected.Append(file));
    }

    internal void SetSelection(IEnumerable<ScreenshotFile> selected)
    {
        var current = Files;
        var next = selected.Where(current.Contains).Distinct().ToArray();
        if (next.Length == Selection.Length && next.All(Selection.Contains)) return;
        Selection = next;
    }

    private sealed class ReadContext
    {
        internal ReadContext(string instanceId, Func<string, CancellationToken, Task<ScreenshotCatalog>> read, IScheduler? scheduler, CancellationToken lifetime)
        {
            InstanceId = instanceId;
            Operation = new(ct => read(instanceId, ct), scheduler, lifetime);
        }

        internal string InstanceId { get; }
        internal LatestOperation<ScreenshotCatalog> Operation { get; }
    }
}