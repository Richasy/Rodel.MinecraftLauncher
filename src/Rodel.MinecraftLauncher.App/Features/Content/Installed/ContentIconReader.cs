// Copyright (c) Richasy. All rights reserved.

using System.IO.Compression;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed class ContentIconReader : IDisposable
{
    private const int MaximumBytes = 1024 * 1024;
    private const int CacheBytes = 16 * MaximumBytes;
    private static readonly SemaphoreSlim Slots = new(4);
    private readonly Dictionary<string, byte[]> _cache = [with(StringComparer.Ordinal)];
    private readonly Lock _gate = new();
    private int _bytes;

    internal Task<(byte[]? Bytes, string? Warning)> ReadLocalAsync(
        string game,
        ContentItem item,
        ContentKind kind,
        CancellationToken ct)
    {
        if (item.IconPath is null && kind == ContentKind.Mods)
            return Task.FromResult<(byte[]?, string?)>((null, null));
        var icon = item.IconPath ?? "pack.png";
        var key = item.Sha1 is { Length: > 0 } hash ? hash + ":" + icon : string.Empty;
        var path = Path.GetFullPath(Path.Combine(game, item.StoredPath.Replace('/', '\\')));
        if (key.Length == 0 && File.Exists(path))
        {
            try
            {
                var file = new FileInfo(path);
                key = path + ":" + file.Length + ":" + file.LastWriteTimeUtc.Ticks + ":" + icon;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The actual read below retains the feature's normal recoverable warning.
            }
        }

        return ReadAsync(key, async () =>
            {
                if (System.IO.Directory.Exists(path))
                {
                    var child = Path.GetFullPath(Path.Combine(path, icon.Replace('/', '\\')));
                    if (!child.StartsWith(Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Icon path leaves the content directory.");
                    if (!File.Exists(child) && item.IconPath is null)
                        return null;
                    await using var input = File.OpenRead(child);
                    return await ReadBoundedAsync(input, ct).ConfigureAwait(false);
                }

                using var zip = ZipFile.OpenRead(path);
                var entry = zip.GetEntry(icon.Replace('\\', '/').TrimStart('/'));
                if (entry is null)
                {
                    if (item.IconPath is not null)
                        throw new InvalidDataException("The declared content icon is missing.");
                    return null;
                }

                if (entry.Length > MaximumBytes)
                    throw new InvalidDataException("The content icon exceeds the image byte limit.");
                await using var stream = entry.Open();
                return await ReadBoundedAsync(stream, ct).ConfigureAwait(false);
            }, ct);
    }

    private async Task<(byte[]? Bytes, string? Warning)> ReadAsync(string key, Func<Task<byte[]?>> read, CancellationToken ct)
    {
        lock (_gate)
        {
            if (key.Length > 0 && _cache.TryGetValue(key, out var cached))
                return (cached, null);
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            byte[]? bytes;
            await Slots.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                bytes = await read().ConfigureAwait(false);
            }
            finally
            {
                Slots.Release();
            }

            if (bytes is null)
                return (null, null);
            if (key.Length > 0)
            {
                lock (_gate)
                {
                    if (_cache.TryGetValue(key, out var cached))
                        return (cached, null);
                    while (_cache.Count >= 64 || _bytes + bytes.Length > CacheBytes)
                    {
                        var oldest = _cache.Keys.First();
                        _bytes -= _cache[oldest].Length;
                        _cache.Remove(oldest);
                    }

                    _cache[key] = bytes;
                    _bytes += bytes.Length;
                }
            }

            return (bytes, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException or ArgumentException or NotSupportedException)
        {
            return (null, exception.Message);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[16384];
        while (await stream.ReadAsync(buffer, ct).ConfigureAwait(false) is > 0 and var read)
        {
            if (output.Length + read > MaximumBytes)
                throw new InvalidDataException("The content icon exceeds the image byte limit.");
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    /// <summary>Releases cached icon bytes; in-flight readers retain the process-owned concurrency gate until they finish.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _cache.Clear();
            _bytes = 0;
        }
    }
}