// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Java;

/// <summary>Reuses successful, complete Java probes after validating their executable, runtime files and environment.</summary>
internal sealed class JavaProbeCache(LauncherPaths paths)
{
    internal async Task<JavaInstallation> ProbeAsync(string executable,
        Func<string, CancellationToken, Task<JavaInstallation>> probe, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var warnings = new List<string>();
        string path;
        try { path = ResourceLease.CanonicalPath(executable); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(executable, null, ex.Message, false);
        }

        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        var file = System.IO.Path.Combine(paths.CacheDirectory, "java-probes", key + ".json");
        ResourceLease? lease = null;
        try { lease = await ResourceLease.AcquireAsync(file, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add("Java probe cache coordination failed: " + ex.Message);
        }

        using (lease)
        {
            string? Fingerprint()
            {
                try { return GetFingerprint(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add("Java probe cache validation failed: " + ex.Message);
                    return null;
                }
            }

            var before = Fingerprint();
            if (lease is not null && before is not null && File.Exists(file))
            {
                try
                {
                    var entry = JsonSerializer.Deserialize(JsonFiles.ReadObject(file), JavaJsonContext.Default.JavaProbeCacheEntry);
                    if (entry is { SchemaVersion: 1, Installation: { IsUsable: true, FullVersion: not null, JavaHome: not null, Warnings: not null } }
                        && entry.Fingerprint == before && string.Equals(entry.Installation.Path, path, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.Installation;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                {
                    warnings.Add("Java probe cache could not be read; probing the executable: " + ex.Message);
                }
            }

            var result = await probe(path, ct).ConfigureAwait(false);
            var after = Fingerprint();
            if (before is not null && after != before)
            {
                before = after;
                result = await probe(path, ct).ConfigureAwait(false);
                after = Fingerprint();
                if (before != after)
                    return result with { IsUsable = false, Warnings = [.. warnings, .. result.Warnings, "Java changed repeatedly during probing; retry after its update finishes."] };
            }
            // Wrappers and incomplete property probes are not stable runtime identities.
            var stableHome = false;
            if (result.JavaHome is { } home)
            {
                try
                {
                    var reportedHome = ResourceLease.CanonicalPath(home);
                    var installationHome = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(path))!;
                    stableHome = string.Equals(reportedHome, installationHome, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(reportedHome, System.IO.Path.Combine(installationHome, "jre"), StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    warnings.Add("Java home could not be validated for probe caching: " + ex.Message);
                }
            }

            if (lease is not null && before is not null && after == before && result.IsUsable
                && result.Warnings.Length == 0 && result.FullVersion is not null && stableHome)
            {
                try
                {
                    var entry = new JavaProbeCacheEntry(1, before, result);
                    await JsonFiles.WriteAsync(file, JsonSerializer.SerializeToNode(entry, JavaJsonContext.Default.JavaProbeCacheEntry)!, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add("Java probe succeeded but its cache could not be saved: " + ex.Message);
                }
            }

            return result with { Warnings = [.. result.Warnings, .. warnings] };
        }
    }

    private static string? GetFingerprint(string executable)
    {
        var bin = System.IO.Path.GetDirectoryName(executable);
        if (!string.Equals(System.IO.Path.GetFileName(bin), "bin", StringComparison.OrdinalIgnoreCase) || !File.Exists(executable))
            return null;
        var home = System.IO.Path.GetDirectoryName(bin)!;
        var release = System.IO.Path.Combine(home, "release");
        var runtimeFiles = new[]
        {
            "bin\\server\\jvm.dll", "bin\\client\\jvm.dll", "bin\\java.dll", "bin\\jli.dll",
            "jre\\bin\\server\\jvm.dll", "jre\\bin\\client\\jvm.dll", "jre\\bin\\java.dll", "jre\\bin\\jli.dll",
            "lib\\modules", "lib\\rt.jar", "jre\\lib\\rt.jar"
        };
        var material = new StringBuilder("java-probe-v1|");
        var releaseFiles = string.Equals(System.IO.Path.GetFileName(home), "jre", StringComparison.OrdinalIgnoreCase)
            ? new[] { executable, release, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(home)!, "release") }
            : [executable, release];
        foreach (var file in releaseFiles)
        {
            material.Append(file).Append('|');
            if (File.Exists(file))
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                material.Append(Convert.ToHexString(SHA256.HashData(stream)));
            }

            material.Append('|');
        }

        foreach (var relative in runtimeFiles)
        {
            var file = new FileInfo(System.IO.Path.Combine(home, relative));
            material.Append(relative).Append('|');
            if (file.Exists)
                material.Append(ResourceLease.CanonicalPath(file.FullName)).Append('|').Append(file.Length).Append('|').Append(file.LastWriteTimeUtc.Ticks);
            material.Append('|');
        }

        foreach (var variable in new[] { "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS", "JDK_JAVA_OPTIONS", "LANG", "LC_ALL", "LC_CTYPE" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            material.Append(variable).Append('=').Append(value?.Length ?? -1).Append(':').Append(value).Append('|');
        }

        material.Append(System.Globalization.CultureInfo.CurrentCulture.Name).Append('|').Append(Console.OutputEncoding.WebName);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }
}

/// <summary>Stores a versioned successful observation and the runtime fingerprint that validates it.</summary>
internal sealed record JavaProbeCacheEntry(int SchemaVersion, string Fingerprint, JavaInstallation Installation);