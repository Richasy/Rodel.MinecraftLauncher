// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Core.Installation;

/// <summary>Reuses verified files from existing managed dependency sets, without writing into their sources.</summary>
internal sealed class SharedInstallationCache(LauncherPaths paths, string runtime)
{
    private readonly string[] _assets = Sets(paths, "assets");
    private readonly string[] _libraries = Sets(paths, "libraries");
    private readonly string[] _clients = Enumerate(Path.Combine(paths.DependenciesDirectory, "clients"), files: true);
    private readonly string[] _roots = [.. Enumerate(paths.InstancesDirectory).SelectMany(d => new[] { Path.Combine(d, "runtime"), Path.Combine(d, "game") })];

    private int _unavailable;
    internal int UnavailableCandidates => Volatile.Read(ref _unavailable);

    internal async Task<bool> TryReuseAsync(string destination, string? sha1, long? size, CancellationToken ct)
    {
        if (sha1 is null || sha1.Length != 40 || !sha1.All(Uri.IsHexDigit)) return false;
        foreach (var candidate in Candidates(destination))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var source = ResourceLease.CanonicalPath(candidate);
                if (!DownloadOwnership.Within(source, paths.DependenciesDirectory) && !DownloadOwnership.Within(source, paths.InstancesDirectory)) continue;
                if (source.Equals(destination, StringComparison.OrdinalIgnoreCase) || !await DownloadService.ValidAsync(source, sha1, size, ct).ConfigureAwait(false)) continue;
                using var lease = await ResourceLease.AcquireAsync(resource: destination, cancellationToken: ct).ConfigureAwait(false);
                var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".reuse";
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                try
                {
                    using (var input = File.OpenRead(source))
                    using (var output = File.Create(temporary))
                        await input.CopyToAsync(output, ct).ConfigureAwait(false);
                    if (!await DownloadService.ValidAsync(temporary, sha1, size, ct).ConfigureAwait(false)) continue;
                    ct.ThrowIfCancellationRequested();
                    File.Move(temporary, destination, true);
                    return true;
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { Interlocked.Increment(ref _unavailable); }
        }

        return false;
    }

    private IEnumerable<string> Candidates(string destination)
    {
        if (DownloadOwnership.Within(destination, paths.DependenciesDirectory))
        {
            var parts = Path.GetRelativePath(paths.DependenciesDirectory, destination).Split(Path.DirectorySeparatorChar);
            if (parts[0] == "clients") return _clients;
            if (parts.Length > 2 && parts[0] is "assets" or "libraries")
            {
                var relative = Path.Combine([.. parts.Skip(2)]);
                return (parts[0] == "assets" ? _assets : _libraries).Select(set => Path.Combine(set, relative));
            }
        }

        if (DownloadOwnership.Within(destination, runtime))
        {
            var relative = Path.GetRelativePath(runtime, destination);
            if (relative.StartsWith("versions" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return _roots.Select(root => Path.Combine(root, relative));
        }

        return [];
    }

    private static string[] Sets(LauncherPaths paths, string kind)
    {
        var directory = Path.Combine(paths.DependenciesDirectory, kind);
        return [.. Enumerate(directory).Where(d => Path.GetFileName(d) is { Length: 64 } id && id.All(Uri.IsHexDigit))];
    }
    private static string[] Enumerate(string directory, bool files = false)
    {
        try { return !Directory.Exists(directory) ? [] : files ? Directory.GetFiles(directory, "*.jar") : Directory.GetDirectories(directory); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return []; }
    }

}