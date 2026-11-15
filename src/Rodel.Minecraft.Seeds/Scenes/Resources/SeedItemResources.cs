// Copyright (c) Richasy. All rights reserved.

using System.IO.Compression;

namespace Rodel.Minecraft.Seeds;

/// <summary>Reads inventory artwork from an installed original client independently of world generation.</summary>
public static class SeedItemResources
{
    /// <summary>Reads up to 1024 distinct item PNGs, falling back to the same block texture and leaving the supplied client stream open.</summary>
    public static IReadOnlyDictionary<string, byte[]> Read(IEnumerable<string> ids, Stream originalClient, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(originalClient);
        using var archive = new ZipArchive(originalClient, ZipArchiveMode.Read, leaveOpen: true);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var id in ids.Distinct(StringComparer.Ordinal).Take(1024))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parts = id.Split(':', 2);
            var name = parts[^1]; var prefix = "assets/" + (parts.Length == 2 ? parts[0] : "minecraft") + "/textures/";
            var entry = archive.GetEntry(prefix + "item/" + name + ".png") ?? archive.GetEntry(prefix + "block/" + name + ".png")
                ?? archive.GetEntry(prefix + "items/" + name + ".png") ?? archive.GetEntry(prefix + "blocks/" + name + ".png");
            if (entry is null || entry.Length > 4 * 1024 * 1024) continue;
            using var stream = entry.Open(); using var buffer = new MemoryStream(); stream.CopyTo(buffer);
            result[id] = buffer.ToArray();
        }

        return result;
    }
}