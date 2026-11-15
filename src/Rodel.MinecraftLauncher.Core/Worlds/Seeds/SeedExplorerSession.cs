// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Rodel.MinecraftLauncher.Core.Worlds.Seeds;

/// <summary>Owns the original query JVM, temporary spawn children and exact-runtime caches; callers must dispose it on context/window exit.</summary>
public sealed class SeedExplorerSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _directory;
    private readonly LauncherPaths _paths;
    private readonly SeedRuntimeBinding _binding;
    private readonly SemaphoreSlim _requests = new(1);
    private readonly SemaphoreSlim _writes = new(1);
    private readonly StringBuilder _errors = new();
    private readonly Task _stderr;
    private int _disposed;
    private bool _broken;

    private SeedExplorerSession(Process process, string directory, LauncherPaths paths, SeedRuntimeBinding binding)
    {
        _process = process;
        _directory = directory;
        _paths = paths;
        _binding = binding;
        _stderr = DrainErrorsAsync();
    }

    /// <summary>Gets the exact adapter, original client and dependency fingerprint used by this session.</summary>
    public string Fingerprint => _binding.Fingerprint;
    /// <summary>Gets the original game version, independently of a loader profile's display name.</summary>
    public string MinecraftVersion => _binding.Version;
    /// <summary>Gets the executable used by the isolated query JVM, which can differ from a legacy game's Java 6/7 preference.</summary>
    public string QueryJavaPath => _binding.JavaPath;
    /// <summary>Gets the installed original-runtime profile used by this session.</summary>
    public SeedRuntimeProfile Profile => _binding.Profile with { };

    internal bool SupportsDecoratedMaps => !_binding.Portable;

    /// <summary>Gets whether the owned worker is still available for reuse; a request still validates its runtime and can observe a later process exit.</summary>
    public bool IsAvailable
    {
        get
        {
            if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _broken)) return false;
            try { return !_process.HasExited; }
            catch (InvalidOperationException) { return false; }
        }
    }

    internal static async Task<SeedExplorerSession> StartAsync(LauncherPaths paths, SeedRuntimeBinding binding, string directory, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var arguments = new List<string> { "-Xms64m", "-Xmx2048m", "-Dfile.encoding=UTF-8" };
        if (binding.JavaMajorVersion >= 9) arguments.Add($"-XX:ActiveProcessorCount={Math.Clamp(Environment.ProcessorCount, 1, 4)}");
        arguments.AddRange(["-cp", string.Join(Path.PathSeparator, binding.Classpath)]);
        if (binding.Portable) arguments.AddRange(["io.github.richasy.rodel.bridge.OriginalSeedWorker", binding.ClientJar, binding.Version, directory, binding.MappingsPath ?? string.Empty]);
        else arguments.AddRange(["io.github.richasy.rodel.seed.SeedWorker", Environment.ProcessId.ToString(CultureInfo.InvariantCulture)]);
        var (Arguments, _) = await JavaCommandLine.PrepareAsync(binding.JavaPath, [.. arguments], binding.JavaMajorVersion, directory, directory, ct: ct).ConfigureAwait(false);
        var info = new ProcessStartInfo(binding.JavaPath)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in Arguments) info.ArgumentList.Add(argument);
        var process = new Process { StartInfo = info };
        SeedExplorerSession? session = null;
        try
        {
            if (!process.Start()) throw new IOException("The original seed worker could not start.");
            session = new(process, directory, paths, binding);
            var envelope = await session.ReadEnvelopeAsync().WaitAsync(TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
            if (envelope.Kind == "unavailable")
                throw new LauncherOperationException("SEED_ORIGINAL_SEED_UNAVAILABLE", envelope.Error ?? "The selected original game does not expose seed-based generation. Its existing saved maps can still be inspected.");
            var ready = envelope.Kind == "ready" ? envelope.Data.Deserialize(SeedWorkerJsonContext.Default.SeedWorkerReady) : null;
            if (ready is not { Protocol: 1 } || ready.Version != binding.Version)
                throw new InvalidDataException("The original seed worker returned an incompatible startup protocol/version.");
            return session;
        }
        catch
        {
            if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
            else process.Dispose();
            throw;
        }
    }

    /// <summary>Reads native registries and the installed game's localized biome labels through this isolated worker, without launching a playable world.</summary>
    public async Task<SeedCatalog> GetCatalogAsync(string language = "en_us", CancellationToken cancellationToken = default)
    {
        _binding.EnsureUnchanged();
        var catalog = await new SeedCatalogCache(_paths, Fingerprint, MinecraftVersion).GetAsync(ReadOriginalCatalogAsync, cancellationToken).ConfigureAwait(false);
        var translations = await SeedGameLanguage.ReadAsync(_binding, language, cancellationToken).ConfigureAwait(false);
        var names = new Dictionary<string, string>(catalog.Names);
        foreach (var translation in translations) names[translation.Key] = translation.Value;
        _binding.EnsureUnchanged();
        return catalog with { Names = names, Fingerprint = Fingerprint };
    }

    internal Task<SeedCatalog> ReadOriginalCatalogAsync(CancellationToken ct) => SendAsync(new(null, "catalog"), SeedJsonContext.Default.SeedCatalog, null, ct);

    private async Task<SeedRegionResult> ReadPortableRegionAsync(SeedRegionQuery query, Func<JsonElement, Task>? progress, CancellationToken ct)
    {
        var generated = await SendAsync(new(null, "generate", Region: query), SeedWorkerJsonContext.Default.SeedGeneratedWorld, null, ct, progress).ConfigureAwait(false);
        return await SeedPortableRegion.ReadAsync(generated, _directory, query, ct).ConfigureAwait(false);
    }

    /// <summary>Evaluates an explicit original container table and saved seed in a disposable no-player/default-luck world; no inventory is written back.</summary>
    public async Task<SeedContainer> PredictLootAsync(SeedLootQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        SeedValidation.Profile(query.Profile, Profile);
        if (Math.Abs((long)query.X) > 30_000_000 || Math.Abs((long)query.Z) > 30_000_000 || query.Y is < -32768 or > 32767
            || string.IsNullOrWhiteSpace(query.Id) || query.Id.Length > 256 || string.IsNullOrWhiteSpace(query.LootTable) || query.LootTable.Length > 256
            || !long.TryParse(query.LootSeed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
        {
            throw new ArgumentException("Select a recorded original container, table and signed loot seed.", nameof(query));
        }

        _binding.EnsureUnchanged();
        query = query with { Profile = Profile, World = query.World with { }, Seed = await NormalizeSeedAsync(query.Seed, cancellationToken).ConfigureAwait(false) };
        var container = await SendAsync(new(null, "loot", Loot: query), SeedJsonContext.Default.SeedContainer, null, cancellationToken).ConfigureAwait(false);
        if (container.Id != query.Id || container.X != query.X || container.Y != query.Y || container.Z != query.Z
            || container.State != "generated" || container.LootTable != query.LootTable || container.LootSeed != query.LootSeed
            || container.Slots is < 1 or > 256 || container.Items.Length > container.Slots
            || container.Items.Any(item => item.Slot < 0 || item.Slot >= container.Slots || item.Count < 1 || string.IsNullOrWhiteSpace(item.Id))
            || container.Items.Select(item => item.Slot).Distinct().Count() != container.Items.Length)
        {
            throw new InvalidDataException("The original loot prediction does not match its recorded source.");
        }

        return container;
    }

    /// <summary>Uses the original text-seed parser for nonnumeric input and original random-seed generation for empty input.</summary>
    public async Task<string> NormalizeSeedAsync(string? input, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (long.TryParse(input, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seed))
            return seed.ToString(CultureInfo.InvariantCulture);
        var value = await SendAsync(new(null, "seed", Seed: input), SeedWorkerJsonContext.Default.SeedValue, null, cancellationToken).ConfigureAwait(false);
        return value.Seed;
    }

    /// <summary>Returns the new world's actual shared spawn from original initialization, caching only complete validated results for this exact runtime.</summary>
    public async Task<SeedSpawnResult> SpawnAsync(SeedSpawnQuery query, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        SeedValidation.Spawn(query);
        SeedValidation.Profile(query.Profile, Profile);
        _binding.EnsureUnchanged();
        query = query with { Profile = Profile, World = query.World with { }, Seed = await NormalizeSeedAsync(query.Seed, cancellationToken).ConfigureAwait(false) };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(query, SeedJsonContext.Default.SeedSpawnQuery);
        var key = Convert.ToHexStringLower(SHA256.HashData([.. Encoding.UTF8.GetBytes(Fingerprint + "/world_spawn_v1/"), .. bytes]));
        var path = Path.Combine(_paths.CacheDirectory, "seed-explorer", "spawns", key + ".json");
        using var lease = await ResourceLease.AcquireAsync(resource: path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (File.Exists(path))
        {
            try
            {
                var cached = JsonFiles.ReadSnapshot(path, stream => JsonSerializer.Deserialize(stream, SeedJsonContext.Default.SeedSpawnResult));
                if (cached is not null && cached.Fingerprint == Fingerprint)
                {
                    SeedValidation.Spawn(cached, query);
                    return cached with { Cached = true };
                }
            }
            catch (Exception error) when (error is IOException or JsonException or InvalidDataException) { }
        }

        var result = await SendAsync(new(null, "spawn", Spawn: query), SeedJsonContext.Default.SeedSpawnResult, null, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        _binding.EnsureUnchanged();
        SeedValidation.Spawn(result, query);
        result = result with { Fingerprint = Fingerprint };
        try { await JsonFiles.WriteBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(result, SeedJsonContext.Default.SeedSpawnResult), cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("The original world spawn could not be cached: {0}", error);
        }

        return result;
    }

    /// <summary>Reads or computes one complete tile without modifying a player world; failed/canceled calculations never replace a cached tile.</summary>
    public async Task<SeedMapTile> MapAsync(SeedMapQuery query, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        SeedValidation.Map(query);
        SeedValidation.Profile(query.Profile, Profile);
        _binding.EnsureUnchanged();
        query = query with { Profile = Profile, World = query.World with { }, Structures = query.Structures?.ToArray() };
        var seed = await NormalizeSeedAsync(query.Seed, cancellationToken).ConfigureAwait(false);
        query = query with { Seed = seed, Profile = Profile };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(query, SeedJsonContext.Default.SeedMapQuery);
        var key = Convert.ToHexStringLower(SHA256.HashData([.. Encoding.UTF8.GetBytes(Fingerprint + "/surface-map-v1/"), .. bytes]));
        var path = Path.Combine(_paths.CacheDirectory, "seed-explorer", "tiles", key + ".json");
        using var lease = await ResourceLease.AcquireAsync(resource: path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (File.Exists(path))
        {
            try
            {
                var tile = JsonFiles.ReadSnapshot(path, stream => JsonSerializer.Deserialize(stream, SeedJsonContext.Default.SeedMapTile));
                if (tile is not null && tile.Fingerprint == Fingerprint)
                {
                    SeedValidation.Tile(tile, query);
                    return tile with { Cached = true };
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
            {
                // A corrupt disposable tile is recalculated; the previous file remains until a complete replacement exists.
            }
        }

        var result = query.Decorated
            ? await ReadSurfaceMapAsync(query, cancellationToken).ConfigureAwait(false)
            : await SendAsync(new(null, "map", Map: query), SeedJsonContext.Default.SeedMapTile, null, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        result = result with { Fingerprint = Fingerprint };
        SeedValidation.Tile(result, query);
        await JsonFiles.WriteBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(result, SeedJsonContext.Default.SeedMapTile), cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<SeedMapTile> ReadSurfaceMapAsync(SeedMapQuery query, CancellationToken ct)
    {
        if (!SupportsDecoratedMaps) throw new LauncherOperationException("SEED_SURFACE_UNAVAILABLE", "This original adapter does not expose decorated map sampling; base terrain maps remain available.");
        var surface = await SendAsync(new(null, "surface", Map: query), SeedWorkerJsonContext.Default.SeedSurfaceMap, null, ct).ConfigureAwait(false);
        var count = query.Side * query.Side;
        SeedValidation.Tile(surface.Tile, query with { Decorated = false });
        if (surface.Palette is not { Length: > 0 and <= 65536 } || surface.Blocks.Length != count || surface.Tints.Length != count
            || surface.SurfaceHeights.Length != count || surface.WaterDepths.Length != count || surface.WaterColors.Length != count
            || surface.ReliefHeights.Length != (query.Side + 2) * (query.Side + 2)
            || surface.Blocks.Any(index => index < 0 || index >= surface.Palette.Length)
            || surface.Palette.Any(state => state is null || string.IsNullOrWhiteSpace(state.Id) || state.Properties is null)
            || surface.WaterColors.Any(color => color > 0xffffff)
            || surface.Tints.Any(tints => tints is not { Length: <= 64 } || tints.Any(color => color is < -1 or > 0xffffff)))
        {
            throw new InvalidDataException("Original surface samples are incomplete.");
        }

        using var file = File.OpenRead(_binding.ClientJar);
        using var resources = new SeedSurfaceColors(file);
        var colors = new uint[count];
        var unavailable = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var state = surface.Palette[surface.Blocks[i]];
            var material = surface.Tile.Fluids[i] == 0 ? resources.Color(state, surface.Tints[i]) : null;
            if (material is null && surface.Tile.Fluids[i] == 0 && state.Id is not ("minecraft:air" or "minecraft:cave_air" or "minecraft:void_air")) unavailable.Add(state.Id);
            colors[i] = surface.Tile.Fluids[i] switch
            {
                1 => surface.WaterColors[i],
                2 => 0xe87822,
                _ => material ?? 0xa9a191,
            };
        }

        var markers = query.Structures is { Length: 0 } ? []
            : (await SendAsync(new(null, "map", Map: query with { Terrain = false, Decorated = false }), SeedJsonContext.Default.SeedMapTile, null, ct).ConfigureAwait(false)).Structures;
        _binding.EnsureUnchanged();
        return surface.Tile with
        {
            SurfaceColors = colors,
            SurfaceHeights = surface.SurfaceHeights,
            WaterDepths = surface.WaterDepths,
            ReliefHeights = surface.ReliefHeights,
            Decorated = true,
            Structures = markers,
            Warnings = [.. unavailable.Order(StringComparer.Ordinal).Select(id => "SEED_MAP_MODEL_UNAVAILABLE: " + id)]
        };
    }

    /// <summary>Finds assembled structures in this warmed original runtime, ordered by horizontal distance; no decorated chunks or player save is created.</summary>
    public async Task<SeedStructureResult> FindStructuresAsync(SeedStructureQuery query, CancellationToken cancellationToken = default)
    {
        SeedValidation.Structures(query);
        SeedValidation.Profile(query.Profile, Profile);
        query = query with { Profile = Profile, World = query.World with { }, Targets = [.. query.Targets] };
        query = query with { Seed = await NormalizeSeedAsync(query.Seed, cancellationToken).ConfigureAwait(false) };
        var result = await SendAsync(new(null, "structures", Structures: query), SeedJsonContext.Default.SeedStructureResult, null, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        SeedValidation.Structures(result, query);
        return result with { Fingerprint = Fingerprint };
    }

    /// <summary>Generates a frozen original block volume, ordinary-container loot and material counts; no player world is opened or modified.</summary>
    public async Task<SeedRegionResult> RegionAsync(SeedRegionQuery query, IProgress<SeedRegionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        SeedValidation.Region(query);
        SeedValidation.Profile(query.Profile, Profile);
        _binding.EnsureUnchanged();
        query = query with { Profile = Profile, World = query.World with { }, Bounds = query.Bounds with { } };
        query = query with { Seed = await NormalizeSeedAsync(query.Seed, cancellationToken).ConfigureAwait(false) };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(query, SeedJsonContext.Default.SeedRegionQuery);
        var key = Convert.ToHexStringLower(SHA256.HashData([.. Encoding.UTF8.GetBytes(Fingerprint + "/fixed_region_v1/"), .. bytes]));
        var path = Path.Combine(_paths.CacheDirectory, "seed-explorer", "regions", key + ".json");
        using var lease = await ResourceLease.AcquireAsync(resource: path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (File.Exists(path))
        {
            try
            {
                var cached = JsonFiles.ReadSnapshot(path, stream => JsonSerializer.Deserialize(stream, SeedJsonContext.Default.SeedRegionResult));
                if (cached is not null && cached.Fingerprint == Fingerprint)
                {
                    SeedValidation.Region(cached, query);
                    return cached with { Cached = true };
                }
            }
            catch (Exception error) when (error is IOException or JsonException or InvalidDataException) { }
        }

        Func<JsonElement, Task>? nativeProgress = progress is null ? null : snapshot =>
            {
                var value = snapshot.Deserialize(SeedJsonContext.Default.SeedRegionProgress) ?? throw new InvalidDataException("Original region progress is missing.");
                if (value.Completed < 0 || value.Total < value.Completed || value.Phase is not ("loading" or "terrain" or "features" or "full" or "scan"))
                    throw new InvalidDataException("Invalid original region progress.");
                progress.Report(value);
                return Task.CompletedTask;
            };
        var result = _binding.Portable
            ? await ReadPortableRegionAsync(query, nativeProgress, cancellationToken).ConfigureAwait(false)
            : await SendAsync(new(null, "region", Region: query), SeedJsonContext.Default.SeedRegionResult, null, cancellationToken, nativeProgress).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        result = result with { Fingerprint = Fingerprint };
        SeedValidation.Region(result, query);
        await JsonFiles.WriteBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(result, SeedJsonContext.Default.SeedRegionResult), cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Resolves original model tint-index colors from a frozen state/biome snapshot in bounded requests; no world is opened or generated.</summary>
    public async Task<SeedRegionResult> ResolveTintsAsync(SeedRegionResult snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _binding.EnsureUnchanged();
        if (_binding.Portable || snapshot.TintIndices.Length != 0 || snapshot.Blocks.Length == 0 || snapshot.Biomes.Length == 0) return snapshot;
        var result = await SeedTintResolver.ResolveAsync(snapshot, (query, ct) => SendAsync(new(null, "tints", Tints: query), SeedWorkerJsonContext.Default.SeedRegionTints, null, ct), cancellationToken).ConfigureAwait(false);
        _binding.EnsureUnchanged();
        return result;
    }

    /// <summary>Loads this runtime's original block models and textures for a completed snapshot; no new world generation or network access occurs.</summary>
    public async Task<SeedBlockScene> BuildSceneAsync(SeedRegionResult snapshot, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (snapshot.Fingerprint != Fingerprint) throw new ArgumentException("The block snapshot belongs to another original runtime.", nameof(snapshot));
        _binding.EnsureUnchanged();
        SeedValidation.Region(snapshot, new() { Profile = Profile, Seed = snapshot.Seed, World = snapshot.World, Bounds = snapshot.Bounds, IncludeBlocks = true });
        return await Task.Run(() =>
        {
            using var file = File.OpenRead(_binding.ClientJar);
            var scene = SeedModelResources.Load(snapshot, file, cancellationToken);
            _binding.EnsureUnchanged();
            return scene;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads up to 1024 original item PNGs for inventory slots, using a same-ID block texture when no flat item texture exists; absent images retain their named slots.</summary>
    public async Task<IReadOnlyDictionary<string, byte[]>> ReadItemImagesAsync(IEnumerable<string> itemIds, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(itemIds);
        _binding.EnsureUnchanged();
        var ids = itemIds.Distinct(StringComparer.Ordinal).Take(1024).ToArray();
        return await Task.Run(() =>
        {
            using var client = File.OpenRead(_binding.ClientJar);
            var result = SeedItemResources.Read(ids, client, cancellationToken);
            _binding.EnsureUnchanged();
            return (IReadOnlyDictionary<string, byte[]>)result;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs a bounded positive-witness scan; cooperative cancellation returns its retained results and restart cursor.</summary>
    public Task<SeedSearchResult> SearchAsync(SeedSearchQuery query, IProgress<SeedSearchResult>? progress = null, CancellationToken cancellationToken = default) =>
        SearchCoreAsync(query, progress is null ? null : result => { progress.Report(result); return Task.CompletedTask; }, cancellationToken);

    internal async Task<SeedSearchResult> SearchCoreAsync(SeedSearchQuery query, Func<SeedSearchResult, Task>? progress, CancellationToken ct)
    {
        SeedValidation.Search(query);
        SeedValidation.Profile(query.Profile, Profile);
        query = query with
        {
            Profile = Profile,
            World = query.World with { },
            Conditions = [.. query.Conditions.Select(condition => condition with { Targets = [.. condition.Targets] })],
        };
        query = query with { StartSeed = await NormalizeSeedAsync(query.StartSeed, ct).ConfigureAwait(false) };
        return await SendAsync(new(null, "search", Search: query), SeedJsonContext.Default.SeedSearchResult, progress, ct).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(SeedWorkerRequest request, JsonTypeInfo<T> metadata, Func<SeedSearchResult, Task>? progress, CancellationToken ct, Func<JsonElement, Task>? nativeProgress = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _requests.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_broken || _process.HasExited) throw new LauncherOperationException("SEED_WORKER_EXITED", "The original seed worker stopped. Reopen the selected version to retry.");
            _binding.EnsureUnchanged();
            request = request with { Id = Guid.NewGuid().ToString("N") };
            await WriteAsync(request).ConfigureAwait(false);
            var response = ReadResponseAsync(request.Id, metadata, progress, nativeProgress);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = ct.Register(() => cancelled.TrySetResult());
            if (await Task.WhenAny(response, cancelled.Task).ConfigureAwait(false) != response)
            {
                await WriteAsync(new(null, "cancel", CancelId: request.Id)).ConfigureAwait(false);
                // Drain the terminal reply after cancellation so the next request retains NDJSON framing.
                var value = await response.WaitAsync(TimeSpan.FromSeconds(8), CancellationToken.None).ConfigureAwait(false);
                if (request.Operation != "search") throw new SeedCalculationCancelledException();
                return value;
            }

            return await response.ConfigureAwait(false);
        }
        catch (SeedCalculationCancelledException exception)
        {
            // The native terminal response was consumed, so the next tile can reuse this warmed JVM.
            throw new OperationCanceledException("The original seed calculation was cancelled.", exception, ct);
        }
        catch (LauncherOperationException exception) when (exception.Code == "SEED_CALCULATION_FAILED")
        {
            // A complete native error response does not corrupt the transport or require another JVM startup.
            throw;
        }
        catch
        {
            _broken = true;
            Kill();
            ct.ThrowIfCancellationRequested();
            throw;
        }
        finally { _requests.Release(); }
    }

    private async Task<T> ReadResponseAsync<T>(string id, JsonTypeInfo<T> metadata, Func<SeedSearchResult, Task>? progress, Func<JsonElement, Task>? nativeProgress)
    {
        while (true)
        {
            var envelope = await ReadEnvelopeAsync().ConfigureAwait(false);
            if (envelope.Id != id) throw new InvalidDataException("The original seed worker returned a stale request identity.");
            if (envelope.Kind == "progress")
            {
                if (nativeProgress is not null) await nativeProgress(envelope.Data).ConfigureAwait(false);
                if (progress is not null)
                {
                    var snapshot = envelope.Data.Deserialize(SeedJsonContext.Default.SeedSearchResult)
                        ?? throw new InvalidDataException("The original seed progress snapshot is missing.");
                    await progress(snapshot).ConfigureAwait(false);
                }

                continue;
            }

            if (envelope.Kind == "cancelled") throw new SeedCalculationCancelledException();
            if (envelope.Kind == "world_unsupported") throw new LauncherOperationException("SEED_WORLD_UNSUPPORTED", envelope.Error ?? "The selected original game does not provide this world preset.");
            if (envelope.Kind == "error") throw new LauncherOperationException("SEED_CALCULATION_FAILED", envelope.Error ?? "The original seed calculation failed.");
            if (envelope.Kind != "result") throw new InvalidDataException("Unknown original seed worker message.");
            return envelope.Data.Deserialize(metadata) ?? throw new InvalidDataException("The original seed result is missing.");
        }
    }

    private async Task WriteAsync(SeedWorkerRequest request)
    {
        await _writes.WaitAsync().ConfigureAwait(false);
        try
        {
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, SeedWorkerJsonContext.Default.SeedWorkerRequest)).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        finally { _writes.Release(); }
    }

    private async Task<SeedWorkerEnvelope> ReadEnvelopeAsync()
    {
        var line = await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
        if (line is null)
        {
            string diagnostic;
            lock (_errors) diagnostic = _errors.ToString();
            throw new LauncherOperationException("SEED_WORKER_EXITED", "The original seed worker closed its output. " + diagnostic);
        }

        if (line.Length > 32 * 1024 * 1024) throw new InvalidDataException("The original seed worker response exceeds 32 MiB.");
        return JsonSerializer.Deserialize(line, SeedWorkerJsonContext.Default.SeedWorkerEnvelope)
            ?? throw new InvalidDataException("The original seed worker returned an empty message.");
    }

    private async Task DrainErrorsAsync()
    {
        try
        {
            var buffer = new char[2048];
            int count;
            while ((count = await _process.StandardError.ReadAsync(buffer).ConfigureAwait(false)) != 0)
            {
                lock (_errors)
                {
                    _errors.Append(buffer, 0, count);
                    if (_errors.Length > 8192) _errors.Remove(0, _errors.Length - 8192);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException) { }
    }

    private void Kill()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    /// <summary>Stops only this session's Java child, drains its streams and removes its isolated template workspace.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            _process.StandardInput.Close();
            try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (TimeoutException) { Kill(); await _process.WaitForExitAsync().ConfigureAwait(false); }

            await _stderr.ConfigureAwait(false);
            await _requests.WaitAsync().ConfigureAwait(false);
        }
        finally
        {
            _process.Dispose();
            _requests.Dispose();
            _writes.Dispose();
            try { Directory.Delete(_directory, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}

internal sealed class SeedCalculationCancelledException : OperationCanceledException;