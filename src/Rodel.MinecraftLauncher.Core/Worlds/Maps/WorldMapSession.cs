// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using System.Diagnostics;

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Owns one retained saved-world revision and bounded chunk/color caches; disposal never modifies the source world.</summary>
public sealed class WorldMapSession : IDisposable
{
    private readonly string _directory;
    private readonly Dictionary<string, string> _dimensions;
    private readonly Dictionary<string, WorldFileStamp> _files;
    private readonly Dictionary<(string Dimension, int X, int Z), SavedChunk?> _chunks = [];
    private readonly HashSet<(string Dimension, int X, int Z)> _missingChunks = [];
    private readonly Dictionary<(string Dimension, int X, int Z), string> _failedChunks = [];
    private readonly Dictionary<string, WorldFileStamp> _revisionInventory;
    private readonly Dictionary<(string Dimension, int X, int Z), LinkedListNode<(string Dimension, int X, int Z)>> _chunkNodes = [];
    private readonly LinkedList<(string Dimension, int X, int Z)> _recentChunks = [];
    private long _chunkBytes;
    private readonly SemaphoreSlim _queries = new(1);
    private readonly WorldBlockColors _colors;
    private readonly string? _clientJar;
    private readonly Func<SeedRegionResult, CancellationToken, Task<SeedRegionResult>>? _resolveDefaults;
    private readonly FiniteWorld? _finite;
    private readonly Lock _lifecycle = new();
    private int _readers;
    private volatile bool _disposed;
    private bool _resourcesDisposed;
    private SeedCatalog? _catalog;
    private readonly SeedCatalog _installedCatalog;
    private readonly Dictionary<string, HashSet<string>> _observedBiomes = [];
    private readonly Dictionary<string, HashSet<string>> _observedStructures = [];
    private readonly Dictionary<string, (int Minimum, int Maximum)> _observedHeights = [];

    internal WorldMapSession(WorldMapSnapshot snapshot, string directory, Dictionary<string, string> dimensions, Dictionary<string, WorldFileStamp> files, string? clientJar, FiniteWorld? finite = null, SeedCatalog? catalog = null,
        Func<SeedRegionResult, CancellationToken, Task<SeedRegionResult>>? resolveDefaults = null)
    {
        Snapshot = snapshot; _directory = directory; _dimensions = dimensions; _files = files;
        _colors = new(clientJar); _finite = finite;
        _revisionInventory = WorldMapFileInventory.Capture(directory, dimensions.Values, finite is not null, CancellationToken.None);
        _clientJar = clientJar;
        if (finite is not null) foreach (var id in snapshot.Dimensions) _observedHeights[id] = (0, finite.Height - 1);
        _resolveDefaults = resolveDefaults;
        _installedCatalog = catalog ?? new(snapshot.Version, [.. snapshot.Dimensions.Select(id => new SeedDimension(id, -32768, 65536, []))], [], []);
        _catalog = BuildCatalog();
    }

    /// <summary>Gets the metadata and source revision frozen when this session opened.</summary>
    public WorldMapSnapshot Snapshot { get; }

    /// <summary>Checks captured file stamps and newly saved region/player/legacy files without decoding NBT or textures; true requires reopening the session.</summary>
    public Task<bool> HasChangedAsync(CancellationToken cancellationToken = default) => ReadSavedAsync(() =>
    {
        foreach (var stamp in _files.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { stamp.EnsureUnchanged(); }
            catch (WorldSnapshotChangedException) { return Task.FromResult(true); }
        }

        var current = WorldMapFileInventory.Capture(_directory, _dimensions.Values, _finite is not null, cancellationToken);
        return Task.FromResult(current.Count != _revisionInventory.Count || current.Any(pair => !_revisionInventory.TryGetValue(pair.Key, out var old) || old != pair.Value));
    }, cancellationToken);

    /// <summary>Completes only confirmed missing saved samples through an optional isolated original runtime, using its real decorated surface for supported close views; saved data, failures and structure witnesses retain their own provenance.</summary>
    public Task<SeedMapTile> MapWithPreviewAsync(WorldMapQuery query, SeedExplorerSession? preview, CancellationToken cancellationToken = default) =>
        WorldMapPreview.ReadAsync(this, query, preview is null ? null : preview.MapAsync, cancellationToken, preview?.SupportsDecoratedMaps == true);

    /// <summary>Retains original structure predictions only when their anchor chunks have no saved record; unreadable or partially saved chunks never become predicted witnesses.</summary>
    public Task<SeedPosition[]> FilterPreviewStructuresAsync(string dimension, IEnumerable<SeedPosition> positions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positions);
        if (!Snapshot.PreviewSupported) return Task.FromResult(Array.Empty<SeedPosition>());
        if (!_dimensions.TryGetValue(dimension, out var directory)) throw new ArgumentException("The selected save has no data for this dimension.", nameof(dimension));
        var retained = positions.Take(10001).ToArray();
        if (retained.Length > 10000 || retained.Any(position => Math.Abs((long)position.X) > 30_000_000 || Math.Abs((long)position.Z) > 30_000_000))
            throw new ArgumentOutOfRangeException(nameof(positions));
        return ReadSavedAsync(async () =>
        {
            foreach (var stamp in _files.Values) stamp.EnsureUnchanged();
            var result = new List<SeedPosition>();
            foreach (var position in retained)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = (dimension, position.X >> 4, position.Z >> 4);
                if (_chunks.ContainsKey(key) || _failedChunks.ContainsKey(key)) continue;
                if (!_missingChunks.Contains(key))
                {
                    try
                    {
                        var chunk = await WorldRegionReader.ReadAsync(directory, key.Item2, key.Item3, _files, cancellationToken).ConfigureAwait(false);
                        if (chunk is not null) continue;
                    }
                    catch (WorldSnapshotChangedException) { throw; }
                    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or OverflowException)
                    { System.Diagnostics.Trace.TraceWarning("A saved structure anchor could not be inspected: {0}", error.Message); continue; }
                }

                result.Add(position with { Bounds = position.Bounds?.ToArray() });
            }

            foreach (var stamp in _files.Values) stamp.EnsureUnchanged();
            return result.ToArray();
        }, cancellationToken);
    }

    /// <summary>Reads a bounded frozen volume of actual saved blocks and inventories; missing chunks remain unknown and loot is never generated.</summary>
    public Task<SeedRegionResult> RegionAsync(WorldRegionQuery query, IProgress<SeedRegionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Selection != Snapshot.Selection) throw new ArgumentException("The region request belongs to another save.", nameof(query));
        if (!_dimensions.TryGetValue(query.Dimension, out var dimension)) throw new ArgumentException("The selected save has no data for this dimension.", nameof(query));
        return ReadSavedAsync(() => SavedRegionReader.ReadAsync(Snapshot, dimension, _files, _finite, query, progress, cancellationToken), cancellationToken);
    }

    /// <summary>Finds actual recorded structure starts inside a saved radius; empty or partial results never predict absence in unsaved terrain.</summary>
    public Task<SeedStructureResult> FindStructuresAsync(WorldStructureQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Selection != Snapshot.Selection) throw new ArgumentException("The structure request belongs to another save.", nameof(query));
        if (!_dimensions.TryGetValue(query.Dimension, out var dimension)) throw new ArgumentException("The selected save has no data for this dimension.", nameof(query));
        return ReadSavedAsync(() => SavedStructureReader.ReadAsync(Snapshot, dimension, _files, query, cancellationToken), cancellationToken);
    }

    /// <summary>Loads original models for a saved volume, resolving compact native state defaults when available without generating chunks or reopening the player world.</summary>
    public Task<SeedBlockScene> BuildSceneAsync(SeedRegionResult snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Fingerprint != Snapshot.Revision || snapshot.GenerationPolicy != "saved_snapshot_v1")
            throw new ArgumentException("The saved block snapshot belongs to another world revision.", nameof(snapshot));
        if (_clientJar is null || !File.Exists(_clientJar)) throw new FileNotFoundException("The installed original client JAR is required for block models and textures.");
        return ReadSavedAsync(async () =>
        {
            var resolved = _resolveDefaults is null ? snapshot : await _resolveDefaults(snapshot, cancellationToken).ConfigureAwait(false);
            return await Task.Run(() =>
            {
                using var client = File.OpenRead(_clientJar);
                return SeedModelResources.Load(resolved, client, cancellationToken);
            }, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>Reads installed item artwork for saved inventories without creating a generation runtime; missing resources retain their named slots.</summary>
    public Task<IReadOnlyDictionary<string, byte[]>> ReadItemImagesAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var retained = ids.Distinct(StringComparer.Ordinal).Take(1024).ToArray();
        return ReadSavedAsync(() => Task.Run<IReadOnlyDictionary<string, byte[]>>(() =>
        {
            if (_clientJar is null || !File.Exists(_clientJar)) return new Dictionary<string, byte[]>();
            using var client = File.OpenRead(_clientJar);
            return SeedItemResources.Read(retained, client, cancellationToken);
        }, cancellationToken), cancellationToken);
    }

    private async Task<T> ReadSavedAsync<T>(Func<Task<T>> read, CancellationToken ct)
    {
        lock (_lifecycle) { ObjectDisposedException.ThrowIf(_disposed, this); _readers++; }

        var entered = false;
        try
        {
            await _queries.WaitAsync(ct).ConfigureAwait(false); entered = true;
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await read().ConfigureAwait(false);
        }
        finally
        {
            if (entered) _queries.Release();
            lock (_lifecycle) { if (--_readers == 0 && _disposed) DisposeResources(); }
        }
    }

    /// <summary>Reads only actual saved samples; missing chunks remain unknown and are never generated or synthesized.</summary>
    public async Task<SeedMapTile> MapAsync(WorldMapQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Dimension);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (query.Selection != Snapshot.Selection) throw new ArgumentException("The map request belongs to another save.", nameof(query));
        if (query.Side is < 1 or > 256 || query.Step is < 1 or > 524288
            || Math.Abs((long)query.MinX) > 30_000_000 || Math.Abs((long)query.MinZ) > 30_000_000
            || (long)query.MinX + ((long)query.Step * (query.Side - 1)) > 30_000_000
            || (long)query.MinZ + ((long)query.Step * (query.Side - 1)) > 30_000_000 || query.Y is < -32768 or > 32767)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Select a bounded saved map rectangle.");
        }

        if (!_dimensions.TryGetValue(query.Dimension, out var dimension)) throw new ArgumentException("The selected save has no data for this dimension.", nameof(query));
        lock (_lifecycle) { ObjectDisposedException.ThrowIf(_disposed, this); _readers++; }

        var entered = false;
        try
        {
            await _queries.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var metadata in _files.Values.Where(file => Path.GetDirectoryName(file.Path) == _directory)) metadata.EnsureUnchanged();
            var timer = Stopwatch.StartNew();
            var sampleSide = query.Side + 2;
            var count = sampleSide * sampleSide;
            var biomes = new int[count]; var heights = new int[count]; var fluids = new int[count];
            var known = new bool[count]; var colors = new uint[count]; var eligible = new bool[count];
            var surfaceHeights = new int[count]; var depths = new int[count];
            var palette = new List<string> { "unknown" }; var paletteIds = new Dictionary<string, int>(StringComparer.Ordinal) { ["unknown"] = 0 };
            var structures = new Dictionary<(string Id, int X, int Z), SeedPosition>();
            var warnings = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<(int X, int Z)>();
            for (var row = 0; row < sampleSide; row++)
            {
                for (var column = 0; column < sampleSide; column++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var x = query.MinX + ((column - 1) * query.Step); var z = query.MinZ + ((row - 1) * query.Step); var index = (row * sampleSide) + column;
                    if (x < -30_000_000 || x >= 30_000_000 || z < -30_000_000 || z >= 30_000_000) continue;
                    if (_finite is not null)
                    {
                        if (_finite.Sample(x, z, query.Y) is { } sample)
                        {
                            known[index] = true; heights[index] = sample.Y; fluids[index] = sample.Fluid;
                            colors[index] = sample.Fluid switch { 1 => _colors.Water(sample.Biome), 2 => 0xe87822, _ => _colors.Color(sample.Block, sample.Biome) };
                            surfaceHeights[index] = sample.Y;
                        }

                        continue;
                    }

                    var chunkKey = (x >> 4, z >> 4);
                    var key = (query.Dimension, chunkKey.Item1, chunkKey.Item2);
                    SavedChunk? chunk;
                    if (_failedChunks.TryGetValue(key, out var failure))
                    {
                        chunk = null;
                        if (warnings.Count < 16) warnings.Add(failure);
                    }
                    else if (_missingChunks.Contains(key))
                    {
                        chunk = null;
                    }
                    else if (!_chunks.TryGetValue(key, out chunk) || (query.Y is not null && chunk?.SurfaceOnly == true))
                    {
                        var readFailed = false;
                        chunk = null;
                        try { chunk = await WorldRegionReader.ReadAsync(dimension, chunkKey.Item1, chunkKey.Item2, _files, cancellationToken).ConfigureAwait(false); }
                        catch (WorldSnapshotChangedException) { throw; }
                        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or OverflowException)
                        {
                            readFailed = true;
                            var warning = "WORLD_CHUNK_READ: " + chunkKey + ": " + error.Message;
                            if (warnings.Count < 16) warnings.Add(warning);
                            if (_failedChunks.Count >= 65536) _failedChunks.Remove(_failedChunks.Keys.First());
                            _failedChunks[key] = warning;
                        }

                        if (chunk is not null && !chunk.PreviewEligible)
                        {
                            var range = chunk.VerticalRange;
                            if (_observedHeights.TryGetValue(query.Dimension, out var previous)) range = (Math.Min(previous.Minimum, range.Minimum), Math.Max(previous.Maximum, range.Maximum));
                            if (!_observedHeights.TryGetValue(query.Dimension, out previous) || previous != range) { _observedHeights[query.Dimension] = range; _catalog = BuildCatalog(); }
                        }

                        if (query.Y is null && chunk is not null && !chunk.PreviewEligible)
                        {
                            try { chunk.RetainSurface(); }
                            catch (InvalidDataException error) { if (warnings.Count < 16) warnings.Add("WORLD_SAMPLE_READ: " + error.Message); }
                        }

                        if (!readFailed) CacheChunk(key, chunk);
                    }
                    else { var node = _chunkNodes[key]; _recentChunks.Remove(node); _recentChunks.AddLast(node); }

                    if (chunk is not null && visited.Add(chunkKey))
                        foreach (var structure in chunk.Structures) structures[(structure.Id, structure.X, structure.Z)] = structure;

                    if (chunk is null) { eligible[index] = _missingChunks.Contains(key); continue; }

                    if (chunk.PreviewEligible) { eligible[index] = true; continue; }

                    try
                    {
                        var sample = chunk.Sample(x & 15, z & 15, query.Y);
                        var (State, Biome, Y, Fluid, SurfaceY, Depth) = sample;
                        if (!paletteIds.TryGetValue(Biome, out var biome)) { biome = palette.Count; palette.Add(Biome); paletteIds.Add(Biome, biome); }

                        biomes[index] = biome; heights[index] = Y; fluids[index] = Fluid; known[index] = true;
                        surfaceHeights[index] = SurfaceY; depths[index] = Depth;
                        colors[index] = Fluid switch { 1 => _colors.Water(Biome), 2 => 0xe87822, _ => _colors.Color(State, Biome) };
                    }
                    catch (InvalidDataException error) { if (warnings.Count < 16) warnings.Add("WORLD_SAMPLE_READ: " + error.Message); }
                }
            }

            // Cached chunks still belong to the captured file revisions; never reuse them after a source write.
            foreach (var stamp in _files.Values) stamp.EnsureUnchanged();
            var catalogChanged = false;
            foreach (var biome in palette.Where(id => id != "unknown")) catalogChanged |= Observe(_observedBiomes, biome, query.Dimension);
            foreach (var structure in structures.Values) catalogChanged |= Observe(_observedStructures, structure.Id, query.Dimension);
            if (catalogChanged) _catalog = BuildCatalog();
            return new()
            {
                Source = "saved",
                SourceName = Snapshot.Name,
                Seed = Snapshot.Seed ?? string.Empty,
                World = Snapshot.Settings with { Dimension = query.Dimension },
                MinX = query.MinX,
                MinZ = query.MinZ,
                Step = query.Step,
                Side = query.Side,
                Y = query.Y,
                Palette = [.. palette],
                Biomes = Crop(biomes),
                Heights = Crop(heights),
                Fluids = Crop(fluids),
                Structures = [.. structures.Values],
                Known = Crop(known),
                Saved = Crop(known),
                PreviewEligible = Crop(eligible),
                SurfaceColors = Crop(colors),
                SurfaceHeights = Crop(surfaceHeights),
                WaterDepths = Crop(depths),
                ReliefHeights = [.. surfaceHeights.Select((height, index) => known[index] ? height : int.MinValue)],
                Warnings = [.. warnings],
                Fingerprint = Snapshot.Revision,
                ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds,
            };

            T[] Crop<T>(T[] samples)
            {
                var result = new T[query.Side * query.Side];
                for (var row = 0; row < query.Side; row++) samples.AsSpan(((row + 1) * sampleSide) + 1, query.Side).CopyTo(result.AsSpan(row * query.Side));
                return result;
            }
        }
        finally
        {
            if (entered) _queries.Release();
            lock (_lifecycle) { if (--_readers == 0 && _disposed) DisposeResources(); }
        }
    }

    private void CacheChunk((string Dimension, int X, int Z) key, SavedChunk? chunk)
    {
        if (_chunks.Remove(key, out var previous))
        {
            _chunkBytes -= previous?.CacheBytes ?? 128;
            _recentChunks.Remove(_chunkNodes[key]); _chunkNodes.Remove(key);
        }

        if (chunk is null)
        {
            if (_missingChunks.Count >= 65536) _missingChunks.Remove(_missingChunks.First());
            _missingChunks.Add(key); return;
        }

        var bytes = chunk?.CacheBytes ?? 128;
        while (_recentChunks.First is { } oldest && (_chunks.Count >= 16384 || _chunkBytes + bytes > 128 * 1024 * 1024))
        {
            _chunkBytes -= _chunks[oldest.Value]?.CacheBytes ?? 128;
            _chunks.Remove(oldest.Value); _chunkNodes.Remove(oldest.Value); _recentChunks.RemoveFirst();
        }

        _chunks[key] = chunk; _chunkNodes[key] = _recentChunks.AddLast(key); _chunkBytes += bytes;
    }

    /// <summary>Gets installed registry legends plus observed saved IDs; registry membership does not claim a structure exists in the save.</summary>
    public SeedCatalog GetCatalog() => Volatile.Read(ref _catalog)!;

    private SeedCatalog BuildCatalog() => new(Snapshot.Version,
        [.. _installedCatalog.Dimensions.Select(dimension => dimension with {
            MinY = dimension.Height == 65536 && _observedHeights.TryGetValue(dimension.Id, out var range) ? range.Minimum : dimension.MinY,
            Height = dimension.Height == 65536 && _observedHeights.TryGetValue(dimension.Id, out range) ? range.Maximum - range.Minimum + 1 : dimension.Height,
            Biomes = [.. dimension.Biomes.Concat(_observedBiomes.Where(pair => pair.Value.Contains(dimension.Id)).Select(pair => pair.Key)).Distinct(StringComparer.Ordinal)] })],
        [.. _installedCatalog.Biomes.Concat(_observedBiomes.Select(pair => new SeedBiome(pair.Key, [.. pair.Value]))).GroupBy(biome => biome.Id, StringComparer.Ordinal)
            .Select(group => new SeedBiome(group.Key, [.. group.SelectMany(biome => biome.Dimensions).Distinct(StringComparer.Ordinal)]))],
        [.. _installedCatalog.Structures.Concat(_observedStructures.Select(pair => new SeedStructure(pair.Key, [.. pair.Value], []))).GroupBy(structure => structure.Id, StringComparer.Ordinal)
            .Select(group => new SeedStructure(group.Key, [.. group.SelectMany(structure => structure.Dimensions).Distinct(StringComparer.Ordinal)], [.. group.SelectMany(structure => structure.Biomes).Distinct(StringComparer.Ordinal)]))])
    { Fingerprint = Snapshot.Revision, Names = _installedCatalog.Names };

    private static bool Observe(Dictionary<string, HashSet<string>> entries, string id, string dimension)
    {
        if (!entries.TryGetValue(id, out var dimensions)) entries[id] = dimensions = [];
        return dimensions.Add(dimension);
    }

    /// <summary>Releases this session's caches; the source save and game process are never owned or stopped.</summary>
    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true;
            // Queued readers retain the gate until they observe disposal and leave their finally.
            if (_readers == 0) DisposeResources();
        }
    }

    private void DisposeResources()
    {
        if (_resourcesDisposed) return;
        _resourcesDisposed = true; _colors.Dispose(); _chunks.Clear(); _missingChunks.Clear(); _failedChunks.Clear(); _chunkNodes.Clear(); _recentChunks.Clear(); _queries.Dispose();
    }
}