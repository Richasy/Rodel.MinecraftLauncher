// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Features.Settings;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using Sprout;
using Sprout.WindowsAppSDK;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Retains a warmed Core session and coalesces obsolete viewports without publishing a tile under another seed/configuration.</summary>
internal sealed class SeedMapController(SeedExplorerController owner) : IAsyncDisposable
{
    private readonly SemaphoreSlim _contexts = new(1);
    private readonly Dictionary<string, (SeedRenderedTile Tile, long Used)> _tiles = [];
    private readonly Dictionary<string, (SeedMapTile Tile, long Used)> _markers = [];
    private HashSet<string> _markerKeys = [];
    private SeedExplorerSession? _session;
    internal bool HasOpenRuntime => _session?.IsAvailable == true && Current.Fingerprint == _session.Fingerprint;
    private CancellationTokenSource? _opening, _view, _point, _catalogCancel;
    private SeedRuntimeProfile? _catalogProfile;
    private int _catalogGeneration;
    private Task _viewTask = Task.CompletedTask;
    private Task _catalogTask = Task.CompletedTask;
    private long _contextGeneration, _viewportGeneration, _usage;
    private int _tileKey;
    private string _language = string.Empty;
    private SeedExplorerSnapshot Current { get => owner.State.Current; set { if (!owner.Disposed) owner.State.Current = value; } }

    internal async Task InitializeAsync()
    {
        try
        {
            var versions = await Task.Run(() => owner.Client.Seeds.ListVersionsAsync(owner.Lifetime.Token));
            if (owner.Disposed) return;
            Current = Current with { Versions = versions, Opening = false, Phase = "ready" };
            var draft = owner.Draft;
            var index = Array.FindIndex(versions, version => version.Installed && draft.Profile is { } selected
                && string.Equals(version.Profile.MinecraftDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    selected.MinecraftDirectory?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && version.Profile.VersionId == selected.VersionId);
            var restore = index >= 0;
            if (!restore) index = Array.FindIndex(versions, version => version.Installed);
            if (index >= 0)
            {
                Current = Current with
                {
                    Profile = versions[index].Profile,
                    VersionIndex = index,
                    World = restore ? draft.World : new(),
                    SliceY = restore ? draft.SliceY : null,
                    Terrain = draft.Terrain
                };
                owner.Search.RestoreConditions();
                if (Current.SearchMode && Current.Conditions.Length > 0) await EnsureCatalogAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Current = Current with { Opening = false }; owner.Error(error); }
    }

    internal async Task PrepareAsync()
    {
        if (Current.Preparing) return;
        Current = Current with { Preparing = true, Error = string.Empty, Phase = "preparing" };
        try
        {
            var profile = await Task.Run(() => owner.Client.PrepareSeedRuntimeAsync(cancellationToken: owner.Lifetime.Token));
            var versions = await Task.Run(() => owner.Client.Seeds.ListVersionsAsync(owner.Lifetime.Token));
            if (owner.Disposed) return;
            Current = Current with { Versions = versions };
            await OpenAsync(profile, Current.World, Current.SliceY, Current.SeedInput, loadMap: Current.SeedInput.Trim().Length > 0);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { owner.Error(error); }
        finally { Current = Current with { Preparing = false }; }
    }

    internal Task SelectVersionAsync(int index)
    {
        if (index < 0 || index >= Current.Versions.Length) return Task.CompletedTask;
        if (!Current.Versions[index].Installed)
        {
            owner.NotifyError(Resources.SeedUnsupported);
            return Task.CompletedTask;
        }

        var profile = Current.Versions[index].Profile;
        if (index == Current.VersionIndex && Current.Profile == profile) return Task.CompletedTask;
        return Current.Seed.Length == 0 ? UpdateEmptyContextAsync(profile, Current.World, Current.SliceY, index)
            : OpenAsync(profile, Current.World, Current.SliceY, Current.SeedInput);
    }
    internal async Task EnsureCatalogAsync()
    {
        if (_catalogProfile != Current.Profile) _catalogCancel?.Cancel();
        if (!_catalogTask.IsCompleted) await _catalogTask;
        if (owner.Disposed || Current.Opening || Current.Catalog is not null || Current.Profile is not { } profile) return;
        await (_catalogTask = LoadCatalogAsync(profile));
    }
    private async Task LoadCatalogAsync(SeedRuntimeProfile profile)
    {
        var generation = ++_catalogGeneration;
        _catalogCancel?.Cancel(); _catalogCancel?.Dispose();
        _catalogProfile = profile;
        _catalogCancel = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token);
        var ct = _catalogCancel.Token;
        Current = Current with { CatalogLoading = true, Error = string.Empty };
        try
        {
            var language = LanguageSection.GameLanguage;
            var catalog = await Task.Run(() => owner.Client.Seeds.GetCatalogAsync(profile, language, ct), ct);
            if (owner.Disposed || ct.IsCancellationRequested || generation != _catalogGeneration || Current.Profile != profile) return;
            _language = language;
            Current = Current with { Catalog = catalog, Fingerprint = catalog.Fingerprint, Terrain = catalog.Biomes.Length == 0 || Current.Terrain };
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!ct.IsCancellationRequested && generation == _catalogGeneration && Current.Profile == profile) owner.Error(error); }
        finally { if (generation == _catalogGeneration) Current = Current with { CatalogLoading = false }; }
    }
    internal Task GenerateAsync() => Current.SeedInput.Trim().Length > 0 && Current.Profile is { } profile
        ? OpenAsync(profile, Current.World, Current.SliceY, Current.SeedInput) : Task.CompletedTask;
    internal async Task ChangeWorldAsync(SeedWorldSettings world, int? y)
    {
        if (Current.WorldSource && !Current.SearchMode && Current.SavedMap)
        {
            if (Current.World != world) { await owner.Regions.StopAsync(); await owner.Structures.ResetAsync(); }

            var changed = Current.World != world || Current.SliceY != y;
            Current = Current with
            {
                World = world,
                SliceY = y,
                FollowPlayer = false,
                WorldPreviewUnavailable = Current.WorldSnapshot is not { PreviewSupported: true, Seed: not null },
                Tiles = changed ? [] : Current.Tiles,
                MapPositions = changed ? [] : Current.MapPositions
            };
            owner.Worlds.RequestViewport();
            return;
        }

        if (Current.Seed.Length > 0 && Current.Profile is { } profile)
            await OpenAsync(profile, world, y, Current.Seed);
        else if (Current.Profile is { } selected) await UpdateEmptyContextAsync(selected, world, y, Current.VersionIndex);
    }
    private async Task UpdateEmptyContextAsync(SeedRuntimeProfile profile, SeedWorldSettings world, int? y, int index)
    {
        var generation = ++_contextGeneration;
        _opening?.Cancel(); _view?.Cancel(); _point?.Cancel();
        Current = Current with { Opening = true, Error = string.Empty };
        await owner.Search.ResetContextAsync();
        if (owner.Disposed || generation != _contextGeneration) return;
        var sameProfile = Current.Profile == profile;
        if (!sameProfile) _catalogCancel?.Cancel();
        Current = Current with
        {
            Profile = profile,
            VersionIndex = index,
            World = world,
            SliceY = y,
            Catalog = sameProfile ? Current.Catalog : null,
            Fingerprint = sameProfile ? Current.Fingerprint : string.Empty,
            Opening = false,
        };
        owner.Search.RestoreConditions();
        if ((Current.SearchMode && Current.Conditions.Length > 0) || world.Preset == "single_biome") await EnsureCatalogAsync();
    }
    internal Task ShowSeedAsync(string seed, SeedPosition? focus = null) => Current.Profile is { } profile
        ? OpenAsync(profile, Current.World, Current.SliceY, seed, keepSearch: true, focus: focus) : Task.CompletedTask;
    internal void CancelPoint() { _point?.Cancel(); owner.Worlds.CancelPoint(); }
    internal Task ShowResultAsync(string seed)
    {
        if (Current.Profile is not { } profile) return Task.CompletedTask;
        Current = Current with { ShowOrigin = true, SelectedPlace = null };
        Current = Current with { BlocksPerPixel = 8 };
        return OpenAsync(profile, Current.World, Current.SliceY, seed, keepSearch: true,
            loadMap: true);
    }

    internal void ReturnToOrigin()
    {
        Current = Current with { ShowOrigin = true, SelectedPlace = null, FocusedPlace = new() { Id = "origin", Kind = "origin" } };
        Pan(0, 0);
    }

    internal void ReturnToSpawn()
    {
        if (Current.Spawn is not { } spawn) return;
        Current = Current with { SelectedPlace = null, FocusedPlace = spawn };
        Pan(spawn.X, spawn.Z);
    }

    internal Task RandomAsync()
    {
        if (Current.Opening || Current.Profile is not { } profile) return Task.CompletedTask;
        return OpenAsync(profile, Current.World, Current.SliceY, string.Empty, keepSearch: true);
    }

    internal async Task<bool> OpenAsync(SeedRuntimeProfile profile, SeedWorldSettings world, int? y, string seedInput,
        bool keepSearch = false, SeedBookmark? bookmark = null, SeedPosition? focus = null, SeedMapViewport? viewport = null, bool loadMap = true, string? expectedFingerprint = null)
    {
        if (owner.Disposed) return false;
        var inputAtStart = Current.SeedInput;
        var generation = ++_contextGeneration;
        _opening?.Cancel(); _opening?.Dispose();
        var opening = _opening = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token);
        var ct = opening.Token;
        owner.DismissPopups?.Invoke(); _point?.Cancel(); _view?.Cancel();
        Current = Current with { Opening = true, LoadingMap = loadMap, Error = string.Empty, Phase = "opening" };
        SeedExplorerSession? candidate = null;
        var entered = false;
        try
        {
            if (!keepSearch) await owner.Search.StopAsync();
            await owner.Structures.StopAsync();
            await owner.Regions.StopAsync();
            await _contexts.WaitAsync(ct); entered = true;
            await _viewTask;
            var old = _session;
            var sameProfile = old is { IsAvailable: true } && old.Profile == profile;
            candidate = sameProfile ? old! : await Task.Run(() => owner.Client.Seeds.OpenAsync(profile, ct), ct);
            var language = LanguageSection.GameLanguage;
            var catalog = sameProfile && Current.Catalog is not null ? Current.Catalog : await Task.Run(() => candidate.GetCatalogAsync(language, ct), ct);
            if (expectedFingerprint is { Length: > 0 } && candidate.Fingerprint != expectedFingerprint)
                throw new InvalidOperationException(Resources.SeedRuntimeChanged);
            if (catalog.Dimensions.FirstOrDefault(item => item.Id == world.Dimension) is { } dimension && y is { } slice)
                y = Math.Clamp(slice, dimension.MinY, dimension.MinY + dimension.Height - 1);
            var seed = loadMap ? await Task.Run(() => candidate.NormalizeSeedAsync(seedInput, ct), ct) : string.Empty;
            SeedPosition? spawn = null;
            if (loadMap && world.Dimension == "overworld" && catalog.SeedBoundSpawn)
            {
                if (generation == _contextGeneration) Current = Current with { Phase = "spawn" };
                spawn = (await Task.Run(() => candidate.SpawnAsync(new() { Profile = candidate.Profile, Seed = seed, World = world with { } }, ct), ct)).Position;
            }

            var pending = Current with
            {
                Profile = candidate.Profile,
                World = world with { },
                SliceY = y,
                Seed = seed,
                SeedInput = seed,
                Spawn = spawn,
                CenterX = bookmark?.X ?? focus?.X ?? viewport?.X ?? spawn?.X ?? 0,
                CenterZ = bookmark?.Z ?? focus?.Z ?? viewport?.Z ?? spawn?.Z ?? 0,
                BlocksPerPixel = bookmark?.BlocksPerPixel ?? viewport?.BlocksPerPixel ?? (focus is null ? Current.BlocksPerPixel : Math.Min(8, Current.BlocksPerPixel)),
            };
            var pendingView = SeedMapPlanning.Normalize(SeedMapTiles.View(pending));
            pending = pending with { BlocksPerPixel = pendingView.BlocksPerPixel, CenterX = pendingView.X, CenterZ = pendingView.Z };
            SeedMapTile? tile = null;
            SeedRenderedTile? rendered = null;
            if (loadMap)
            {
                if (generation == _contextGeneration) Current = Current with { Phase = "map" };
                var firstQuery = SeedMapTiles.Plan(pending, candidate.Profile)[0];
                tile = await Task.Run(() => candidate.MapAsync(firstQuery, ct), ct);
                rendered = await Task.Run(() => Render(firstQuery, tile), ct);
            }

            ct.ThrowIfCancellationRequested();
            if (generation != _contextGeneration || owner.Disposed) return false;
            var shapeChanged = !sameProfile || Current.World != pending.World;
            if (!sameProfile && old is not null) await old.DisposeAsync();
            _session = candidate; candidate = null;
            _language = language;
            _markers.Clear();
            _tiles.Clear();
            if (rendered is not null) _tiles[rendered.RequestKey] = (rendered, ++_usage);
            var restoredView = SeedMapPlanning.Normalize(new(pending.CenterX, pending.CenterZ, pending.BlocksPerPixel, Current.MapWidth, Current.MapHeight));
            var selection = SeedVersionSelection.Opened(Current.Versions, pending.Profile!, catalog.Version);
            Current = Current with
            {
                Profile = pending.Profile,
                Catalog = catalog,
                Terrain = catalog.Biomes.Length == 0 || Current.Terrain,
                Versions = selection.Versions,
                VersionIndex = selection.Index,
                Fingerprint = _session.Fingerprint,
                Seed = seed,
                Spawn = spawn,
                SeedInput = loadMap && Current.SeedInput == inputAtStart ? seed : Current.SeedInput,
                World = pending.World,
                SliceY = y,
                CenterX = restoredView.X,
                CenterZ = restoredView.Z,
                BlocksPerPixel = restoredView.BlocksPerPixel,
                Tiles = rendered is null ? [] : [rendered],
                MapPositions = [],
                FocusedPlace = focus ?? bookmark?.Position ?? spawn,
                PickedPoint = null,
                MarkerBusy = false,
                MarkerScope = null,
                MarkerRegionCount = 0,
                MarkerRegionsChecked = 0,
                MarkerLimited = false,
                RemainingMarkerRegions = 0,
                Opening = false,
                Error = string.Empty,
                Phase = "ready",
                CalculationMilliseconds = tile is null || tile.Cached ? 0 : tile.ElapsedMilliseconds,
                PendingPlace = bookmark?.Position,
                PendingTeleportY = bookmark?.TeleportY ?? 80,
                Search = keepSearch ? Current.Search : null,
                Previews = keepSearch ? Current.Previews : [],
                StructureResult = null,
                StructurePhase = "ready",
                StructureError = string.Empty,
            };
            owner.Structures.RestoreTargets();
            if (!keepSearch || shapeChanged) owner.Search.RestoreConditions();
            RequestViewport();
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception error) { if (generation == _contextGeneration) owner.Error(error); return false; }
        finally
        {
            if (candidate is not null && !ReferenceEquals(candidate, _session)) await candidate.DisposeAsync();
            if (entered) _contexts.Release();
            if (generation == _contextGeneration) Current = Current with { Opening = false, LoadingMap = false };
        }
    }

    internal async Task<SeedStructureResult> FindStructuresAsync(SeedStructureQuery query, CancellationToken cancellationToken)
    {
        if (Current.UseSavedMap) return await owner.Worlds.FindStructuresAsync(query, cancellationToken);
        _view?.Cancel();
        await _viewTask;
        var session = _session ?? throw new InvalidOperationException(Resources.SeedNoRuntime);
        try { return await Task.Run(() => session.FindStructuresAsync(query, cancellationToken), cancellationToken); }
        finally { RequestViewport(); }
    }

    internal async Task<SeedRegionResult> RegionAsync(SeedRegionQuery query, IProgress<SeedRegionProgress> progress, CancellationToken cancellationToken)
    {
        if (Current.UseSavedMap) return await owner.Worlds.RegionAsync(query, progress, cancellationToken);
        _view?.Cancel(); _point?.Cancel();
        await _viewTask;
        var session = _session ?? throw new InvalidOperationException(Resources.SeedNoRuntime);
        return await Task.Run(() => session.RegionAsync(query, progress, cancellationToken), cancellationToken);
    }
    internal Task<SeedBlockScene> BuildSceneAsync(SeedRegionResult snapshot, CancellationToken cancellationToken) =>
        Current.UseSavedMap ? owner.Worlds.BuildSceneAsync(snapshot, cancellationToken)
        : (_session ?? throw new InvalidOperationException(Resources.SeedNoRuntime)).BuildSceneAsync(snapshot, cancellationToken);
    internal Task<IReadOnlyDictionary<string, byte[]>> ItemImagesAsync(IEnumerable<string> ids, CancellationToken cancellationToken) =>
        Current.UseSavedMap ? owner.Worlds.ItemImagesAsync(ids, cancellationToken)
        : (_session ?? throw new InvalidOperationException(Resources.SeedNoRuntime)).ReadItemImagesAsync(ids, cancellationToken);
    internal void ResumeViewport() { if (!Current.SceneOpen) RequestViewport(); }

    internal void Resize(float width, float height)
    {
        if (width <= 0 || height <= 0 || (Math.Abs(Current.MapWidth - width) < .5 && Math.Abs(Current.MapHeight - height) < .5)) return;
        var view = SeedMapPlanning.Normalize(new(Current.CenterX, Current.CenterZ, Current.BlocksPerPixel, width, height));
        Current = Current with { MapWidth = width, MapHeight = height, CenterX = view.X, CenterZ = view.Z, BlocksPerPixel = view.BlocksPerPixel };
        RequestViewport();
    }
    internal void Pan(double x, double z, bool playerFollow = false)
    {
        if (Current.Opening) return;
        if (!playerFollow && Current.FollowPlayer) owner.Worlds.Follow(false);
        owner.DismissPopups?.Invoke(); _point?.Cancel();
        var (X, Z) = LimitPan(x, z);
        Current = Current with { CenterX = X, CenterZ = Z, PendingPlace = null };
        RequestViewport();
    }
    internal (double X, double Z) LimitPan(double x, double z)
    {
        var view = SeedMapPlanning.Normalize(new(x, z, Current.BlocksPerPixel, Current.MapWidth, Current.MapHeight));
        return (view.X, view.Z);
    }
    internal void BeginPan()
    {
        if (Current.FollowPlayer) owner.Worlds.Follow(false);
        _viewportGeneration++;
        owner.DismissPopups?.Invoke();
        _point?.Cancel();
        _view?.Cancel();
        owner.Worlds.SuspendViewport();
    }
    internal void Zoom(double factor, double? anchorX = null, double? anchorZ = null)
    {
        var old = Current.BlocksPerPixel;
        var next = SeedMapPlanning.ClampScale(old * factor, Current.MapWidth, Current.MapHeight);
        if (Math.Abs(next - old) < .001) return;
        var ratio = next / old;
        var x = anchorX ?? Current.CenterX; var z = anchorZ ?? Current.CenterZ;
        Current = Current with { BlocksPerPixel = next };
        Pan(x + ((Current.CenterX - x) * ratio), z + ((Current.CenterZ - z) * ratio));
    }

    internal void RequestViewport()
    {
        if (Current.UseSavedMap) { owner.Worlds.RequestViewport(); return; }

        if (owner.Disposed || Current.Opening || Current.SceneOpen || Current.RegionBusy || _session is null || Current.Profile is null || Current.Seed.Length == 0) return;
        _view?.Cancel(); _view?.Dispose();
        _view = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token);
        var generation = ++_viewportGeneration;
        var markerQueries = SeedMapPlanning.Markers(SeedMapTiles.Template(Current, _session.Profile), SeedMapTiles.View(Current), out var limited);
        if (Current.StructureLayers is { Length: 0 }) markerQueries = [];
        _markerKeys = [.. markerQueries.Select(SeedMapPlanning.Key)];
        var rasterCount = SeedMapTiles.Plan(Current, _session.Profile).Length;
        Current = Current with
        {
            Tiles = Visible(),
            MapPositions = VisiblePositions(),
            MapBusy = true,
            RemainingTiles = rasterCount,
            TotalTiles = rasterCount,
            MarkerBusy = false,
            RemainingMarkerRegions = 0,
            MarkerLimited = limited && markerQueries.Length > 0,
            MarkerRegionCount = markerQueries.Length,
            MarkerRegionsChecked = CompletedMarkerRegions(),
            MarkerScope = markerQueries.Length == 0 ? null : new()
            {
                MinX = markerQueries.Min(query => query.MinX),
                MinZ = markerQueries.Min(query => query.MinZ),
                MaxX = markerQueries.Max(query => query.MinX + query.Step - 1),
                MaxZ = markerQueries.Max(query => query.MinZ + query.Step - 1),
            },
        };
        _viewTask = RefreshViewportAsync(_session, markerQueries, _contextGeneration, generation, _view.Token);
    }

    private async Task RefreshViewportAsync(SeedExplorerSession session, SeedMapQuery[] markerQueries, long context, long generation, CancellationToken ct)
    {
        try
        {
            // Coalesce pointer/wheel bursts; this cancels before touching native generation.
            await Task.Delay(100, ct);
            var snapshot = Current;
            var queries = SeedMapTiles.Plan(snapshot, session.Profile);
            Current = Current with { MapBusy = true, RemainingTiles = queries.Length, TotalTiles = queries.Length, SampleStep = queries[^1].Step };
            var remaining = queries.Length;
            foreach (var query in queries)
            {
                ct.ThrowIfCancellationRequested();
                var key = SeedMapTiles.Key(query);
                if (!_tiles.TryGetValue(key, out var cached))
                {
                    var tile = await Task.Run(() => session.MapAsync(query, ct), ct);
                    var rendered = await Task.Run(() => Render(query, tile), ct);
                    ct.ThrowIfCancellationRequested();
                    if (context != _contextGeneration || generation != _viewportGeneration || owner.Disposed) return;
                    cached = (rendered, ++_usage); _tiles[key] = cached;
                    Current = Current with { CalculationMilliseconds = Current.CalculationMilliseconds + (tile.Cached ? 0 : tile.ElapsedMilliseconds) };
                }

                _tiles[key] = (cached.Tile, ++_usage);
                Current = Current with { Tiles = Visible(), RemainingTiles = --remaining };
                Evict();
            }
            // Raster completion enables export; structure work is a separate progressive layer.
            Current = Current with { MapBusy = false, RemainingTiles = 0 };
            if (Current.StructureLayers is not { Length: 0 })
            {
                var remainingMarkers = markerQueries.Length;
                Current = Current with { MarkerBusy = true, RemainingMarkerRegions = remainingMarkers };
                foreach (var query in markerQueries)
                {
                    ct.ThrowIfCancellationRequested();
                    var key = SeedMapPlanning.Key(query);
                    if (!_markers.TryGetValue(key, out var known))
                    {
                        var tile = await Task.Run(() => session.MapAsync(query, ct), ct);
                        ct.ThrowIfCancellationRequested();
                        if (context != _contextGeneration || generation != _viewportGeneration || owner.Disposed) return;
                        known = (tile, ++_usage);
                    }

                    _markers[key] = (known.Tile, ++_usage);
                    Current = Current with { MapPositions = VisiblePositions(), RemainingMarkerRegions = --remainingMarkers, MarkerRegionsChecked = CompletedMarkerRegions() };
                    foreach (var expired in _markers.Where(entry => !_markerKeys.Contains(entry.Key)).OrderBy(entry => entry.Value.Used).Take(Math.Max(0, _markers.Count - 128)).Select(entry => entry.Key).ToArray())
                        _markers.Remove(expired);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!ct.IsCancellationRequested && context == _contextGeneration && generation == _viewportGeneration) owner.Error(error); }
        finally { if (context == _contextGeneration && generation == _viewportGeneration) Current = Current with { MapBusy = false, RemainingTiles = 0, MarkerBusy = false, RemainingMarkerRegions = 0 }; }
    }

    private SeedRenderedTile Render(SeedMapQuery query, SeedMapTile tile) => new(Interlocked.Increment(ref _tileKey), SeedMapTiles.Key(query), tile,
        SeedMapTiles.Image(tile, true), SeedMapTiles.Image(tile, false));
    private SeedRenderedTile[] Visible() => SeedMapTiles.Layers(_tiles.Values.Select(entry => entry.Tile), Current);
    private int CompletedMarkerRegions() => _markerKeys.Count(key => _markers.ContainsKey(key));
    private SeedPosition[] VisiblePositions() => [.. _markers.Where(entry => _markerKeys.Contains(entry.Key) && SeedMapPlanning.Visible(entry.Value.Tile, SeedMapTiles.View(Current))).SelectMany(entry => entry.Value.Tile.Structures)
        .Where(position => Math.Abs(position.X - Current.CenterX) <= Current.MapWidth * Current.BlocksPerPixel / 2
            && Math.Abs(position.Z - Current.CenterZ) <= Current.MapHeight * Current.BlocksPerPixel / 2)
        .DistinctBy(position => (position.Kind, position.Id, position.X, position.Z))];

    private void Evict()
    {
        var step = SeedMapPlanning.Step(SeedMapTiles.View(Current));
        foreach (var key in _tiles.Where(entry => !SeedMapTiles.Visible(entry.Value.Tile.Tile, Current) || entry.Value.Tile.Tile.Step < step)
            .OrderBy(entry => SeedMapTiles.Visible(entry.Value.Tile.Tile, Current)).ThenBy(entry => entry.Value.Used)
            .Take(Math.Max(0, _tiles.Count - 128)).Select(entry => entry.Key).ToArray())
        {
            _tiles.Remove(key);
        }
    }

    internal async Task QueryPointAsync(int x, int z, Action<SeedPosition> show)
    {
        if (Current.UseSavedMap) { await owner.Worlds.QueryPointAsync(x, z, show); return; }

        if (_session is null || Current.Profile is null || Current.Opening) return;
        _point?.Cancel(); _point?.Dispose();
        var source = _point = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token);
        var ct = source.Token; var generation = _contextGeneration; var session = _session;
        var query = new SeedMapQuery
        {
            Profile = session.Profile,
            Seed = Current.Seed,
            World = Current.World with { },
            Y = Current.SliceY,
            MinX = Math.Clamp(x, -30_000_000, 29_999_999),
            MinZ = Math.Clamp(z, -30_000_000, 29_999_999),
            Side = 1,
            Step = 1,
            Structures = []
        };
        _view?.Cancel();
        var viewportGeneration = _viewportGeneration;
        try
        {
            var tile = await Task.Run(() => session.MapAsync(query, ct), ct);
            if (generation != _contextGeneration || ct.IsCancellationRequested || owner.Disposed) return;
            show(new() { Id = tile.Palette[tile.Biomes[0]], Kind = "biome", X = query.MinX, Y = tile.Heights[0], Z = query.MinZ, Biome = tile.Palette[tile.Biomes[0]] });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!ct.IsCancellationRequested && generation == _contextGeneration && ReferenceEquals(_point, source)) owner.Error(error); }
        finally
        {
            var current = ReferenceEquals(_point, source);
            if (current) _point = null;
            if (current && generation == _contextGeneration && viewportGeneration == _viewportGeneration) RequestViewport();
            source.Dispose();
        }
    }

    internal async Task<SeedMapTile?> ThumbnailAsync(SeedSearchHit hit, CancellationToken ct)
    {
        if (Current.Profile is null || !await EnsurePreviewSessionAsync(ct)) return null;
        var session = _session!; var snapshot = Current;
        var anchor = snapshot.World.Dimension == "overworld"
            ? (await Task.Run(() => session.SpawnAsync(new() { Profile = session.Profile, Seed = hit.Seed, World = snapshot.World with { } }, ct), ct)).Position
            : null;
        return await Task.Run(() => session.MapAsync(new()
        {
            Profile = session.Profile,
            Seed = hit.Seed,
            World = snapshot.World with { },
            Y = snapshot.SliceY,
            MinX = (anchor?.X ?? 0) - 2048,
            MinZ = (anchor?.Z ?? 0) - 2048,
            Step = 128,
            Side = 32,
            Structures = [],
            Terrain = false,
        }, ct), ct);
    }

    private async Task<bool> EnsurePreviewSessionAsync(CancellationToken ct)
    {
        var profile = Current.Profile;
        if (profile is null || owner.Disposed) return false;
        await _contexts.WaitAsync(ct);
        try
        {
            if (owner.Disposed || Current.Profile != profile) return false;
            if (_session is { IsAvailable: true } && _session.Profile == profile) return true;
            var candidate = await Task.Run(() => owner.Client.Seeds.OpenAsync(profile, ct), ct);
            if (owner.Disposed || ct.IsCancellationRequested || Current.Profile != profile)
            {
                await candidate.DisposeAsync();
                return false;
            }

            if (candidate.Fingerprint != Current.Fingerprint)
            {
                await candidate.DisposeAsync();
                throw new InvalidOperationException(Resources.SeedRuntimeChanged);
            }

            if (_session is not null) await _session.DisposeAsync();
            _session = candidate;
            return true;
        }
        finally { _contexts.Release(); }
    }

    internal async Task RefreshLanguageAsync(string language)
    {
        if (Current.UseSavedMap) { await owner.Worlds.RefreshLanguageAsync(language); return; }

        if (Current.Profile is not { } profile || Current.Catalog is null || _language == language || owner.Disposed) return;
        _language = language;
        var session = _session; var generation = _contextGeneration;
        try
        {
            var catalog = await Task.Run(() => session is not null && session.Profile == profile ? session.GetCatalogAsync(language, owner.Lifetime.Token)
                : owner.Client.Seeds.GetCatalogAsync(profile, language, owner.Lifetime.Token));
            if (generation == _contextGeneration && _language == language && profile == Current.Profile) Current = Current with { Catalog = catalog };
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (generation == _contextGeneration) owner.Error(error); }
    }

    internal async Task ExportAsync()
    {
        var snapshot = Current;
        if (snapshot.Exporting || snapshot.Opening || snapshot.MapBusy || snapshot.Tiles.Length == 0) return;
        Current = Current with { Exporting = true };
        try
        {
            var view = new SeedMapView(snapshot.CenterX - (snapshot.MapWidth * snapshot.BlocksPerPixel / 2),
                snapshot.CenterZ - (snapshot.MapHeight * snapshot.BlocksPerPixel / 2), snapshot.BlocksPerPixel,
                Math.Max(1, (int)Math.Floor(snapshot.MapWidth)), Math.Max(1, (int)Math.Floor(snapshot.MapHeight)), [.. snapshot.Tiles.Select(tile => tile.Tile)]);
            var picker = owner.Window.CreateFileSavePicker();
            picker.SuggestedFileName = (snapshot.UseSavedMap ? "world-" + snapshot.WorldSnapshot!.Selection.World : "seed-" + snapshot.Seed) + "-" + snapshot.World.Dimension.Replace(':', '-');
            string[] extensions = [".png"];
            picker.FileTypeChoices.Add(Resources.SeedPng, extensions);
            var file = await picker.PickSaveFileAsync();
            if (file is null || owner.Disposed) return;
            await Task.Run(() => SeedExplorerService.ExportViewAsync(view, file.Path, snapshot.Terrain, owner.Lifetime.Token));
            Current = Current with { Phase = "exported" };
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { owner.Error(error); }
        finally { Current = Current with { Exporting = false }; }
    }

    internal async Task SuspendAsync()
    {
        _opening?.Cancel(); _view?.Cancel(); _point?.Cancel(); _catalogCancel?.Cancel();
        _contextGeneration++; _viewportGeneration++;
        await _contexts.WaitAsync();
        try
        {
            await _catalogTask; await _viewTask;
            if (_session is not null) { await _session.DisposeAsync(); _session = null; }

            _tiles.Clear(); _markers.Clear();
        }
        finally { _contexts.Release(); }
    }

    /// <summary>Releases this viewport's native requests and session after the window cancels its owned work.</summary>
    public async ValueTask DisposeAsync()
    {
        _opening?.Cancel(); _view?.Cancel(); _point?.Cancel(); _catalogCancel?.Cancel();
        await _contexts.WaitAsync();
        try { await _catalogTask; await _viewTask; if (_session is not null) await _session.DisposeAsync(); }
        finally { _opening?.Dispose(); _view?.Dispose(); _point?.Dispose(); _catalogCancel?.Dispose(); _contexts.Dispose(); }
    }
}