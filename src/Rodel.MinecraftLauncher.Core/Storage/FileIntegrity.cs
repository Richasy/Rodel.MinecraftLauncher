// Copyright (c) Richasy. All rights reserved.

using System.Buffers;
using System.Security.Cryptography;

namespace Rodel.MinecraftLauncher.Core.Storage;

/// <summary>Reads requested integrity digests together through one ordinary file handle.</summary>
internal static class FileIntegrity
{
    internal static async Task<FileDigests> ReadAsync(string path, bool sha1, bool sha256, bool sha512, CancellationToken ct, Action<long, long>? progress = null)
    {
        using var first = sha1 ? IncrementalHash.CreateHash(HashAlgorithmName.SHA1) : null;
        using var second = sha256 ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
        using var third = sha512 ? IncrementalHash.CreateHash(HashAlgorithmName.SHA512) : null;
        var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var disposal = input.ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            var total = input.Length;
            long read = 0;
            int count;
            while ((count = await input.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
            {
                first?.AppendData(buffer, 0, count);
                second?.AppendData(buffer, 0, count);
                third?.AppendData(buffer, 0, count);
                read += count;
                progress?.Invoke(read, total);
            }

            ct.ThrowIfCancellationRequested();
            return new(read, Digest(first), Digest(second), Digest(third));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string? Digest(IncrementalHash? hash) => hash is null ? null : Convert.ToHexStringLower(hash.GetHashAndReset());
}

/// <summary>Integrity results from one complete read of the selected file.</summary>
/// <param name="Size">The actual number of bytes read.</param>
/// <param name="Sha1">The SHA-1 digest, when requested.</param>
/// <param name="Sha256">The SHA-256 digest, when requested.</param>
/// <param name="Sha512">The SHA-512 digest, when requested.</param>
internal sealed record FileDigests(long Size, string? Sha1, string? Sha256, string? Sha512);