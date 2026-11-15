// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Worlds.Maps;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Owns the isolated native preview used behind missing saved chunks, independently of the saved revision.</summary>
internal sealed class SeedWorldPreview(SeedExplorerController owner) : IAsyncDisposable
{
    private readonly SemaphoreSlim _opening = new(1);
    private SeedExplorerSession? _session;
    private SeedRuntimeProfile? _failed;

    internal void Retry() => _failed = null;

    internal async Task<SeedExplorerSession?> GetAsync(WorldMapSnapshot snapshot, CancellationToken ct)
    {
        if (snapshot.Seed is null || !snapshot.PreviewSupported) return null;
        if (_failed == snapshot.Profile) { Update(snapshot, preparing: false, unavailable: true); return null; }

        await _opening.WaitAsync(ct);
        try
        {
            if (_session is { IsAvailable: true } && _session.Profile == snapshot.Profile) return _session;
            if (_failed == snapshot.Profile) { Update(snapshot, preparing: false, unavailable: true); return null; }

            Update(snapshot, preparing: true, unavailable: false);
            if (_session is not null) { await _session.DisposeAsync(); _session = null; }

            _session = await Task.Run(() => owner.Client.Seeds.OpenAsync(snapshot.Profile, ct), ct);
            return _session;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            _failed = snapshot.Profile;
            System.Diagnostics.Trace.TraceWarning("Saved-world native preview could not be opened: {0}", error);
            Update(snapshot, preparing: false, unavailable: true);
            return null;
        }
        finally
        {
            if (_failed != snapshot.Profile) Update(snapshot, preparing: false, unavailable: false);
            _opening.Release();
        }
    }

    private void Update(WorldMapSnapshot snapshot, bool preparing, bool unavailable)
    {
        if (!owner.Disposed && owner.State.Current.UseSavedMap && owner.State.Current.WorldSnapshot?.Selection == snapshot.Selection)
            owner.State.Current = owner.State.Current with { WorldPreviewPreparing = preparing, WorldPreviewUnavailable = unavailable };
    }

    internal async Task<SeedRegionResult> RegionAsync(WorldMapSnapshot snapshot, SeedRegionQuery query, IProgress<SeedRegionProgress> progress, CancellationToken ct)
    {
        var session = await GetAsync(snapshot, ct) ?? throw new InvalidOperationException(Resources.SeedWorldPreviewUnavailable);
        return await Task.Run(() => session.RegionAsync(query with { Profile = session.Profile }, progress, ct), ct);
    }

    internal async Task<SeedBlockScene> BuildSceneAsync(WorldMapSnapshot snapshot, SeedRegionResult region, CancellationToken ct)
    {
        var session = await GetAsync(snapshot, ct) ?? throw new InvalidOperationException(Resources.SeedWorldPreviewUnavailable);
        return await Task.Run(() => session.BuildSceneAsync(region, ct), ct);
    }

    /// <summary>Closes only the isolated generation worker; the saved world is never opened by that worker.</summary>
    public async ValueTask DisposeAsync()
    {
        await _opening.WaitAsync();
        try { if (_session is not null) await _session.DisposeAsync(); }
        finally { _opening.Release(); _opening.Dispose(); }
    }
}