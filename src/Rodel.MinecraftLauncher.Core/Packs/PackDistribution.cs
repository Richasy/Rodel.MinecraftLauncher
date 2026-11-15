// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Packs;

public sealed partial class PackService
{
    /// <summary>Fetches a retained HMCL distribution and stages an exact file review without applying it or starting the game.</summary>
    /// <param name="instanceId">The imported HMCL client, independently of any game-server association.</param>
    /// <param name="cancellationToken">Cancels metadata or file acquisition without changing the installed baseline.</param>
    /// <returns>A durable review whose staged hashes and manifest are reused by ApplyAsync, including missing files and preserved player changes.</returns>
    public async Task<PackReview> ReviewDistributionAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        using var cacheUse = await CacheActivity.EnterAsync(_paths, cancellationToken).ConfigureAwait(false);
        var instance = new InstanceStore(_paths).Get(instanceId);
        var pack = ReadLock(instance);
        if (pack["format"]?.ToString() != "hmcl")
            throw new InvalidOperationException("This pack does not have an HMCL distribution manifest.");
        if (pack["sourceDetached"]?.GetValue<bool>() == true)
            throw new InvalidOperationException("This client is detached from distribution updates. Import a new archive explicitly to select a source.");
        var source = HmclPack.ManifestUri(pack["manifest"]!.AsObject());
        var metadata = Path.Combine(_paths.CacheDirectory, "pack-distributions", Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await new DownloadService(_paths, _http).DownloadAsync(source, metadata, progress: _progress, cancellationToken: cancellationToken).ConfigureAwait(false);
            var manifest = JsonFiles.ReadObject(metadata);
            var archive = await MaterializeHmclManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
            var review = await ReviewAsync(instanceId, archive, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!JsonNode.DeepEquals(pack, review.PreviousLock))
            {
                review.Phase = "stale";
                await WriteReviewAsync(review, CancellationToken.None).ConfigureAwait(false);
                throw new InvalidOperationException("The distribution source or pack ownership changed while checking. Review the current source again.");
            }

            review.Distribution = new(source.AbsoluteUri, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.ToJsonString()))).ToLowerInvariant());
            await WriteReviewAsync(review, cancellationToken).ConfigureAwait(false);
            return review;
        }
        finally
        {
            foreach (var file in new[] { metadata, metadata + ".rodel.partial", metadata + ".rodel.partial.json" })
                if (File.Exists(file)) File.Delete(file);
        }
    }

    private async Task<string> MaterializeHmclManifestAsync(JsonObject manifest, CancellationToken cancellationToken)
    {
        _ = HmclPack.Dependencies(manifest);
        _ = HmclPack.Files(manifest);
        var bytes = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var archive = Path.Combine(_paths.Root, "packs", "sources", hash + ".zip");
        using var lease = await ResourceLease.AcquireAsync(resource: archive, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (File.Exists(archive) && JsonNode.DeepEquals(PackArchive.Inspect(archive).Manifest, manifest))
            return archive;
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        var temporary = archive + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            {
                var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                await using (output.ConfigureAwait(false))
                {
                    using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
                    var entry = zip.CreateEntry("server-manifest.json").Open();
                    await using (entry.ConfigureAwait(false))
                        await entry.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, archive, overwrite: true);
            return archive;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}