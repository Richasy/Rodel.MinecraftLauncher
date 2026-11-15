// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Rodel.MinecraftLauncher.Core.Packs;

internal static class PackFiles
{
    internal static string Safe(string root, string relative, bool allowInternal = false)
    {
        var selectedRoot = ResourceLease.CanonicalPath(root);
        var file = SafeRead(selectedRoot, relative, allowInternal);
        var resolved = ResourceLease.CanonicalPath(file);
        var prefix = selectedRoot.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Pack target escapes the selected game directory through a link: {relative}");
        return resolved;
    }

    // Exporting a player-selected readable link is safe: ZIP entries retain their relative logical path.
    internal static string SafeRead(string root, string relative, bool allowInternal = false)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') || relative.StartsWith('/') || relative.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ')))
            throw new InvalidDataException($"Unsafe pack path: {relative}");
        if (!allowInternal && (relative.Split('/')[0].Equals(".rodel", StringComparison.OrdinalIgnoreCase) || relative.Equals(".rodel-instance-owner", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"Pack cannot overwrite launcher ownership metadata: {relative}");
        var prefix = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var file = Path.GetFullPath(Path.Combine(prefix, relative));
        if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Pack file escapes destination.");
        return file;
    }

    internal static void NoLinks(string path)
    {
        // Explicit roots/archive/output paths may be aliases. Relative writes are bounded by Safe().
        _ = ResourceLease.CanonicalPath(path);
    }

    internal static async Task<string?> Sha1Async(string file, CancellationToken token)
    {
        if (!File.Exists(file))
            return null;
        NoLinks(file);
        var stream = File.OpenRead(file);
        await using var streamDisposal = stream.ConfigureAwait(false);
        return Convert.ToHexString(await SHA1.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
    }

    internal static async Task<string> Sha512Async(string file, CancellationToken token)
    {
        var stream = File.OpenRead(file);
        await using var streamDisposal = stream.ConfigureAwait(false);
        return Convert.ToHexString(await SHA512.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
    }

    internal static IEnumerable<string> Enumerate(string directory, Func<string, bool>? includeDirectory = null, bool allowReadLinks = false)
    {
        var selectedRoot = ResourceLease.CanonicalPath(directory);
        var logicalRoot = Path.GetFullPath(directory);
        var prefix = selectedRoot.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> Walk(string current)
        {
            var physical = ResourceLease.CanonicalPath(current);
            if (!active.Add(physical))
                throw new IOException($"Pack source contains a directory link cycle: {current}");
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                {
                    var attributes = File.GetAttributes(entry);
                    var isDirectory = (attributes & FileAttributes.Directory) != 0;
                    var relative = Path.GetRelativePath(logicalRoot, entry).Replace('\\', '/');
                    // Do not resolve or walk excluded trees: their junction targets are unrelated to the export.
                    if (isDirectory && includeDirectory is not null && !includeDirectory(relative))
                        continue;
                    var resolved = ResourceLease.CanonicalPath(entry);
                    if (!allowReadLinks && !resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        throw new IOException($"Pack entry escapes the selected directory through a link: {entry}");
                    if (isDirectory)
                    {
                        foreach (var file in Walk(entry))
                            yield return file;
                    }
                    else
                    {
                        yield return entry;
                    }
                }
            }
            finally
            {
                active.Remove(physical);
            }
        }

        return Walk(logicalRoot);
    }

    internal static async Task PublishAsync(string source, string target, CancellationToken token)
    {
        NoLinks(source);
        NoLinks(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".pack-tmp";
        try
        {
            {
                var input = File.OpenRead(source);
                await using (input.ConfigureAwait(false))
                {
                    var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                    await using (output.ConfigureAwait(false))
                        await input.CopyToAsync(output, token).ConfigureAwait(false);
                }
            }

            token.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    internal static async Task ExtractAsync(ZipArchiveEntry entry, string target, IProgress<DownloadProgress> progress, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var input = entry.Open();
        await using var inputDisposal = input.ConfigureAwait(false);
        var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
        await using var outputDisposal = output.ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            long copied = 0;
            int count;
            while ((count = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                copied += count;
                progress.Report(new(target, copied, entry.Length, false) { Stage = "pack-extract" });
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal static string? Kind(string relative) => relative.Split('/')[0] switch
    {
        "mods" => "mod",
        "resourcepacks" => "resourcepack",
        "shaderpacks" => "shader",
        _ => null
    };
}