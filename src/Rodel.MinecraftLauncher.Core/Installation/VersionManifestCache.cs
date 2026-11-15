// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Installation;

internal static class VersionManifestCache
{
    private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    internal static async Task<(JsonObject Manifest, bool Cached)> ReadAsync(LauncherPaths paths, HttpClient client, Uri source, bool offline, bool refresh, CancellationToken ct)
    {
        var name = source.AbsoluteUri == "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"
            ? "version_manifest_v2.json"
            : "version_manifest_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source.AbsoluteUri)))[..24] + ".json";
        var file = ResourceLease.CanonicalPath(Path.Combine(paths.CacheDirectory, name));
        var entry = Entries.GetOrAdd(file, _ => new Entry());
        var requested = DateTimeOffset.UtcNow;
        await entry.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ReadDisk(entry, file);
            if (offline)
                return (Clone(entry.Manifest ?? new JsonObject { ["versions"] = new JsonArray() }), true);
            if (Reusable(entry, requested, refresh))
                return (Clone(entry.Manifest!), true);
            using var lease = await ResourceLease.AcquireAsync(file, ct).ConfigureAwait(false);
            ReadDisk(entry, file);
            if (Reusable(entry, requested, refresh))
                return (Clone(entry.Manifest!), true);
            try
            {
                using var response = await DownloadRequests.GetBufferedAsync(client, source, NetworkSettings.MetadataTimeout(paths), ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var disposal = stream.ConfigureAwait(false);
                var manifest = await JsonNode.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false) as JsonObject;
                if (manifest?["versions"] is not JsonArray)
                    throw new InvalidDataException("Version manifest has no versions array.");
                ct.ThrowIfCancellationRequested();
                entry.Manifest = manifest;
                entry.FetchedAt = DateTimeOffset.UtcNow;
                entry.RetryAfter = DateTimeOffset.MinValue;
                try
                {
                    await JsonFiles.WriteAsync(file, manifest, ct).ConfigureAwait(false);
                    entry.DiskStamp = File.GetLastWriteTimeUtc(file);
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                {
                    // Optional persistence failure must not discard a valid downloaded catalog.
                }

                return (Clone(manifest), false);
            }
            catch (Exception failure) when (failure is HttpRequestException or IOException or JsonException or InvalidDataException
                || (failure is OperationCanceledException && !ct.IsCancellationRequested))
            {
                if (entry.Manifest is null)
                    throw;
                entry.RetryAfter = DateTimeOffset.UtcNow.AddMinutes(1);
                return (Clone(entry.Manifest), true);
            }
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private static bool Reusable(Entry entry, DateTimeOffset requested, bool refresh) => entry.Manifest is not null
        && (entry.FetchedAt >= requested || (!refresh && (requested - entry.FetchedAt < Lifetime || requested < entry.RetryAfter)));

    private static void ReadDisk(Entry entry, string file)
    {
        var stamp = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : (DateTime?)null;
        if (entry.DiskStamp == stamp)
            return;
        entry.DiskStamp = stamp;
        if (stamp is null)
        {
            entry.Manifest = null;
            return;
        }

        try
        {
            var manifest = JsonFiles.ReadObject(file);
            if (manifest["versions"] is not JsonArray)
                throw new InvalidDataException("Cached version manifest has no versions array.");
            entry.Manifest = manifest;
            entry.FetchedAt = new DateTimeOffset(stamp.Value, TimeSpan.Zero);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            entry.Manifest = null;
        }
    }

    private static JsonObject Clone(JsonObject manifest) => (JsonObject)manifest.DeepClone();

    private sealed class Entry
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal JsonObject? Manifest { get; set; }
        internal DateTime? DiskStamp { get; set; }
        internal DateTimeOffset FetchedAt { get; set; }
        internal DateTimeOffset RetryAfter { get; set; }
    }
}