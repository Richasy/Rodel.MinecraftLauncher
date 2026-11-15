// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Features.Settings;
using Rodel.MinecraftLauncher.Core.Worlds.Maps;
using Sprout.Graphics;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Coordinates the selected instance/save and live observations; file and game ownership stay in Core.</summary>
internal sealed class SeedWorldController(SeedExplorerController owner) : IAsyncDisposable
{
    private WorldMapSession? _session;
    private readonly SeedWorldPreview _preview = new(owner);
    private CancellationTokenSource? _read, _view, _tracking, _watch, _point;
    private Task _viewTask = Task.CompletedTask, _trackingTask = Task.CompletedTask, _watchTask = Task.CompletedTask;
    private readonly SeedMapTileCache _tiles = new();
    private readonly Dictionary<string, SeedPosition[]> _previewMarkers = [];
    private long _generation, _viewport;
    private int _tileKey;
    private WorldPlayerObservation? _lastLive;
    private string? _language, _pendingLanguage;
    private IReadOnlyDictionary<string, string> _names = new Dictionary<string, string>();
    private SeedExplorerSnapshot? _manualDraft;
    private SeedExplorerSnapshot? _worldDraft;
    internal SeedExplorerSnapshot? ManualDraft => Current.WorldSource ? _manualDraft : null;
    private SeedExplorerSnapshot Current { get => owner.State.Current; set { if (!owner.Disposed) owner.State.Current = value; } }
    private WorldMapSession Session => Current.UseSavedMap && _session is { } session && Current.WorldSnapshot?.Selection == session.Snapshot.Selection
        ? session : throw new InvalidOperationException(Resources.SeedSelectSavedWorld);

    internal async Task<SeedRegionResult> RegionAsync(SeedRegionQuery query, IProgress<SeedRegionProgress> progress, CancellationToken ct)
    {
        var session = Session; _view?.Cancel(); await _viewTask;
        if (Current.SceneOpen && Current.ScenePreview) return await _preview.RegionAsync(session.Snapshot, query, progress, ct);
        return await Task.Run(() => session.RegionAsync(new()
        {
            Selection = session.Snapshot.Selection,
            Dimension = query.World.Dimension,
            Bounds = query.Bounds with { },
            IncludeBlocks = query.IncludeBlocks
        }, progress, ct), ct);
    }

    internal async Task<SeedStructureResult> FindStructuresAsync(SeedStructureQuery query, CancellationToken ct)
    {
        var session = Session; _view?.Cancel(); await _viewTask;
        return await Task.Run(() => session.FindStructuresAsync(new()
        {
            Selection = session.Snapshot.Selection,
            Dimension = query.World.Dimension,
            CenterX = query.CenterX,
            CenterZ = query.CenterZ,
            Radius = query.Radius,
            Targets = query.Targets,
            Limit = query.Limit
        }, ct), ct);
    }

    internal Task<SeedBlockScene> BuildSceneAsync(SeedRegionResult snapshot, CancellationToken ct) => snapshot.GenerationPolicy == "saved_snapshot_v1"
        ? Session.BuildSceneAsync(snapshot, ct) : _preview.BuildSceneAsync(Session.Snapshot, snapshot, ct);
    internal Task<IReadOnlyDictionary<string, byte[]>> ItemImagesAsync(IEnumerable<string> ids, CancellationToken ct) => Session.ReadItemImagesAsync(ids, ct);
    internal bool IsPreview(SeedPosition position) => Current.UseSavedMap
        && ((Current.PickedPointPreview && Current.PickedPoint == position)
            || Current.PreviewPositions.Any(item => item.Kind == position.Kind && item.Id == position.Id && item.X == position.X && item.Z == position.Z));
    internal async Task<bool> IsPreviewAsync(SeedPosition position, CancellationToken ct)
    {
        if (!Current.UseSavedMap || _session is not { } session) return false;
        if (IsPreview(position)) return true;
        var tile = await session.MapAsync(new()
        {
            Selection = session.Snapshot.Selection,
            Dimension = Current.World.Dimension,
            MinX = position.X,
            MinZ = position.Z,
            Side = 1,
            Step = 1
        }, ct);
        return tile.PreviewEligible is [true] && session.Snapshot.PreviewSupported && session.Snapshot.Seed is not null;
    }
    internal void CancelPoint() => _point?.Cancel();
    internal void SuspendViewport() { _view?.Cancel(); CancelPoint(); }

    internal async Task RefreshLanguageAsync(string language)
    {
        if (_language == language || _pendingLanguage == language || Current.WorldSnapshot is not { } snapshot || Current.Catalog is null) return;
        var generation = _generation;
        _pendingLanguage = language;
        try
        {
            using var labels = await Task.Run(() => owner.Client.WorldMaps.OpenAsync(snapshot.Selection, language, owner.Lifetime.Token), owner.Lifetime.Token);
            if (generation == _generation && !owner.Disposed && Current.UseSavedMap && LanguageSection.GameLanguage == language && Current.Catalog is { } catalog)
            {
                _language = language; _names = labels.GetCatalog().Names;
                Current = Current with { Catalog = catalog with { Names = _names } };
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (generation == _generation) owner.Error(error); }
        finally { if (_pendingLanguage == language) _pendingLanguage = null; }
    }

    internal async Task InitializeAsync()
    {
        var instances = await Task.Run(() => owner.Client.Instances.List().Select(instance => instance.Describe()).ToArray(), owner.Lifetime.Token);
        if (owner.Disposed) return;
        Current = Current with { Instances = instances };
        var previousFolder = Current.WorldFolder;
        if (instances.Length != 0)
        {
            await SelectInstanceAsync(instances.Any(instance => instance.Id == Current.WorldInstanceId) ? Current.WorldInstanceId! : instances[0].Id);
            if (previousFolder is not null && Current.Worlds.Any(world => world.Folder == previousFolder)) await SelectWorldAsync(previousFolder);
        }
    }

    internal async Task OpenSelectionAsync(WorldMapSelection selection)
    {
        await SelectSourceAsync(true);
        await SelectInstanceAsync(selection.InstanceId);
        await SelectWorldAsync(selection.World);
        Current = Current with { SavedMap = true };
        await OpenAsync();
    }

    internal async Task OpenBookmarkAsync(Core.Worlds.Seeds.SeedBookmark bookmark)
    {
        await OpenSelectionAsync(bookmark.SavedWorld!);
        if (!Current.WorldLoaded || Current.WorldSnapshot?.Selection != bookmark.SavedWorld) return;
        Current = Current with { BlocksPerPixel = SeedMapPlanning.ClampScale(bookmark.BlocksPerPixel, Current.MapWidth, Current.MapHeight) };
        await owner.Map.ChangeWorldAsync(bookmark.World, bookmark.SliceY);
        owner.Map.Pan(bookmark.X, bookmark.Z);
        if (bookmark.Position is not null) Current = Current with { PendingPlace = bookmark.Position, PendingTeleportY = bookmark.TeleportY };
    }

    internal async Task SelectSourceAsync(bool world)
    {
        if (Current.WorldSource == world) return;
        owner.DismissPopups?.Invoke();
        await owner.Regions.StopAsync();
        await owner.Structures.ResetAsync();
        if (world)
        {
            _manualDraft = Current;
            await owner.Search.StopAsync();
            await owner.Map.SuspendAsync();
            if (_worldDraft is { SavedMap: true, WorldLoaded: true } draft && _session?.Snapshot.Selection == draft.WorldSnapshot?.Selection)
            {
                Current = draft with
                {
                    WorldSource = true,
                    SearchMode = false,
                    FollowPlayer = false,
                    Player = null,
                    Bookmarks = Current.Bookmarks,
                    Width = Current.Width,
                    Height = Current.Height,
                    MapWidth = Current.MapWidth,
                    MapHeight = Current.MapHeight
                };
                await RefreshLanguageAsync(LanguageSection.GameLanguage);
                RequestViewport(); StartTracking(); StartWatching();
            }
            else
            {
                Current = Current with
                {
                    WorldSource = true,
                    SearchMode = false,
                    WorldLoaded = false,
                    Tiles = [],
                    MapPositions = [],
                    Catalog = null,
                    Player = null
                };
            }
        }
        else
        {
            _view?.Cancel(); _tracking?.Cancel(); _watch?.Cancel(); CancelPoint();
            await _viewTask;
            var worldState = Current;
            _worldDraft = worldState;
            Current = (_manualDraft ?? Current) with
            {
                WorldSource = false,
                WorldLoaded = false,
                Player = null,
                FollowPlayer = false,
                Instances = worldState.Instances,
                Worlds = worldState.Worlds,
                WorldInstanceId = worldState.WorldInstanceId,
                WorldFolder = worldState.WorldFolder,
                WorldSnapshot = worldState.WorldSnapshot,
                Bookmarks = worldState.Bookmarks,
                Width = worldState.Width,
                Height = worldState.Height,
                MapWidth = worldState.MapWidth,
                MapHeight = worldState.MapHeight,
            };
            if (Current.Seed.Length > 0 && Current.Profile is { } profile)
                await owner.Map.OpenAsync(profile, Current.World, Current.SliceY, Current.SeedInput);
        }
    }

    internal async Task SelectInstanceAsync(string instanceId)
    {
        var generation = ++_generation;
        _read?.Cancel(); _tracking?.Cancel(); _view?.Cancel(); _watch?.Cancel(); CancelPoint();
        await owner.Regions.StopAsync(); await owner.Structures.ResetAsync();
        if (generation != _generation || owner.Disposed) return;
        _read?.Dispose();
        var read = _read = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token);
        Current = Current with { WorldInstanceId = instanceId, WorldFolder = null, WorldSnapshot = null, Worlds = [], WorldLoaded = false, WorldReading = true, Player = null, FollowPlayer = false, Fingerprint = string.Empty };
        try
        {
            var worlds = await owner.Client.WorldMaps.ListAsync(instanceId, read.Token);
            if (generation != _generation || owner.Disposed) return;
            Current = Current with { Worlds = [.. worlds], WorldReading = false };
            if (worlds.FirstOrDefault() is { } first) await SelectWorldAsync(first.Folder);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (generation == _generation) { Current = Current with { WorldReading = false }; owner.Error(error); } }
    }

    internal async Task SelectWorldAsync(string folder)
    {
        if (Current.WorldInstanceId is not { } instance) return;
        var generation = ++_generation;
        _read?.Cancel(); _view?.Cancel(); _tracking?.Cancel(); _watch?.Cancel(); CancelPoint();
        await owner.Regions.StopAsync(); await owner.Structures.ResetAsync();
        if (generation != _generation || owner.Disposed) return;
        _read?.Dispose();
        var read = _read = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token);
        Current = Current with { WorldFolder = folder, WorldLoaded = false, WorldReading = true, WorldSnapshot = null, Player = null, FollowPlayer = false, WorldNeedsRefresh = false, Fingerprint = string.Empty };
        try
        {
            var snapshot = await owner.Client.WorldMaps.DescribeAsync(new(instance, folder), read.Token);
            if (generation != _generation || owner.Disposed) return;
            Current = Current with { WorldSnapshot = snapshot, WorldReading = false };
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (generation == _generation) { Current = Current with { WorldReading = false }; owner.Error(error); } }
    }

    internal async Task OpenAsync(bool background = false)
    {
        if (Current.WorldInstanceId is not { } instance || Current.WorldFolder is not { } folder || Current.WorldReading) return;
        if (!Current.SavedMap)
        {
            if (Current.WorldSnapshot is not { Seed: { } seed } snapshot) return;
            _generation++; _view?.Cancel(); _tracking?.Cancel(); _watch?.Cancel(); CancelPoint();
            await _viewTask;
            Current = Current with { WorldLoaded = false };
            await owner.Map.OpenAsync(snapshot.Profile, snapshot.Settings with { Dimension = Current.World.Dimension }, Current.SliceY, seed);
            Current = Current with { WorldLoaded = owner.Map.HasOpenRuntime && Current.Seed == seed };
            StartTracking();
            return;
        }

        var generation = ++_generation;
        _read?.Cancel(); _view?.Cancel(); _tracking?.Cancel(); _watch?.Cancel(); CancelPoint();
        _read?.Dispose(); _read = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token);
        var ct = _read.Token;
        if (!background) { await owner.Regions.StopAsync(); await owner.Structures.ResetAsync(); }

        var previousPlayer = Current.Player;
        if (!background) _preview.Retry();
        Current = Current with { WorldReading = true, LoadingMap = !background, Error = string.Empty };
        WorldMapSession? candidate = null;
        try
        {
            await owner.Map.SuspendAsync();
            await _viewTask;
            var language = LanguageSection.GameLanguage;
            candidate = await Task.Run(() => owner.Client.WorldMaps.OpenAsync(new(instance, folder), language, ct), ct);
            var snapshot = candidate.Snapshot;
            var dimension = snapshot.Dimensions.Contains(Current.World.Dimension, StringComparer.Ordinal) ? Current.World.Dimension : snapshot.Dimensions[0];
            var centerX = Current.WorldLoaded ? Current.CenterX : snapshot.Spawn?.X ?? 0;
            var centerZ = Current.WorldLoaded ? Current.CenterZ : snapshot.Spawn?.Z ?? 0;
            var pending = Current with
            {
                Profile = snapshot.Profile,
                World = snapshot.Settings with { Dimension = dimension },
                Seed = snapshot.Seed ?? string.Empty,
                Catalog = candidate.GetCatalog(),
                Fingerprint = snapshot.Revision,
                Spawn = snapshot.Spawn,
                CenterX = centerX,
                CenterZ = centerZ
            };
            var query = SeedMapTiles.Plan(pending, snapshot.Profile)[0];
            var tile = await candidate.MapAsync(Query(snapshot.Selection, query), ct);
            var rendered = await Task.Run(() => Render(query, tile), ct);
            if (generation != _generation || owner.Disposed) return;
            if (background && (Current.Panning || Current.SceneOpen || Current.RegionBusy || Current.SelectingRegion || Current.Exporting)) return;
            var backdrop = background && Current.WorldSnapshot?.Selection == snapshot.Selection
                ? new SeedMapBackdrop(snapshot.Selection, SeedMapTiles.Retain(Current, pending)) : null;
            _session?.Dispose(); _session = candidate; candidate = null; _tiles.Clear(); _previewMarkers.Clear(); _tiles.Store(rendered);
            _language = language; _names = pending.Catalog!.Names;
            Current = Current with
            {
                Profile = pending.Profile,
                World = pending.World,
                Seed = pending.Seed,
                Catalog = pending.Catalog,
                Fingerprint = pending.Fingerprint,
                Spawn = pending.Spawn,
                FocusedPlace = background ? Current.FocusedPlace : snapshot.Spawn,
                SelectedPlace = background ? Current.SelectedPlace : null,
                CenterX = background ? Current.CenterX : pending.CenterX,
                CenterZ = background ? Current.CenterZ : pending.CenterZ,
                WorldSnapshot = snapshot,
                WorldLoaded = true,
                WorldNeedsRefresh = false,
                WorldPreviewPreparing = false,
                WorldPreviewUnavailable = snapshot.Seed is null || !snapshot.PreviewSupported,
                WorldReading = false,
                LoadingMap = false,
                Opening = false,
                Tiles = [rendered],
                MapBackdrop = backdrop,
                MapPositions = tile.Structures,
                PreviewPositions = [],
                PickedPoint = null,
                PickedPointPreview = false,
                MarkerBusy = false,
                MapBusy = false,
                StructureResult = null,
                RegionData = null,
                RegionContainer = null,
                RegionItem = null,
                Phase = "ready",
                Player = background ? previousPlayer : null,
                FollowPlayer = background && Current.FollowPlayer
            };
            owner.Structures.RestoreTargets();
            RequestViewport(); StartTracking(); if (!background) StartWatching();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (generation == _generation)
            {
                if (background) { System.Diagnostics.Trace.TraceWarning("Saved map refresh will retry: {0}", error); Current = Current with { WorldNeedsRefresh = true }; }
                else
                {
                    owner.Error(error);
                }
            }
        }
        finally
        {
            candidate?.Dispose();
            if (generation == _generation)
            {
                Current = Current with { WorldReading = false, LoadingMap = false };
                if (background && Current.UseSavedMap) StartWatching();
            }
        }
    }

    internal void RequestViewport()
    {
        if (!Current.UseSavedMap || Current.WorldNeedsRefresh || owner.Disposed || _session is null || Current.Opening || Current.WorldReading || Current.SceneOpen || Current.RegionBusy || Current.Panning) return;
        CancelPoint();
        _view?.Cancel();
        _view?.Dispose();
        var source = _view = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token);
        var generation = ++_viewport;
        var queries = SeedMapTiles.Plan(Current, _session.Snapshot.Profile);
        _tiles.Protect(queries.Select(TileKey));
        var count = queries.Length;
        Current = Current with { Tiles = Visible(), MapPositions = SavedPositions(), MapBusy = true, MarkerBusy = false, TotalTiles = count, RemainingTiles = count };
        _viewTask = ReadViewportAsync(_session, _generation, generation, source.Token);
    }

    private async Task ReadViewportAsync(WorldMapSession session, long context, long viewportGeneration, CancellationToken ct)
    {
        try
        {
            await Task.Delay(100, ct);
            var state = Current;
            var queries = SeedMapTiles.Plan(state, session.Snapshot.Profile);
            _tiles.Protect(queries.Select(TileKey));
            Current = Current with { MapBusy = true, TotalTiles = queries.Length, RemainingTiles = queries.Length, SampleStep = queries[^1].Step };
            await SeedWorldMapLoading.ReadAsync(queries, ReadSavedAsync, CompletePreviewAsync, PublishAsync, ct);

            if (context == _generation && viewportGeneration == _viewport)
            {
                var visible = Visible();
                var complete = queries.Where(query => query.Side == 64).All(query => _tiles.TryGet(TileKey(query), out var tile) && tile.Tile.Known.All(value => value));
                Current = Current with { Tiles = visible, MapBackdrop = complete ? null : Current.MapBackdrop, MapBusy = false, RemainingTiles = 0 };
            }

            await ReadPreviewMarkersAsync(session, state, context, viewportGeneration, ct);

            Task<SeedMapTile> ReadSavedAsync(SeedMapQuery query, CancellationToken token) => _tiles.TryGet(TileKey(query), out var cached)
                ? Task.FromResult(cached.Tile) : Task.Run(() => session.MapAsync(Query(session.Snapshot.Selection, query), token), token);

            async Task<SeedMapTile> CompletePreviewAsync(SeedMapQuery query, SeedMapTile tile, CancellationToken token)
            {
                if (tile.Source == "saved" && !Current.WorldPreviewUnavailable && tile.PreviewEligible.Any(value => value)
                    && session.Snapshot.PreviewSupported && session.Snapshot.Seed is not null)
                {
                    var preview = await _preview.GetAsync(session.Snapshot, token);
                    if (preview is not null)
                    {
                        tile = await Task.Run(() => session.MapWithPreviewAsync(Query(session.Snapshot.Selection, query), preview, token), token);
                        token.ThrowIfCancellationRequested();
                        if (context == _generation && viewportGeneration == _viewport && tile.Warnings.Any(warning => warning.StartsWith("WORLD_PREVIEW_", StringComparison.Ordinal)))
                            Current = Current with { WorldPreviewUnavailable = true };
                    }
                }

                return tile;
            }

            async Task PublishAsync(SeedMapQuery query, SeedMapTile tile, bool completed)
            {
                if (context != _generation || viewportGeneration != _viewport || owner.Disposed) return;
                var key = TileKey(query);
                if (!_tiles.TryGet(key, out var rendered) || !ReferenceEquals(rendered.Tile, tile))
                {
                    rendered = await Task.Run(() => Render(query, tile), ct);
                    ct.ThrowIfCancellationRequested();
                    if (context != _generation || viewportGeneration != _viewport || owner.Disposed) return;
                    _tiles.Store(rendered);
                }

                Current = Current with
                {
                    Tiles = Visible(),
                    Catalog = session.GetCatalog() with { Names = _names },
                    RemainingTiles = Math.Max(0, Current.RemainingTiles - (completed ? 1 : 0)),
                    MapPositions = SavedPositions(),
                    PreviewPositions = []
                };
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { if (context == _generation) Current = Current with { WorldNeedsRefresh = true, Phase = "world_changed" }; }
        catch (Exception error) { if (context == _generation && !ct.IsCancellationRequested) owner.Error(error); }
        finally { if (context == _generation && viewportGeneration == _viewport) Current = Current with { MapBusy = false, RemainingTiles = 0, MarkerBusy = false }; }
    }

    private SeedRenderedTile[] Visible() => SeedMapTiles.Layers(_tiles.Tiles.Where(tile => tile.Tile.World.Dimension == Current.World.Dimension && tile.Tile.Y == Current.SliceY), Current);

    private SeedPosition[] SavedPositions() => [.. Visible().SelectMany(tile => tile.Tile.Structures).DistinctBy(position => (position.Id, position.X, position.Z))];

    private async Task ReadPreviewMarkersAsync(WorldMapSession session, SeedExplorerSnapshot state, long context, long viewport, CancellationToken ct)
    {
        if (state.StructureLayers is { Length: 0 } || !session.Snapshot.PreviewSupported || session.Snapshot.Seed is null || Current.WorldPreviewUnavailable
            || !Visible().Any(entry => entry.Tile.PreviewEligible.Any(value => value)))
        {
            return;
        }

        var queries = SeedMapPlanning.Markers(SeedMapTiles.Template(state, session.Snapshot.Profile), SeedMapTiles.View(state), out var limited);
        if (queries.Length == 0) return;
        var preview = await _preview.GetAsync(session.Snapshot, ct);
        if (preview is null) return;
        var savedKeys = SavedPositions().Select(position => (position.Id, position.X, position.Z)).ToHashSet();
        var completed = new List<SeedPosition>();
        Current = Current with { MarkerBusy = true, MarkerLimited = limited, MarkerRegionCount = queries.Length, MarkerRegionsChecked = 0 };
        foreach (var query in queries)
        {
            ct.ThrowIfCancellationRequested();
            var key = state.World.Dimension + ":" + SeedMapPlanning.Key(query);
            if (!_previewMarkers.TryGetValue(key, out var positions))
            {
                var tile = await Task.Run(() => preview.MapAsync(query with { Profile = preview.Profile }, ct), ct);
                positions = await session.FilterPreviewStructuresAsync(state.World.Dimension, tile.Structures, ct);
                if (context != _generation || viewport != _viewport || owner.Disposed) return;
                if (_previewMarkers.Count >= 128) _previewMarkers.Remove(_previewMarkers.Keys.First());
                _previewMarkers[key] = positions;
            }

            completed.AddRange(positions.Where(position => !savedKeys.Contains((position.Id, position.X, position.Z))));
            var predicted = completed.DistinctBy(position => (position.Id, position.X, position.Z)).ToArray();
            Current = Current with { PreviewPositions = predicted, MapPositions = [.. SavedPositions(), .. predicted], MarkerRegionsChecked = Current.MarkerRegionsChecked + 1 };
        }

        // Keep both witness kinds in frozen PNG metadata without treating predictions as recorded starts.
        foreach (var entry in Visible())
        {
            var span = entry.Tile.Side * entry.Tile.Step;
            var predicted = completed.Where(position => position.X >= entry.Tile.MinX && position.X < entry.Tile.MinX + span
                && position.Z >= entry.Tile.MinZ && position.Z < entry.Tile.MinZ + span).DistinctBy(position => (position.Id, position.X, position.Z)).ToArray();
            _tiles.Store(entry with
            {
                Tile = entry.Tile with
                {
                    PreviewStructures = predicted,
                    PreviewFingerprint = predicted.Length > 0 ? preview.Fingerprint : entry.Tile.PreviewFingerprint
                }
            });
        }

        Current = Current with { Tiles = Visible() };
    }

    internal async Task QueryPointAsync(int x, int z, Action<SeedPosition> show)
    {
        if (_session is null || !Current.UseSavedMap) return;
        _point?.Cancel(); _point?.Dispose();
        var source = _point = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token);
        var ct = source.Token;
        var generation = _generation;
        var viewport = _viewport;
        var dimension = Current.World.Dimension;
        var y = Current.SliceY;
        var session = _session;
        try
        {
            var query = new WorldMapQuery { Selection = session.Snapshot.Selection, Dimension = dimension, MinX = x, MinZ = z, Side = 1, Step = 1, Y = y };
            var tile = await session.MapAsync(query, ct);
            if (tile.PreviewEligible is [true] && await _preview.GetAsync(session.Snapshot, ct) is { } preview)
                tile = await session.MapWithPreviewAsync(query, preview, ct);
            if (!ct.IsCancellationRequested && generation == _generation && viewport == _viewport && tile.World.Dimension == Current.World.Dimension && tile.Y == Current.SliceY && tile.Known[0])
            {
                var biome = tile.Biomes[0] is var index && index >= 0 && index < tile.Palette.Length ? tile.Palette[index] : "unknown";
                var position = new SeedPosition { Id = biome, Kind = "biome", X = x, Y = tile.Heights[0], Z = z, Biome = biome };
                show(position);
                Current = Current with { PickedPointPreview = tile.Saved is [false] };
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (generation == _generation && !ct.IsCancellationRequested) owner.Error(error); }
    }

    internal void SetTracking(bool enabled)
    {
        Current = Current with { TrackPlayer = enabled, FollowPlayer = enabled && Current.FollowPlayer };
        if (enabled) StartTracking(); else _tracking?.Cancel();
    }

    internal void SelectPlayer(string? playerId)
    {
        Current = Current with { PlayerId = playerId, Player = null, FollowPlayer = false };
        StartTracking();
    }

    internal void Follow(bool follow)
    {
        Current = Current with { FollowPlayer = follow && Current.Player?.State == "live" };
        if (Current.FollowPlayer) LocatePlayer();
    }

    internal void LocatePlayer()
    {
        if (Current.Player?.Player is not { } player) return;
        if (Current.World.Dimension != player.Dimension)
        {
            _ = LocateOtherDimensionAsync(player);
            return;
        }

        owner.Map.Pan(player.X, player.Z, playerFollow: true);
    }

    private async Task LocateOtherDimensionAsync(SavedWorldPlayer player)
    {
        var generation = _generation;
        try
        {
            await owner.Map.ChangeWorldAsync(Current.World with { Dimension = player.Dimension }, Current.SliceY);
            if (generation == _generation && Current.WorldSource && Current.Player?.Player is { } current && current.Id == player.Id && current.Dimension == player.Dimension)
                owner.Map.Pan(current.X, current.Z, playerFollow: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (generation == _generation) owner.Error(error); }
    }

    private void StartWatching()
    {
        _watch?.Cancel(); _watch?.Dispose(); _watch = null;
        if (!Current.UseSavedMap || _session is not { } session) return;
        var ct = (_watch = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token)).Token;
        var generation = _generation;
        _watchTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            try
            {
                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    bool changed;
                    try { changed = await session.HasChangedAsync(ct).ConfigureAwait(false); }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }

                    if (!changed) continue;
                    owner.Dispatcher.TryEnqueue(() =>
                    {
                        if (owner.Disposed || ct.IsCancellationRequested || generation != _generation || !Current.UseSavedMap
                            || Current.WorldReading || Current.Opening || Current.SceneOpen || Current.RegionBusy || Current.StructureSearching
                            || Current.Panning || Current.SelectingRegion || Current.Exporting)
                        {
                            return;
                        }

                        _ = OpenAsync(background: true);
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }, ct);
    }

    private void StartTracking()
    {
        _tracking?.Cancel();
        _tracking?.Dispose(); _tracking = null;
        if (!Current.TrackPlayer || !Current.WorldSource || Current.WorldSnapshot is not { } snapshot) return;
        var token = (_tracking = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token)).Token;
        var generation = _generation;
        var playerId = Current.PlayerId;
        _lastLive = null;
        _trackingTask = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                do
                {
                    var observation = await owner.Client.WorldMaps.ObservePlayerAsync(snapshot, playerId, token).ConfigureAwait(false);
                    owner.Dispatcher.TryEnqueue(() =>
                    {
                        if (generation != _generation || owner.Disposed || token.IsCancellationRequested || !Current.WorldSource) return;
                        if (observation.State == "live") _lastLive = observation;
                        else if (observation.State is "disconnected" or "saved" or "unavailable" && _lastLive is not null)
                            observation = observation with { State = "disconnected", Player = _lastLive.Player };
                        var changed = Current.Player?.WorldEpoch is { } previous && observation.WorldEpoch != previous;
                        Current = Current with { Player = observation, FollowPlayer = Current.FollowPlayer && observation.State == "live" && !changed };
                        if (Current.FollowPlayer) LocatePlayer();
                    });
                }
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                System.Diagnostics.Trace.TraceWarning("World player observation failed: {0}", error);
                owner.Dispatcher.TryEnqueue(() =>
                {
                    if (generation == _generation && !owner.Disposed && !token.IsCancellationRequested)
                        Current = Current with { Player = new(snapshot.Selection, "disconnected", _lastLive?.Player ?? snapshot.Players.FirstOrDefault()), FollowPlayer = false };
                });
            }
        }, token);
    }

    private static string TileKey(SeedMapQuery query) => query.World.Dimension + ":" + query.Y + ":" + SeedMapTiles.Key(query);
    private SeedRenderedTile Render(SeedMapQuery query, SeedMapTile tile) => new(++_tileKey, TileKey(query), tile, SeedMapTiles.Image(tile, true), SeedMapTiles.Image(tile, false));
    private static WorldMapQuery Query(WorldMapSelection selection, SeedMapQuery query) => new()
    {
        Selection = selection,
        Dimension = query.World.Dimension,
        MinX = query.MinX,
        MinZ = query.MinZ,
        Side = query.Side,
        Step = query.Step,
        Y = query.Y,
        Decorated = query.Decorated
    };

    /// <summary>Cancels owned reads/observations before closing the read-only save session.</summary>
    public async ValueTask DisposeAsync()
    {
        _read?.Cancel(); _view?.Cancel(); _tracking?.Cancel(); _watch?.Cancel(); _point?.Cancel();
        try { await _viewTask; await _trackingTask; await _watchTask; }
        catch (OperationCanceledException) { }

        _session?.Dispose(); await _preview.DisposeAsync(); _read?.Dispose(); _view?.Dispose(); _tracking?.Dispose(); _watch?.Dispose(); _point?.Dispose();
    }
}