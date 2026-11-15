// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Packs;

/// <summary>Selects and caches exact online pack artifacts independently from applying them to an instance.</summary>
/// <param name="paths">The launcher-owned metadata and artifact cache.</param>
/// <param name="content">Optional provider transport configuration shared with content queries.</param>
public sealed class OnlinePackService(LauncherPaths paths, ContentPlatformService? content = null)
{
    private readonly ContentPlatformService _content = content ?? new(paths);

    /// <summary>Lists real provider releases in descending publication order with the same channel policy used by selection.</summary>
    /// <param name="project">The Modrinth project identifier or slug.</param>
    /// <param name="channel">Release only, release plus beta, or alpha/all; null preserves unrestricted selection.</param>
    /// <param name="ct">Cancels the provider request.</param>
    /// <returns>The retained provider version objects, not an inferred compatible version list.</returns>
    public async Task<IReadOnlyList<OnlinePackVersion>> GetVersionsAsync(string project, string? channel = null, CancellationToken ct = default)
    {
        ContentPlatformService.ValidateChannel(channel);
        var versions = await _content.GetVersionsAsync("modrinth", project, ct: ct).ConfigureAwait(false);
        return [.. versions.OfType<JsonObject>().Select(version => new OnlinePackVersion(version))
            .Where(version => ContentPlatformService.AllowedChannel(version.VersionType, channel))
            .OrderByDescending(version => version.PublishedAt, StringComparer.Ordinal)];
    }

    /// <summary>Resolves the selected online pack version and metadata without equating selection with installation.</summary>
    public async Task<OnlinePackSelection> SelectAsync(string project, string? versionId = null, bool offline = false, bool persist = true, string? channel = null, CancellationToken ct = default)
    {
        ContentPlatformService.ValidateChannel(channel);
        static string Key(string projectId, string? selectedVersion, string? selectedChannel) =>
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(projectId + ":" + selectedVersion
                + (selectedVersion is null && selectedChannel is not null ? ":" + selectedChannel : string.Empty))));
        var key = Key(project, versionId, channel);
        var metadataPath = Path.Combine(paths.CacheDirectory, "online-packs", key + ".json");
        if (offline)
            return File.Exists(metadataPath) ? ApplySelectionPolicy(JsonFiles.ReadObject(metadataPath).Deserialize(PackJsonContext.Default.OnlinePackSelection)!, channel, versionId is not null) : throw new IOException("No cached pack selection. Select/download this version online first.");
        var info = await _content.GetProjectAsync("modrinth", project, ct).ConfigureAwait(false);
        if (info["project_type"]?.GetValue<string>() != "modpack")
            throw new ArgumentException("This project is not a Modrinth modpack. Use content install for individual content.");
        var versions = await GetVersionsAsync(project, versionId is null ? channel : null, ct).ConfigureAwait(false);
        var version = versions.FirstOrDefault(value => versionId is null || value.Id == versionId)
            ?? throw new ArgumentException("No matching pack version.");
        var file = version.SelectArchive();
        var id = version.Id;
        var result = new OnlinePackSelection
        {
            ProjectId = info["id"]!.GetValue<string>(),
            VersionId = id,
            VersionName = version.VersionName,
            VersionType = version.VersionTypeMetadata,
            Channel = channel,
            Name = info["title"]?.DeepClone(),
            File = file,
            GameVersions = version.GameVersions,
            Loaders = version.Loaders,
            ArchivePath = Path.Combine(paths.CacheDirectory, "online-packs", Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id))) + ".mrpack")
        };
        ApplySelectionPolicy(result, channel, versionId is not null);
        if (persist)
        {
            var document = JsonSerializer.SerializeToNode(result, PackJsonContext.Default.OnlinePackSelection)!;
            await JsonFiles.WriteAsync(metadataPath, document, ct).ConfigureAwait(false);
            foreach (var cacheVersion in versionId is null ? new string?[]
            {
                null,
                id
            }

            :
            [
                id
            ]

            )
            {
                var canonical = Key(result.ProjectId, cacheVersion, channel);
                await JsonFiles.WriteAsync(Path.Combine(paths.CacheDirectory, "online-packs", canonical + ".json"), document, ct).ConfigureAwait(false);
            }
        }

        return result;
    }

    private static OnlinePackSelection ApplySelectionPolicy(OnlinePackSelection selection, string? channel, bool explicitVersion)
    {
        selection.Channel = channel;
        selection.SelectionWarnings = null;
        if (explicitVersion && selection.VersionType?.ToString() is { } type && !ContentPlatformService.AllowedChannel(type, channel))
        {
            selection.SelectionWarnings = [new("explicit-version-channel-override", selection.VersionId, type, channel)];
        }

        return selection;
    }

    /// <summary>Downloads a selected pack artifact using its resolved provider metadata and reports actual transfer progress.</summary>
    public async Task<string> DownloadAsync(OnlinePackSelection selection, bool offline, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var file = selection.File;
        var path = selection.ArchivePath;
        var sha1 = file.Sha1;
        if (offline)
        {
            if (!File.Exists(path))
                throw new IOException("Pack archive is not cached.");
        }
        else
        {
            var client = NetworkSettings.Client(paths, TimeSpan.FromMinutes(5));
            await new DownloadService(paths, client).DownloadAsync(uri: new Uri(file.Url), destination: path, sha1: sha1, size: file.Size, progress: progress, cancellationToken: ct, sha512: file.Sha512).ConfigureAwait(false);
        }

        if (file.Size is { } expectedSize && new FileInfo(path).Length != expectedSize)
            throw new InvalidDataException("Cached pack size differs from the selected version. Retry online to repair the cache.");
        if (sha1 is not null)
        {
            var stream = File.OpenRead(path);
            await using var streamDisposal = stream.ConfigureAwait(false);
            if (!string.Equals(Convert.ToHexString(await SHA1.HashDataAsync(stream, ct).ConfigureAwait(false)), sha1, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Cached pack SHA1 differs from the selected version. Retry online to repair the cache.");
        }

        if (file.Sha512 is { } expected)
        {
            var stream = File.OpenRead(path);
            await using var streamDisposal = stream.ConfigureAwait(false);
            if (!string.Equals(Convert.ToHexString(await SHA512.HashDataAsync(stream, ct).ConfigureAwait(false)), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Cached pack SHA512 differs from the selected version.");
        }

        return path;
    }
}