// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.App.Features.Content;

/// <summary>Reads a bounded optional save thumbnail off the render path; callers retain their own warning policy.</summary>
internal static class WorldIconReader
{
    internal static async Task<byte[]> ReadAsync(string file, CancellationToken cancellationToken)
    {
        using var input = File.OpenRead(file);
        if (input.Length > 2 * 1024 * 1024)
            throw new InvalidDataException("World icon exceeds 2 MiB.");
        var icon = new byte[(int)input.Length];
        await input.ReadExactlyAsync(icon, cancellationToken).ConfigureAwait(false);
        return icon;
    }
}