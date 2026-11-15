// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Downloads;

internal static class DownloadOwnership
{
    /// <summary>Retains reclaimable-file evidence, reusing digests computed under the publishing download lease when available.</summary>
    public static async Task RecordAsync(LauncherPaths paths, string file, CancellationToken ct, FileDigests? verified = null)
    {
        try
        {
            var canonical = ResourceLease.CanonicalPath(file);
            if (!Within(canonical, paths.CacheDirectory) && !Within(canonical, paths.InstancesDirectory))
                return;
            var hashes = verified is { Sha256: not null } ? verified : await FileIntegrity.ReadAsync(file, false, true, false, ct).ConfigureAwait(false);
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToUpperInvariant())));
            await JsonFiles.WriteAsync(Path.Combine(paths.CacheDirectory, "download-ownership", id + ".json"), new JsonObject { ["path"] = canonical, ["sha256"] = hashes.Sha256, ["bytes"] = hashes.Size, ["publishedAt"] = DateTimeOffset.UtcNow.ToString("O") }, ct).ConfigureAwait(false);
        }
        // Bookkeeping failure cannot invalidate an already verified, published game file.
        // Without an ownership record reclamation retains it.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
        }
    }

    /// <summary>Checks whether an absolute file path is lexically contained by a selected storage root.</summary>
    public static bool Within(string file, string root) => Path.GetFullPath(file).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}