// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Coordinates one frozen current-seed structure query and rejects results arriving after another query or world is selected.</summary>
internal sealed class SeedStructureController(SeedExplorerController owner)
{
    private readonly Dictionary<(string Id, int X, int Z), int> _keys = [];
    private CancellationTokenSource? _cancellation;
    private Task _work = Task.CompletedTask;
    private long _revision;
    private int _key;
    private SeedExplorerSnapshot Current { get => owner.State.Current; set { if (!owner.Disposed) owner.State.Current = value; } }

    internal int Key(SeedPosition position)
    {
        var identity = (position.Id, position.X, position.Z);
        if (!_keys.TryGetValue(identity, out var key)) _keys[identity] = key = ++_key;
        return key;
    }

    internal void RestoreTargets()
    {
        var choices = SeedText.Targets(Current, "structure");
        if (!choices.Any(choice => SeedText.SameTargets(choice.Targets, Current.StructureTargets)))
            Current = Current with { StructureTargets = choices.FirstOrDefault()?.Targets ?? [] };
    }

    internal void Choose(string[] targets)
    {
        if (SeedText.SameTargets(targets, Current.StructureTargets)) return;
        Invalidate();
        Current = Current with { StructureTargets = [.. targets] };
    }

    internal void Radius(int radius)
    {
        if (radius == Current.StructureRadius) return;
        Invalidate();
        Current = Current with { StructureRadius = radius };
    }

    private void Invalidate()
    {
        _revision++;
        _cancellation?.Cancel();
        Current = Current with { StructureResult = null, StructureSearching = false, StructurePhase = "ready", StructureError = string.Empty };
    }

    internal Task FindAsync()
    {
        var state = Current;
        if (owner.Disposed || state.Opening || state.Profile is null || (!state.UseSavedMap && state.Seed.Length == 0) || state.StructureTargets.Length == 0) return Task.CompletedTask;
        var revision = ++_revision;
        _cancellation?.Cancel();
        var source = _cancellation = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token);
        var previous = _work;
        var query = new SeedStructureQuery
        {
            Profile = state.Profile,
            Seed = state.Seed,
            World = state.World with { },
            CenterX = (int)Math.Round(state.CenterX),
            CenterZ = (int)Math.Round(state.CenterZ),
            Radius = state.StructureRadius,
            Targets = [.. state.StructureTargets],
            Limit = 100,
        };
        Current = state with { StructureSearching = true, StructurePhase = "running", StructureError = string.Empty };
        return _work = RunAsync(previous, query, state.Fingerprint, revision, source);
    }

    private async Task RunAsync(Task previous, SeedStructureQuery query, string fingerprint, long revision, CancellationTokenSource source)
    {
        try
        {
            // Let the previous cancellation drain its native response before reusing this warmed transport.
            await previous;
            source.Token.ThrowIfCancellationRequested();
            var result = await owner.Map.FindStructuresAsync(query, source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (owner.Disposed || revision != _revision || Current.Opening || Current.Fingerprint != fingerprint
                || Current.Seed != query.Seed || Current.World != query.World)
            {
                return;
            }

            var retained = result.Positions.Select(position => (position.Id, position.X, position.Z)).ToHashSet();
            foreach (var key in _keys.Keys.Where(key => !retained.Contains(key)).ToArray()) _keys.Remove(key);
            Current = Current with { StructureResult = result, StructurePhase = "completed" };
        }
        catch (OperationCanceledException) { if (revision == _revision) Current = Current with { StructurePhase = "stopped" }; }
        catch (Exception error) { if (revision == _revision) Current = Current with { StructurePhase = "failed", StructureError = error.Message }; }
        finally
        {
            if (revision == _revision) Current = Current with { StructureSearching = false };
            if (ReferenceEquals(source, _cancellation)) _cancellation = null;
            source.Dispose();
        }
    }

    internal async Task StopAsync()
    {
        _cancellation?.Cancel();
        await _work;
    }

    internal async Task ResetAsync() { await StopAsync(); Invalidate(); }

    internal void Select(SeedPosition position)
    {
        if (Current.StructureResult is not { } result || result.Seed != Current.Seed || result.World != Current.World
            || result.Fingerprint != Current.Fingerprint || !result.Positions.Contains(position))
        {
            return;
        }

        var layers = Current.StructureLayers;
        if (layers is not null && !layers.Contains(position.Id)) layers = [.. layers, position.Id];
        Current = Current with { BlocksPerPixel = Math.Min(4, Current.BlocksPerPixel), StructureLayers = layers };
        owner.Map.Pan(position.X, position.Z);
        Current = Current with { PendingPlace = position, PendingTeleportY = position.Y };
    }
}