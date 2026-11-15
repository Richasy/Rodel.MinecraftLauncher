// Copyright (c) Richasy. All rights reserved.

using System.IO.Compression;

namespace Rodel.Minecraft.Seeds;

/// <summary>Reads blockstates, inherited JSON models and textures from the exact installed original client; it does not generate world blocks.</summary>
public static class SeedModelResources
{
    /// <summary>Loads a region's original models and an RGBA atlas, leaving the caller-owned original client stream open.</summary>
    /// <param name="region">A complete validated paletted original block snapshot.</param>
    /// <param name="originalClient">Readable and seekable original client JAR; no texture pack or remote resource is consulted.</param>
    /// <param name="cancellationToken">Stops archive decoding and model preparation between bounded resources.</param>
    /// <returns>Reusable original model data; unsupported special models are explicitly reported and omitted.</returns>
    public static SeedBlockScene Load(SeedRegionResult region, Stream originalClient, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(originalClient);
        SeedValidation.RegionColors(region);
        if (region.Bounds.Volume > 4_194_304 || region.Blocks.LongLength != region.Bounds.Volume * 2 || region.Palette.Length == 0)
            throw new ArgumentException("Original models require a complete bounded block snapshot.", nameof(region));
        using var archive = new ZipArchive(originalClient, ZipArchiveMode.Read, leaveOpen: true);
        return new SeedModelReader(archive, cancellationToken).Read(region);
    }
}