// Copyright (c) Richasy. All rights reserved.

using System.IO.Compression;

namespace Rodel.Minecraft.Seeds;

/// <summary>Reads overhead block-model colors from an installed resource archive, sharing the scene model/texture decoder.</summary>
public sealed class SeedSurfaceColors : IDisposable
{
    private readonly ZipArchive _archive;
    private readonly SeedModelReader _reader;
    private readonly Dictionary<string, uint?> _colors = new(StringComparer.Ordinal);

    /// <summary>Opens original model resources, retaining the supplied readable, seekable stream until disposal without owning it.</summary>
    public SeedSurfaceColors(Stream originalClient)
    {
        _archive = new(originalClient, ZipArchiveMode.Read, leaveOpen: true);
        _reader = new(_archive, CancellationToken.None);
    }

    /// <summary>Gets an alpha-weighted top-model color with native tint-index colors; unavailable models return null.</summary>
    public uint? Color(SeedBlockState state, int[] tints)
    {
        var key = state.Id + "[" + string.Join(',', state.Properties.OrderBy(pair => pair.Key).Select(pair => pair.Key + "=" + pair.Value)) + "]:" + string.Join(',', tints);
        if (_colors.TryGetValue(key, out var color)) return color;
        try { color = _reader.SurfaceColor(state, tints); }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { color = null; }

        return _colors[key] = color;
    }

    /// <summary>Releases archive readers; the caller retains ownership of the original client stream.</summary>
    public void Dispose() => _archive.Dispose();
}