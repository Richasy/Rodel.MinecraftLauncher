// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Instances;

public sealed partial class InstanceStore
{
    /// <summary>Saves a small instance-owned icon in metadata so copies remain independent of its source image.</summary>
    public async Task<InstanceRecord> SetIconAsync(string id, string? imageFile = null, CancellationToken cancellationToken = default)
    {
        JsonObject? icon = null;
        if (imageFile is not null)
        {
            using var input = new FileStream(imageFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length is <= 0 or > 4 * 1024 * 1024)
                throw new ArgumentException("Choose a PNG or JPEG image no larger than 4 MiB.", nameof(imageFile));
            var bytes = new byte[checked((int)input.Length)];
            await input.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            var png = bytes.Length >= 24 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var jpeg = bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
            if (!png && !jpeg)
                throw new InvalidDataException("Choose a readable PNG or JPEG image.");
            icon = new JsonObject { ["data"] = Convert.ToBase64String(bytes), ["format"] = png ? "png" : "jpeg" };
        }

        using var lease = await ResourceLease.AcquireAsync(resource: MetadataPath(id), cancellationToken: cancellationToken).ConfigureAwait(false);
        var metadata = Get(id).ToMetadata();
        if (icon is null) metadata.Remove("icon");
        else metadata["icon"] = icon;
        var result = await SaveAsync(id, metadata, cancellationToken).ConfigureAwait(false);
        await new LauncherActivityLog(paths).WriteAsync("instance.icon-changed", id, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
        return result;
    }
}