// Copyright (c) Richasy. All rights reserved.

using Sprout.Reactive;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed record WorldItem(
    string InstanceId,
    string Folder,
    string Name,
    string Version,
    int? GameType,
    bool Hardcore,
    long? LastPlayed,
    string? Warning,
    byte[]? Icon)
{
    internal (string InstanceId, string Folder) Key => (InstanceId, Folder);
}

internal sealed record WorldListRead(string InstanceId, IReadOnlyList<WorldItem> Previous);

internal sealed record WorldListSnapshot(
    string InstanceId,
    IReadOnlyList<WorldItem> Items,
    int RecoveryCount,
    string Warning);

/// <summary>Owns instance-bound list reads, retaining a refresh only within the same instance.</summary>
[Store]
internal sealed partial class WorldListStore
{
    private readonly Func<WorldListRead, CancellationToken, Task<WorldListSnapshot>> _read;
    private readonly IScheduler? _scheduler;
    private readonly CancellationToken _lifetime;

    internal WorldListStore(Func<WorldListRead, CancellationToken, Task<WorldListSnapshot>> read, IScheduler? scheduler = null)
    {
        _lifetime = Lifetime;
        _read = read;
        _scheduler = scheduler;
        Context = new(string.Empty, read, scheduler, _lifetime);
    }

    private partial ReadContext Context { get; set; }
    internal string InstanceId => Context.InstanceId;
    private WorldListSnapshot? Current => Context.Operation.State is { HasValue: true, Value: { } value } && value.InstanceId == InstanceId ? value : null;
    internal IReadOnlyList<WorldItem> Rows => Current?.Items ?? [];
    internal int RecoveryCount => Current?.RecoveryCount ?? 0;
    internal bool Loading => InstanceId.Length > 0 && Context.Operation.State.IsLoading;
    internal string Error => InstanceId.Length == 0 ? string.Empty : Context.Operation.State.Error?.Message ?? Current?.Warning ?? string.Empty;

    internal Task SelectAsync(string id, bool force = false)
    {
        if (_lifetime.IsCancellationRequested || (!force && InstanceId == id))
            return Task.CompletedTask;
        var context = Context;
        if (context.InstanceId != id)
        {
            context.Operation.Cancel();
            context = new(id, _read, _scheduler, _lifetime);
            Context = context;
        }

        if (id.Length == 0)
            return Task.CompletedTask;
        context.Request = new(id, Rows);
        return context.Operation.Run();
    }

    private sealed class ReadContext
    {
        internal ReadContext(
            string instanceId,
            Func<WorldListRead, CancellationToken, Task<WorldListSnapshot>> read,
            IScheduler? scheduler,
            CancellationToken lifetime)
        {
            InstanceId = instanceId;
            Request = new(instanceId, []);
            Operation = new(ct => read(Request, ct), scheduler, lifetime);
        }

        internal string InstanceId { get; }
        internal WorldListRead Request { get; set; }
        internal LatestOperation<WorldListSnapshot> Operation { get; }
    }
}