// Copyright (c) Richasy. All rights reserved.

using System.Security.Cryptography;
using System.Text;

namespace Rodel.MinecraftLauncher.Core.Storage;

/// <summary>Concurrent consumers share a handle; reclamation requires an exclusive handle.</summary>
public static class CacheActivity
{
    public static FileStream? TryEnter(LauncherPaths paths, bool exclusive = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Rodel.MinecraftLauncher.CacheLocks");
        Directory.CreateDirectory(directory);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ResourceLease.CanonicalPath(paths.CacheDirectory).ToUpperInvariant())));
        try
        {
            return new FileStream(Path.Combine(directory, key), FileMode.OpenOrCreate, FileAccess.ReadWrite, exclusive ? FileShare.None : FileShare.ReadWrite);
        }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
        {
            return null;
        }
    }

    public static async Task<FileStream> EnterAsync(LauncherPaths paths, CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (TryEnter(paths) is { } handle)
                return handle;
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }
}