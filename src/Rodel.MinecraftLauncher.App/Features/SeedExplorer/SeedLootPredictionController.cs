// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Worlds.Maps;
using Sprout.Reactive;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Retains a manual prediction separately from saved inventory and rejects canceled or superseded completions.</summary>
internal sealed class SeedLootPredictionController(
    Func<WorldMapSnapshot, string, SeedContainer, CancellationToken, Task<WorldContainerPrediction>> predict,
    Func<WorldMapSnapshot, string, SeedContainer, bool> isCurrent,
    CancellationToken lifetime) : IDisposable
{
    private readonly Signal<SeedLootPredictionStatus> _current = new(new());
    private Target? _target;
    private CancellationTokenSource? _operation;
    private long _revision;
    private bool _disposed;
    internal SeedLootPredictionStatus Current => _current.Value;
    internal bool CanPredict => _target is { Snapshot.Seed: not null } target && !string.IsNullOrWhiteSpace(target.Recorded.LootTable);

    internal bool SetContext(WorldMapSnapshot? snapshot, string dimension, SeedContainer recorded)
    {
        var next = snapshot is null ? null : new Target(snapshot, dimension, recorded with { Items = [.. recorded.Items] });
        if (Same(_target, next)) return false;
        _revision++;
        _operation?.Cancel();
        _target = next;
        _current.Value = new();
        return true;
    }

    internal async Task StartAsync()
    {
        if (_disposed || Current.Busy || !CanPredict || _target is not { } target) return;
        var revision = ++_revision;
        var source = _operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        var token = source.Token;
        _current.Value = new() { Busy = true };
        try
        {
            var result = await predict(target.Snapshot, target.Dimension, target.Recorded, token);
            token.ThrowIfCancellationRequested();
            if (!Valid(target, revision)) return;
            if (result.SourceRevision != target.Snapshot.Revision) throw new InvalidDataException("Prediction does not belong to the inspected save snapshot.");
            _current.Value = new() { Result = result };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceWarning("Container prediction failed: {0}", error);
            if (Valid(target, revision) && !token.IsCancellationRequested) _current.Value = new() { Error = error.Message };
        }
        finally
        {
            if (Owns(target, revision)) _current.Value = Current with { Busy = false, Canceling = false };
            if (ReferenceEquals(_operation, source)) _operation = null;
            source.Dispose();
        }
    }

    internal void Cancel()
    {
        if (_disposed || !Current.Busy || Current.Canceling) return;
        _current.Value = Current with { Canceling = true };
        _operation?.Cancel();
    }

    internal void Select(SeedLootItem? item) => _current.Value = Current with
    {
        Selected = item is not null && Current.Result?.Container.Items.Contains(item) == true ? item : null
    };

    private bool Owns(Target target, long revision) => !_disposed && revision == _revision && Same(target, _target);
    private bool Valid(Target target, long revision) => Owns(target, revision) && isCurrent(target.Snapshot, target.Dimension, target.Recorded);
    private static bool Same(Target? first, Target? second) => first is null || second is null ? first is null && second is null
        : first.Snapshot.Selection == second.Snapshot.Selection && first.Snapshot.Revision == second.Snapshot.Revision && first.Dimension == second.Dimension
            && first.Recorded.Id == second.Recorded.Id && first.Recorded.X == second.Recorded.X && first.Recorded.Y == second.Recorded.Y && first.Recorded.Z == second.Recorded.Z
            && first.Recorded.LootTable == second.Recorded.LootTable && first.Recorded.LootSeed == second.Recorded.LootSeed && first.Recorded.State == second.Recorded.State;

    /// <summary>Cancels only this inspector's outstanding prediction and prevents further UI publication.</summary>
    public void Dispose() { _disposed = true; _revision++; _operation?.Cancel(); _operation?.Dispose(); _operation = null; }

    private sealed record Target(WorldMapSnapshot Snapshot, string Dimension, SeedContainer Recorded);
}

internal sealed record SeedLootPredictionStatus
{
    internal bool Busy { get; init; }
    internal bool Canceling { get; init; }
    internal WorldContainerPrediction? Result { get; init; }
    internal SeedLootItem? Selected { get; init; }
    internal string Error { get; init; } = string.Empty;
}