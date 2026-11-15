// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Worlds.Seeds;

/// <summary>Identifies an exported map and its lossless original-generation context.</summary>
/// <param name="Path">Absolute PNG destination.</param>
/// <param name="Sha256">Hash of the committed PNG bytes.</param>
/// <param name="Width">Exported sample width in pixels.</param>
/// <param name="Height">Exported sample height in pixels.</param>
public sealed record SeedMapExport(string Path, string Sha256, int Width, int Height);

/// <summary>Retains a reproducible search, its original runtime identity and the latest fully committed candidate cursor.</summary>
/// <param name="Id">Stable search identity.</param>
/// <param name="Query">Normalized original request, before advancing its restart cursor.</param>
/// <param name="Fingerprint">Exact original runtime owning the witnesses.</param>
/// <param name="Result">Aggregate positive results and committed cursor.</param>
/// <param name="ModifiedAt">Time of the latest atomic publication.</param>
public sealed record SeedSearchState(string Id, SeedSearchQuery Query, string Fingerprint, SeedSearchResult Result, DateTimeOffset ModifiedAt);

/// <summary>Stores a seed or a place together with enough original configuration to reopen the correct map.</summary>
public sealed record SeedBookmark
{
    /// <summary>Gets the saved instance/world owning this view, or null for a generated seed bookmark.</summary>
    public Maps.WorldMapSelection? SavedWorld { get; set; }
    /// <summary>Gets the generated persistent bookmark identity.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Gets seed or place, corresponding to the two bookmark segments.</summary>
    [JsonRequired]
    public string Kind { get; set; } = string.Empty;
    /// <summary>Gets the player-editable label.</summary>
    [JsonRequired]
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets the installed original version used to reopen this bookmark.</summary>
    [JsonRequired]
    public SeedRuntimeProfile Profile { get; set; } = null!;
    /// <summary>Gets the exact game/dependency fingerprint that originally produced this location.</summary>
    [JsonRequired]
    public string Fingerprint { get; set; } = string.Empty;
    /// <summary>Gets the normalized signed 64-bit seed string.</summary>
    [JsonRequired]
    public string Seed { get; set; } = string.Empty;
    /// <summary>Gets original dimension and world preset settings.</summary>
    public SeedWorldSettings World { get; set; } = new();
    /// <summary>Gets the explicit biome slice, or null for surface.</summary>
    public int? SliceY { get; set; }
    /// <summary>Gets the saved map center or place X.</summary>
    public int X { get; set; }
    /// <summary>Gets the saved map center or place Z.</summary>
    public int Z { get; set; }
    /// <summary>Gets the player's chosen teleport height; no safe landing is implied.</summary>
    public int TeleportY { get; set; } = 80;
    /// <summary>Gets the saved world blocks per displayed map pixel.</summary>
    public double BlocksPerPixel { get; set; } = 8;
    /// <summary>Gets the original positive witness for a bookmarked place.</summary>
    public SeedPosition? Position { get; set; }
    /// <summary>Gets the latest successful publication time.</summary>
    public DateTimeOffset ModifiedAt { get; set; }
}

/// <summary>Publishes exploration bookmarks with cross-process serialization and recoverable atomic replacement.</summary>
public sealed class SeedBookmarkStore(LauncherPaths paths)
{
    private string FilePath => Path.Combine(paths.Root, "seed-explorer", "bookmarks.json");

    /// <summary>Reads both bookmark types; damaged data is reported rather than silently replaced with an empty list.</summary>
    public SeedBookmark[] List() => File.Exists(FilePath)
        ? JsonFiles.ReadSnapshot(FilePath, stream => JsonSerializer.Deserialize(stream, SeedStoreJsonContext.Default.SeedBookmarkArray)
            ?? throw new InvalidDataException("The seed bookmark collection is empty or damaged.")) : [];

    /// <summary>Adds or updates an exact seed/place bookmark and returns its stable identity; duplicate locations reuse their existing record.</summary>
    public async Task<SeedBookmark> SaveAsync(SeedBookmark bookmark, CancellationToken cancellationToken = default)
    {
        SeedValidation.World(bookmark.World);
        if (bookmark.Kind is not ("seed" or "place") || string.IsNullOrWhiteSpace(bookmark.Name) || bookmark.Name.Length > 160
            || (bookmark.SavedWorld is null && !long.TryParse(bookmark.Seed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
            || (bookmark.SavedWorld is { } saved && (string.IsNullOrWhiteSpace(saved.InstanceId) || string.IsNullOrWhiteSpace(saved.World)))
            || Math.Abs((long)bookmark.X) > 30_000_000 || Math.Abs((long)bookmark.Z) > 30_000_000
            || !double.IsFinite(bookmark.BlocksPerPixel) || bookmark.BlocksPerPixel is < SeedValidation.MinimumViewportScale or > SeedValidation.MaximumViewportScale
            || string.IsNullOrWhiteSpace(bookmark.Fingerprint))
        {
            throw new ArgumentException("Invalid seed/place bookmark.");
        }

        using var lease = await ResourceLease.AcquireAsync(resource: FilePath, cancellationToken: cancellationToken).ConfigureAwait(false);
        var records = List().ToList();
        var index = records.FindIndex(record => bookmark.Id.Length > 0 ? record.Id == bookmark.Id
            : record.Kind == bookmark.Kind && record.SavedWorld == bookmark.SavedWorld
                && (bookmark.SavedWorld is not null || (record.Fingerprint == bookmark.Fingerprint && record.Seed == bookmark.Seed))
                && record.World == bookmark.World && record.SliceY == bookmark.SliceY
                && (bookmark.Kind == "seed" || (record.X == bookmark.X && record.Z == bookmark.Z && record.Position?.Id == bookmark.Position?.Id)));
        if (bookmark.Id.Length > 0 && index < 0) throw new FileNotFoundException("The bookmark to update no longer exists.");
        bookmark = bookmark with { Id = index >= 0 ? records[index].Id : Guid.NewGuid().ToString("N"), Name = bookmark.Name.Trim(), ModifiedAt = DateTimeOffset.UtcNow };
        if (index >= 0) records[index] = bookmark; else records.Add(bookmark);
        await WriteAsync([.. records], cancellationToken).ConfigureAwait(false);
        return bookmark;
    }

    /// <summary>Removes only the named bookmark; unrelated seed/place records retain their identity and values.</summary>
    public async Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: FilePath, cancellationToken: cancellationToken).ConfigureAwait(false);
        var records = List();
        var remaining = records.Where(record => record.Id != id).ToArray();
        if (remaining.Length != records.Length) await WriteAsync(remaining, cancellationToken).ConfigureAwait(false);
    }

    private Task WriteAsync(SeedBookmark[] records, CancellationToken ct) => JsonFiles.WriteBytesAsync(FilePath,
        JsonSerializer.SerializeToUtf8Bytes(records, SeedStoreJsonContext.Default.SeedBookmarkArray), ct);
}

/// <summary>Retains resumable positive-witness searches without relying on a window's process or memory.</summary>
public sealed class SeedSearchStore(LauncherPaths paths)
{
    private string StatePath(string id) => Guid.TryParseExact(id, "N", out _) ? Path.Combine(paths.Root, "seed-explorer", "searches", id + ".json")
        : throw new ArgumentException("A seed search ID must be a generated identity.", nameof(id));

    /// <summary>Reads a retained query, results and cursor; missing/damaged records are not replaced.</summary>
    public SeedSearchState Read(string id) => JsonFiles.ReadSnapshot(StatePath(id), stream =>
        JsonSerializer.Deserialize(stream, SeedStoreJsonContext.Default.SeedSearchState) ?? throw new InvalidDataException("The retained seed search is damaged."));

    /// <summary>Lists retained search snapshots, most recently published first.</summary>
    public SeedSearchState[] List()
    {
        var directory = Path.Combine(paths.Root, "seed-explorer", "searches");
        return Directory.Exists(directory) ? [.. Directory.EnumerateFiles(directory, "*.json")
            .Select(path => Read(Path.GetFileNameWithoutExtension(path))).OrderByDescending(state => state.ModifiedAt)] : [];
    }

    /// <summary>Creates a durable search identity before execution, so queued/background attempts can resume the same request.</summary>
    public async Task<SeedSearchState> CreateAsync(SeedSearchQuery query, CancellationToken cancellationToken = default)
    {
        SeedValidation.Search(query);
        var state = new SeedSearchState(Guid.NewGuid().ToString("N"), query, string.Empty,
            new() { Status = "queued", NextOffset = query.Offset }, DateTimeOffset.UtcNow);
        await SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return state;
    }

    /// <summary>Executes or resumes one retained search, committing only fully examined candidates and retaining positives on cancellation.</summary>
    public async Task<SeedSearchState> RunAsync(string id, IProgress<SeedSearchState>? progress = null, CancellationToken cancellationToken = default)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: StatePath(id), cancellationToken: cancellationToken).ConfigureAwait(false);
        var state = Read(id);
        if (state.Result.Hits.Length >= state.Query.MaxResults) return state;
        var session = await new SeedExplorerService(paths).OpenAsync(state.Query.Profile, cancellationToken).ConfigureAwait(false);
        await using var sessionDisposal = session.ConfigureAwait(false);
        if (state.Fingerprint.Length > 0 && state.Fingerprint != session.Fingerprint)
            throw new InvalidDataException("This retained search belongs to different game files. Create a new search for the updated version.");
        var normalized = await session.NormalizeSeedAsync(state.Query.StartSeed, cancellationToken).ConfigureAwait(false);
        var previous = state.Result;
        state = state with { Fingerprint = session.Fingerprint, Query = state.Query with { StartSeed = normalized, Profile = session.Profile } };
        await SaveAsync(state, cancellationToken).ConfigureAwait(false);
        var query = state.Query with { Offset = previous.NextOffset, MaxResults = state.Query.MaxResults - previous.Hits.Length };
        async Task Publish(SeedSearchResult result)
        {
            state = state with
            {
                Result = result with
                {
                    Checked = previous.Checked + result.Checked,
                    Hits = [.. previous.Hits.Concat(result.Hits).DistinctBy(hit => hit.Seed)],
                    ElapsedMilliseconds = previous.ElapsedMilliseconds + result.ElapsedMilliseconds,
                },
                ModifiedAt = DateTimeOffset.UtcNow,
            };
            // Cancellation preserves the last complete native snapshot rather than cancelling its atomic publication.
            await SaveAsync(state, CancellationToken.None).ConfigureAwait(false);
            progress?.Report(state);
        }

        var final = await session.SearchCoreAsync(query, Publish, cancellationToken).ConfigureAwait(false);
        await Publish(final).ConfigureAwait(false);
        return state;
    }

    private Task SaveAsync(SeedSearchState state, CancellationToken ct) => JsonFiles.WriteBytesAsync(StatePath(state.Id),
        JsonSerializer.SerializeToUtf8Bytes(state, SeedStoreJsonContext.Default.SeedSearchState), ct);
}