// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.GameBridge;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Reads selected saves and player provenance, optionally previewing missing terrain in an isolated original runtime without changing the player world.</summary>
public sealed class WorldMapService(LauncherPaths paths)
{
    /// <summary>Predicts an explicitly selected saved table with its recorded seed in an isolated original context, keeping actual saved contents unchanged.</summary>
    public Task<WorldContainerPrediction> PredictContainerAsync(WorldMapSnapshot snapshot, string dimension, SeedContainer recorded, CancellationToken cancellationToken = default) =>
        WorldContainerPredictor.ReadAsync(paths, snapshot, dimension, recorded, cancellationToken);

    /// <summary>Reads one selected saved container and explicitly predicts its recorded table in an independent original context, without changing its saved contents.</summary>
    public async Task<WorldContainerPrediction> PredictContainerAsync(WorldContainerQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var saved = await OpenAsync(query.Selection, cancellationToken).ConfigureAwait(false);
        var region = await saved.RegionAsync(new()
        {
            Selection = query.Selection,
            Dimension = query.Dimension,
            Bounds = new() { MinX = query.X, MaxX = query.X, MinY = query.Y, MaxY = query.Y, MinZ = query.Z, MaxZ = query.Z }
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
        var recorded = region.Containers.SingleOrDefault(container => container.X == query.X && container.Y == query.Y && container.Z == query.Z)
            ?? throw new InvalidDataException("The selected saved coordinate contains no readable container.");
        return await PredictContainerAsync(saved.Snapshot, query.Dimension, recorded, cancellationToken).ConfigureAwait(false);
    }

    private static readonly string[] DimensionContentFolders = ["region", "entities", "poi", "data"];
    /// <summary>Reads actual saved voxels and builds original block models and triangle geometry for a CLI inspection without modifying the save.</summary>
    public async Task<WorldSceneInspection> InspectSceneAsync(WorldRegionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var session = await OpenAsync(query.Selection, cancellationToken).ConfigureAwait(false);
        var data = await session.RegionAsync(query with { IncludeBlocks = true }, cancellationToken: cancellationToken).ConfigureAwait(false);
        var scene = await session.BuildSceneAsync(data, cancellationToken).ConfigureAwait(false);
        var mesh = await Task.Run(() => scene.CreateMesh(data.Bounds.MinY, data.Bounds.MaxY, cancellationToken), cancellationToken).ConfigureAwait(false);
        return new(data.Fingerprint, data.Bounds, data.UnknownBlocks, mesh.Blocks, mesh.Vertices.Length / 6, [.. scene.UnavailableModels]);
    }

    /// <summary>Reads one actual saved block/inventory region through an owned read session; no game, generation or world write occurs.</summary>
    public async Task<SeedRegionResult> ReadRegionAsync(WorldRegionQuery query, IProgress<SeedRegionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var session = await OpenAsync(query.Selection, cancellationToken).ConfigureAwait(false);
        return await session.RegionAsync(query, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Finds saved structure records through an owned frozen session without using original seed generation.</summary>
    public async Task<SeedStructureResult> FindStructuresAsync(WorldStructureQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var session = await OpenAsync(query.Selection, cancellationToken).ConfigureAwait(false);
        return await session.FindStructuresAsync(query, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Lists save folders and early finite-world files without opening them in a game or changing their format.</summary>
    public async Task<WorldSummary[]> ListAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        var result = (await new WorldService(paths).ListAsync(instanceId, cancellationToken).ConfigureAwait(false)).ToList();
        var instance = new InstanceStore(paths).Get(instanceId);
        var saves = Path.Combine(instance.GameDirectory, "saves");
        var files = Directory.Exists(saves) ? Directory.EnumerateFiles(saves).Where(file => Path.GetExtension(file).ToLowerInvariant() is ".mclevel" or ".mine" or ".dat").ToList() : [];
        var classic = Path.Combine(instance.GameDirectory, "level.dat");
        if (File.Exists(classic) && !files.Any(file => Path.GetFileName(file) == "level.dat")) files.Add(classic);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(new() { Folder = Path.GetFileName(file), Name = Path.GetFileNameWithoutExtension(file), Directory = file });
        }

        return [.. result];
    }

    /// <summary>Reads one save's seed, generator settings, dimensions and saved player positions.</summary>
    public async Task<WorldMapSnapshot> DescribeAsync(WorldMapSelection selection, CancellationToken cancellationToken = default)
    {
        using var session = await OpenAsync(selection, cancellationToken).ConfigureAwait(false);
        return session.Snapshot;
    }

    /// <summary>Opens a retained read session with installed English labels; source-file changes revoke affected reads until reopened.</summary>
    /// <param name="selection">Registered instance and exact saved world to read.</param>
    /// <param name="cancellationToken">Cancels opening and installed-resource reads.</param>
    public Task<WorldMapSession> OpenAsync(WorldMapSelection selection, CancellationToken cancellationToken = default) => OpenAsync(selection, "en_us", cancellationToken);

    /// <summary>Opens a retained read session with the requested registry language; dispose it after all saved-data reads finish.</summary>
    /// <param name="selection">Registered instance and exact saved world to read.</param>
    /// <param name="cancellationToken">Cancels opening and installed-resource reads.</param>
    /// <param name="language">Minecraft asset language for complete registry legends; unavailable translations retain installed English labels.</param>
    public async Task<WorldMapSession> OpenAsync(WorldMapSelection selection, string language, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentException.ThrowIfNullOrWhiteSpace(selection.InstanceId);
        var instance = new InstanceStore(paths).Get(selection.InstanceId);
        if (string.IsNullOrWhiteSpace(selection.World) || Path.GetFileName(selection.World) != selection.World
            || selection.World.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || selection.World.TrimEnd(' ', '.') != selection.World)
        {
            throw new ArgumentException("Select a single valid world name.", nameof(selection));
        }

        var finite = Path.Combine(instance.GameDirectory, "saves", selection.World);
        if (!File.Exists(finite) && selection.World == "level.dat") finite = Path.Combine(instance.GameDirectory, "level.dat");
        if (File.Exists(finite)) return await OpenFiniteAsync(selection, instance, finite, cancellationToken).ConfigureAwait(false);
        var directory = new WorldService(paths).GetDirectory(selection.InstanceId, selection.World);
        var warnings = new List<string>();
        var files = new Dictionary<string, WorldFileStamp>(StringComparer.OrdinalIgnoreCase);
        var file = Path.Combine(directory, "level.dat");
        Dictionary<string, object> root;
        try { root = await ReadAsync(file).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            var backup = Path.Combine(directory, "level.dat_old");
            if (!File.Exists(backup)) throw;
            root = await ReadAsync(backup).ConfigureAwait(false);
            file = backup;
            warnings.Add("WORLD_METADATA_BACKUP: The primary metadata could not be read; the saved backup metadata is being used.");
        }

        var data = root.GetValueOrDefault("Data") as Dictionary<string, object> ?? root;
        var generation = data.Compound("WorldGenSettings");
        var generationFile = Path.Combine(directory, "data", "minecraft", "world_gen_settings.dat");
        if (File.Exists(generationFile))
        {
            try
            {
                var saved = await ReadAsync(generationFile).ConfigureAwait(false);
                generation = saved.GetValueOrDefault("data") as Dictionary<string, object>
                    ?? saved.GetValueOrDefault("Data") as Dictionary<string, object> ?? saved;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            { warnings.Add("WORLD_GENERATION_READ: " + error.Message); }
        }

        var seed = generation.Integer("seed") ?? data.Integer("RandomSeed");
        var profile = ReadRuntimeProfile(instance, warnings);
        var version = data.Compound("Version").Text("Name");
        string? clientJar = null;
        System.Text.Json.Nodes.JsonObject? runtimeMetadata = null;
        if (instance.VersionId is not null)
        {
            try
            {
                var metadata = await new VersionResolver().ResolveAsync(instance.MinecraftDirectory, instance.VersionId, cancellationToken).ConfigureAwait(false);
                runtimeMetadata = metadata;
                version ??= metadata["_minecraftVersion"]?.GetValue<string>() ?? instance.VersionId;
                var jarId = metadata["_clientJarVersion"]?.GetValue<string>() ?? instance.VersionId;
                clientJar = SharedRuntimeLayout.Resolve(instance.MinecraftDirectory, "versions/" + jarId + "/" + jarId + ".jar");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
            { warnings.Add("WORLD_RUNTIME_METADATA: " + error.Message); }
        }

        var dimensions = DiscoverDimensions(directory, generation, cancellationToken);

        var players = new List<SavedWorldPlayer>();
        var localPlayer = ReadUuid(data.GetValueOrDefault("singleplayer_uuid")) ?? ReadUuid(data.Compound("Player").GetValueOrDefault("UUID"));
        if (ReadPlayer(data.Compound("Player"), "local", files[file].Modified) is { } local) players.Add(local);
        foreach (var folder in new[] { "playerdata", "players", Path.Combine("players", "data") })
        {
            var playerDirectory = Path.Combine(directory, folder);
            if (!Directory.Exists(playerDirectory)) continue;
            foreach (var playerFile in Directory.EnumerateFiles(playerDirectory, "*.dat").Take(1024))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var player = await ReadAsync(playerFile).ConfigureAwait(false);
                    if (ReadPlayer(player, Path.GetFileNameWithoutExtension(playerFile), files[playerFile].Modified) is { } value)
                        players.Add(value.Id.Equals(localPlayer, StringComparison.OrdinalIgnoreCase) ? value with { Id = "local" } : value);
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
                { if (warnings.Count < 16) warnings.Add("WORLD_PLAYER_READ: " + Path.GetFileName(playerFile) + ": " + error.Message); }
            }
        }

        foreach (var dimension in players.Select(player => player.Dimension).Distinct(StringComparer.Ordinal))
        {
            if (dimensions.ContainsKey(dimension)) continue;
            if (dimension is "the_nether" or "the_end") dimensions[dimension] = Path.Combine(directory, dimension == "the_nether" ? "DIM-1" : "DIM1");
            else if (StoredDimensionDirectory(directory, dimension) is { } custom) dimensions[dimension] = custom;
        }

        foreach (var path in dimensions.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var region = Path.Combine(path, "region");
            if (!Directory.Exists(region)) continue;
            foreach (var candidate in Directory.EnumerateFiles(region))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Path.GetExtension(candidate) is ".mca" or ".mcr" or ".mcc") files[candidate] = WorldFileStamp.Read(candidate);
            }
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(selection.InstanceId + "/" + selection.World));
        foreach (var stamp in files.Values.OrderBy(stamp => stamp.Path, StringComparer.OrdinalIgnoreCase))
            hash.AppendData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{stamp.Path}\0{stamp.Length}\0{stamp.Modified.Ticks}\n")));
        var (Settings, Reason) = WorldPreviewSettings.Read(data, generation, directory);
        var snapshot = new WorldMapSnapshot
        {
            Selection = selection,
            Name = data.Text("LevelName") ?? selection.World,
            Version = version ?? instance.VersionId ?? "unknown",
            Profile = profile,
            Seed = seed?.ToString(CultureInfo.InvariantCulture),
            Settings = Reason is null ? Settings : new() { Preset = Preset(data.Text("generatorName"), generation) },
            PreviewSupported = seed is not null && Reason is null,
            PreviewUnavailableReason = Reason ?? (seed is null ? "The save does not record an original world seed." : null),
            Spawn = ReadSpawn(data),
            Dimensions = [.. dimensions.Keys],
            Players = [.. players.GroupBy(player => player.Id, StringComparer.OrdinalIgnoreCase).Select(group => group.MaxBy(player => player.SavedAt)!)],
            SavedAt = new(files[file].Modified, TimeSpan.Zero),
            Revision = Convert.ToHexStringLower(hash.GetHashAndReset()),
            Warnings = [.. warnings],
        };
        var catalog = WorldMapCatalog.Read(clientJar, snapshot);
        if (clientJar is not null && File.Exists(clientJar) && runtimeMetadata is not null)
        {
            try { catalog.Names = await SeedGameLanguage.ReadAsync(instance.MinecraftDirectory, clientJar, runtimeMetadata, language, cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { System.Diagnostics.Trace.TraceWarning("Saved map language resources could not be read: {0}", error.Message); }
        }

        return new(snapshot, directory, dimensions, files, clientJar, catalog: catalog, resolveDefaults: async (region, ct) =>
        {
            if (!region.Palette.Any(state => state.DefaultsOmitted) && (region.TintIndices.Length != 0 || region.Biomes.Length == 0)) return region;
            try
            {
                var native = await new SeedExplorerService(paths).OpenAsync(snapshot.Profile, ct).ConfigureAwait(false);
                await using var disposal = native.ConfigureAwait(false);
                var resolved = region;
                if (region.Palette.Any(state => state.DefaultsOmitted))
                {
                    var original = await native.GetCatalogAsync(cancellationToken: ct).ConfigureAwait(false);
                    resolved = region with
                    {
                        Palette = [.. region.Palette.Select(state =>
                {
                    if (!state.DefaultsOmitted || !original.BlockDefaults.TryGetValue(state.Id, out var defaults)) return state;
                    var properties = new Dictionary<string, string>(defaults, StringComparer.Ordinal);
                    foreach (var (key, value) in state.Properties) properties[key] = value;
                    return state with { Properties = properties, DefaultsOmitted = false };
                })]
                    };
                }

                return await native.ResolveTintsAsync(resolved, ct).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not WorldSnapshotChangedException and (Application.LauncherOperationException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception))
            {
                System.Diagnostics.Trace.TraceWarning("Native saved-state defaults or colors were unavailable: {0}", error.Message);
                return region with { Warnings = [.. region.Warnings, "WORLD_SCENE_TINT_UNAVAILABLE: " + error.Message] };
            }
        });

        async Task<Dictionary<string, object>> ReadAsync(string source)
        {
            var stamp = WorldFileStamp.Read(source);
            var document = await WorldNbt.ReadFileAsync(source, cancellationToken).ConfigureAwait(false);
            stamp.EnsureUnchanged();
            files[source] = stamp;
            return document;
        }
    }

    internal static string? StoredDimensionDirectory(string directory, string id)
    {
        var parts = id.Split(':', 2);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0
            || parts[0] is "." or ".." || parts[1].Split('/').Any(part => part is "" or "." or "..")
            || !parts[0].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-')
            || !parts[1].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' or '/'))
        {
            return null;
        }

        return Path.Combine(directory, "dimensions", parts[0], parts[1].Replace('/', Path.DirectorySeparatorChar));
    }

    private static Dictionary<string, string> DiscoverDimensions(string directory, Dictionary<string, object> generation, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal) { ["overworld"] = directory };
        foreach (var (id, folder) in new[] { ("the_nether", "DIM-1"), ("the_end", "DIM1") })
            if (Directory.Exists(Path.Combine(directory, folder))) result[id] = Path.Combine(directory, folder);
        foreach (var key in generation.Compound("dimensions").Keys)
        {
            var id = Dimension(key);
            var namespaced = StoredDimensionDirectory(directory, key);
            if (namespaced is not null && Directory.Exists(namespaced)) result[id] = namespaced;
            else if (id is "overworld" or "the_nether" or "the_end")
                result[id] = id == "overworld" ? directory : Path.Combine(directory, id == "the_nether" ? "DIM-1" : "DIM1");
            else if (namespaced is not null) result[id] = namespaced;
        }

        var root = Path.Combine(directory, "dimensions");
        if (!Directory.Exists(root)) return result;
        foreach (var space in Directory.EnumerateDirectories(root))
        {
            var pending = new Stack<(string Path, int Depth)>();
            foreach (var child in Directory.EnumerateDirectories(space)) pending.Push((child, 1));
            while (pending.TryPop(out var item))
            {
                ct.ThrowIfCancellationRequested();
                if ((File.GetAttributes(item.Path) & FileAttributes.ReparsePoint) != 0) continue;
                if (DimensionContentFolders.Any(folder => Directory.Exists(Path.Combine(item.Path, folder))))
                {
                    var id = Path.GetFileName(space) + ":" + Path.GetRelativePath(space, item.Path).Replace(Path.DirectorySeparatorChar, '/');
                    if (StoredDimensionDirectory(directory, id) is not null) result[Dimension(id)] = item.Path;
                }
                else if (item.Depth < 16)
                {
                    foreach (var child in Directory.EnumerateDirectories(item.Path)) pending.Push((child, item.Depth + 1));
                }
            }
        }

        return result;
    }

    private static string? ReadUuid(object? value) => value is int[] { Length: 4 } uuid
        ? Guid.ParseExact(string.Concat(uuid.Select(part => unchecked((uint)part).ToString("x8", CultureInfo.InvariantCulture))), "N").ToString()
        : value is string text && Guid.TryParse(text, out var parsed) ? parsed.ToString() : null;

    private async Task<WorldMapSession> OpenFiniteAsync(WorldMapSelection selection, InstanceRecord instance, string file, CancellationToken ct)
    {
        var stamp = WorldFileStamp.Read(file);
        var finite = await FiniteWorld.ReadAsync(file, ct).ConfigureAwait(false);
        stamp.EnsureUnchanged();
        var warnings = new List<string>();
        var revision = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{file}\0{stamp.Length}\0{stamp.Modified.Ticks}"))));
        var snapshot = new WorldMapSnapshot
        {
            Selection = selection,
            Name = finite.Name ?? Path.GetFileNameWithoutExtension(file),
            Version = instance.VersionId ?? "unknown",
            Profile = ReadRuntimeProfile(instance, warnings),
            Spawn = finite.Spawn,
            Dimensions = ["overworld"],
            SavedAt = new(stamp.Modified, TimeSpan.Zero),
            Revision = revision,
            Warnings = [.. warnings],
        };
        return new(snapshot, Path.GetDirectoryName(file)!, new() { ["overworld"] = file }, new(StringComparer.OrdinalIgnoreCase) { [file] = stamp }, null, finite);
    }

    private SeedRuntimeProfile ReadRuntimeProfile(InstanceRecord instance, List<string> warnings)
    {
        try { return SeedRuntimeProfiles.FromInstance(paths, instance); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            // Java is optional for saved pixels; a broken preview preference cannot hide readable player data.
            warnings.Add("WORLD_RUNTIME_SETTINGS: " + error.Message);
            return new() { MinecraftDirectory = instance.MinecraftDirectory, VersionId = instance.VersionId ?? string.Empty, JavaPath = instance.JavaPath };
        }
    }

    /// <summary>Reads a bounded saved tile and optionally completes unexplored terrain in an owned original session; interactive callers should retain their sessions.</summary>
    public async Task<SeedMapTile> ReadMapAsync(WorldMapQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var session = await OpenAsync(query.Selection, cancellationToken).ConfigureAwait(false);
        var saved = await session.MapAsync(query, cancellationToken).ConfigureAwait(false);
        if (!query.FillUnexplored || !saved.PreviewEligible.Any(value => value)) return saved;
        if (!session.Snapshot.PreviewSupported || session.Snapshot.Seed is null)
            return await session.MapWithPreviewAsync(query, null, cancellationToken).ConfigureAwait(false);
        try
        {
            var preview = await new SeedExplorerService(paths).OpenAsync(session.Snapshot.Profile, cancellationToken).ConfigureAwait(false);
            await using var disposal = preview.ConfigureAwait(false);
            return await session.MapWithPreviewAsync(query, preview, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not WorldSnapshotChangedException and (IOException or InvalidDataException or UnauthorizedAccessException
            or Application.LauncherOperationException or InvalidOperationException or ArgumentException or TimeoutException))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await session.HasChangedAsync(cancellationToken).ConfigureAwait(false)) throw new WorldSnapshotChangedException();
            return saved with { Warnings = [.. saved.Warnings, "WORLD_PREVIEW_UNAVAILABLE: " + error.Message] };
        }
    }

    /// <summary>Observes the selected save's player using exact live directory evidence, falling back only to explicitly saved positions.</summary>
    public async Task<WorldPlayerObservation> ObservePlayerAsync(WorldMapSelection selection, string? playerId = null, CancellationToken cancellationToken = default)
    {
        var snapshot = await DescribeAsync(selection, cancellationToken).ConfigureAwait(false);
        return await ObservePlayerAsync(snapshot, playerId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Observes a retained snapshot's player without rescanning region files on each live update.</summary>
    public async Task<WorldPlayerObservation> ObservePlayerAsync(WorldMapSnapshot snapshot, string? playerId = null, CancellationToken cancellationToken = default)
    {
        var selection = snapshot.Selection;
        var saved = playerId is null ? snapshot.Players.FirstOrDefault(player => player.Id == "local") ?? snapshot.Players.MaxBy(player => player.SavedAt)
            : snapshot.Players.FirstOrDefault(player => player.Id == playerId);
        var session = new SessionService(paths).List().Where(session => session.InstanceId == selection.InstanceId && session.Status == "running").MaxBy(session => session.CreatedAt);
        if (session?.Id is not { } sessionId) return new(selection, saved is null ? "unavailable" : "saved", saved);
        var bridge = new GameBridgeService(paths);
        try
        {
            var status = await bridge.GetStatusAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (status.State != "connected") return new(selection, status.State == "disabled" ? "disabled" : "disconnected", saved, sessionId);
            var observed = await bridge.SnapshotAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (!observed.Ready || observed.Mode != "local_owned" || observed.WorldEpoch is null || observed.Dimension is null)
                return new(selection, "other_world", null, sessionId);
            if (playerId is not null && playerId != "local" && !string.Equals(playerId, observed.PlayerId, StringComparison.OrdinalIgnoreCase))
                return new(selection, saved is null ? "unavailable" : "saved", saved, sessionId);
            var world = await bridge.QueryAsync(sessionId, new() { GameIncarnation = observed.GameIncarnation, WorldEpoch = observed.WorldEpoch, Dimension = observed.Dimension, Kind = "world" }, cancellationToken).ConfigureAwait(false);
            var directory = new WorldService(paths).GetDirectory(selection.InstanceId, selection.World);
            if (!world.Data.TryGetProperty("directory", out var stored) || stored.ValueKind != System.Text.Json.JsonValueKind.String)
                return new(selection, saved is null ? "unavailable" : "saved", saved, sessionId);
            if (!ResourceLease.CanonicalPath(stored.GetString()!).Equals(ResourceLease.CanonicalPath(directory), StringComparison.OrdinalIgnoreCase)
                || world.GameIncarnation != observed.GameIncarnation || world.WorldEpoch != observed.WorldEpoch)
            {
                return new(selection, "other_world", null, sessionId);
            }

            if (snapshot.Seed is { } expectedSeed && world.Data.TryGetProperty("seed", out var liveSeed)
                && liveSeed.ValueKind == System.Text.Json.JsonValueKind.String && liveSeed.GetString() != expectedSeed)
            {
                return new(selection, "other_world", null, sessionId);
            }

            if (observed.X is not { } x || observed.Y is not { } y || observed.Z is not { } z || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
                return new(selection, "unavailable", null, sessionId);
            return new(selection, "live", new(observed.PlayerId ?? "local", null, Dimension(observed.Dimension), x, y, z, observed.Yaw,
                DateTimeOffset.Parse(observed.SampledAt, CultureInfo.InvariantCulture)), sessionId, observed.LocalWorldEpoch);
        }
        catch (Exception error) when (error is IOException or Application.LauncherOperationException or InvalidOperationException)
        { return new(selection, "disconnected", saved, sessionId); }
    }

    internal static string Dimension(object? value) => value switch
    {
        string name when name.StartsWith("minecraft:", StringComparison.Ordinal) => name[10..],
        string name => name,
        _ => WorldNbt.Number(value) switch { -1 => "the_nether", 1 => "the_end", _ => "overworld" },
    };

    private static SavedWorldPlayer? ReadPlayer(Dictionary<string, object> data, string fallbackId, DateTime modified)
    {
        if (data.GetValueOrDefault("Pos") is not List<object> { Count: >= 3 } position
            || WorldNbt.Decimal(position[0]) is not { } x || WorldNbt.Decimal(position[1]) is not { } y || WorldNbt.Decimal(position[2]) is not { } z)
        {
            return null;
        }

        if (Math.Abs(x) > 30_000_000 || Math.Abs(z) > 30_000_000 || Math.Abs(y) > 1_000_000) return null;
        var id = fallbackId;
        if (fallbackId != "local") id = ReadUuid(data.GetValueOrDefault("UUID")) ?? fallbackId;
        var rotation = data.GetValueOrDefault("Rotation") as List<object>;
        var yaw = rotation is { Count: > 0 } ? WorldNbt.Decimal(rotation[0]) : null;
        return new(id, data.Text("Name"), Dimension(data.GetValueOrDefault("Dimension")), x, y, z, yaw is { } angle ? (float)angle : null, new(modified, TimeSpan.Zero));
    }

    private static SeedPosition? ReadSpawn(Dictionary<string, object> data)
    {
        if (data.Integer("SpawnX") is { } x && data.Integer("SpawnY") is { } y && data.Integer("SpawnZ") is { } z)
            return new() { Id = "spawn", Kind = "spawn", X = checked((int)x), Y = checked((int)y), Z = checked((int)z) };
        var spawn = data.Compound("spawn");
        return spawn.GetValueOrDefault("pos") is int[] { Length: 3 } pos && Dimension(spawn.GetValueOrDefault("dimension")) == "overworld"
            ? new() { Id = "spawn", Kind = "spawn", X = pos[0], Y = pos[1], Z = pos[2] } : null;
    }

    private static string Preset(string? legacy, Dictionary<string, object> generation)
    {
        var generator = generation.Compound("dimensions").Compound("minecraft:overworld").Compound("generator");
        if (generator.Text("type") == "minecraft:flat" || legacy?.Equals("flat", StringComparison.OrdinalIgnoreCase) == true) return "flat";
        if (legacy?.Equals("largeBiomes", StringComparison.OrdinalIgnoreCase) == true || generator.Text("settings") == "minecraft:large_biomes") return "large_biomes";
        if (legacy?.Equals("amplified", StringComparison.OrdinalIgnoreCase) == true || generator.Text("settings") == "minecraft:amplified") return "amplified";
        return "normal";
    }
}