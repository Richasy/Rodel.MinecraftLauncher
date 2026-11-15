// Copyright (c) Richasy. All rights reserved.

using System.Security.Cryptography;
using System.Text;

namespace Rodel.MinecraftLauncher.Core.Storage;

public sealed class ResourceLease : IDisposable
{
    private readonly FileStream _stream;
    private ResourceLease(FileStream stream) => _stream = stream;
    /// <summary>Acquires an exclusive process-coordinated lease for a canonical resource path, independent of launcher data roots; dispose it to release the lock.</summary>
    public static async Task<ResourceLease> AcquireAsync(string resource, CancellationToken cancellationToken = default)
    {
        var key = CanonicalPath(resource).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        // A shared game/cache directory must coordinate even when callers use different launcher data roots.
        var directory = Path.Combine(Path.GetTempPath(), "Rodel.MinecraftLauncher.Locks");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".lock");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Keep the zero-byte rendezvous file: deleting it on release creates a Windows
                // delete-pending interval that reports access denied instead of lock contention.
                // Ownership belongs to the exclusive handle, never to the file's existence.
                return new ResourceLease(new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None));
            }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Releases the exclusive handle, retaining the empty rendezvous file for subsequent owners.</summary>
    public void Dispose() => _stream.Dispose();
    /// <summary>Resolves existing directory and file links while retaining the lexical path of not-yet-created or atomically replaced segments.</summary>
    /// <param name="path">The resource path whose aliases must share a lock.</param>
    /// <returns>An absolute path with available link targets resolved.</returns>
    public static string CanonicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var segment in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) == 0) continue;
                FileSystemInfo info = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(current) : new FileInfo(current);
                current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? current;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }

        return current;
    }
}