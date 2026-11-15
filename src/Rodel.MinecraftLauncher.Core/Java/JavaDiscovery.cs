// Copyright (c) Richasy. All rights reserved.

using Microsoft.Win32;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Rodel.MinecraftLauncher.Core.Java;

/// <summary>Finds Java in known installation layouts and validates executables with change-aware probe caching.</summary>
public sealed partial class JavaDiscovery(LauncherPaths? paths = null)
{
    private readonly LauncherPaths _paths = paths ?? new LauncherPaths();

    /// <summary>Resolves an explicit executable or Java home using the caller's path base.</summary>
    public static string ResolveExecutable(string path, string? baseDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = System.IO.Path.GetFullPath(path.Trim().Trim('"'), baseDirectory ?? Environment.CurrentDirectory);
        return Directory.Exists(full) ? System.IO.Path.Combine(full, "bin", "java.exe") : full;
    }

    /// <summary>Refreshes candidate locations and probes them with at most four concurrent workers; only caller cancellation ends the overall search.</summary>
    public async Task<JavaInstallation[]> DiscoverAsync(string? explicitPath = null, string? minecraftDirectory = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                path = ResolveExecutable(path);
                if (File.Exists(path))
                    candidates.Add(ResourceLease.CanonicalPath(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Trace.TraceWarning("Java discovery skipped {0}: {1}", path, ex.Message);
            }
        }

        void AddHome(string home)
        {
            Add(System.IO.Path.Combine(home, "bin", "java.exe"));
            Add(System.IO.Path.Combine(home, "jre", "bin", "java.exe"));
        }

        void SearchHomes(string root)
        {
            AddHome(root);
            foreach (var home in Directories(root))
                AddHome(home);
        }

        void SearchRuntimes(string root)
        {
            AddHome(root);
            foreach (var component in Directories(root))
            {
                SearchHomes(component);
                foreach (var platform in Directories(component))
                    SearchHomes(platform);
            }
        }

        Add(explicitPath);
        Add(Environment.GetEnvironmentVariable("JAVA_HOME"));
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(System.IO.Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(directory))
                Add(System.IO.Path.Combine(directory.Trim('"'), "java.exe"));
        }

        foreach (var root in new[] { _paths.RuntimesDirectory, _paths.LegacyRuntimesDirectory })
        {
            foreach (var directory in Directories(root).Where(directory => !System.IO.Path.GetFileName(directory).StartsWith('.')))
            {
                var registration = System.IO.Path.Combine(directory, "registration.json");
                if (!File.Exists(registration))
                    continue;
                try
                {
                    if (JsonFiles.ReadObject(registration)["javaPath"]?.GetValue<string>() is { } selected)
                        Add(System.IO.Path.GetFullPath(selected, directory));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
                {
                    Trace.TraceWarning("Java registration skipped {0}: {1}", registration, ex.Message);
                }
            }
        }

        if (OperatingSystem.IsWindows())
            AddRegistryHomes(AddHome);
        foreach (var programFiles in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var vendor in new[] { "Java", "Eclipse Adoptium", "AdoptOpenJDK", "Microsoft", "Zulu", "BellSoft", "Amazon Corretto", "Semeru" })
                SearchHomes(System.IO.Path.Combine(programFiles, vendor));
        }

        SearchHomes(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jdks"));
        if (minecraftDirectory is not null)
            SearchRuntimes(System.IO.Path.Combine(minecraftDirectory, "runtime"));
        SearchRuntimes(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages", "Microsoft.4297127D64EC6_8wekyb3d8bbwe", "LocalCache", "Local", "runtime"));
        var official = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(official))
            SearchRuntimes(System.IO.Path.Combine(official, "Minecraft Launcher", "runtime"));

        var ordered = candidates.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var results = new JavaInstallation[ordered.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, ordered.Length),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (index, token) =>
            {
                var candidate = ordered[index];
                var probed = await ProbeAsync(candidate, token).ConfigureAwait(false);
                // Re-probe wrappers each time; their target can change without changing the wrapper itself.
                if (probed.IsUsable && probed.JavaHome is { } home)
                {
                    var binary = System.IO.Path.Combine(home, "bin", "java.exe");
                    if (File.Exists(binary) && !string.Equals(ResourceLease.CanonicalPath(binary), candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        var actual = await ProbeAsync(binary, token).ConfigureAwait(false);
                        if (actual.IsUsable)
                            probed = actual with { DiscoveryPath = candidate, Warnings = [.. probed.Warnings, .. actual.Warnings] };
                    }
                }

                results[index] = probed;
            }).ConfigureAwait(false);
        return [.. results.DistinctBy(result => result.Path, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Checks an executable, reusing successful probe data only while its runtime fingerprint remains unchanged.</summary>
    public Task<JavaInstallation> ProbeAsync(string javaPath, CancellationToken ct = default) =>
        new JavaProbeCache(_paths).ProbeAsync(javaPath, ProbeUncachedAsync, ct);

    private static async Task<JavaInstallation> ProbeUncachedAsync(string javaPath, CancellationToken ct)
    {
        try
        {
            var properties = await RunProbeAsync(javaPath, true, ct).ConfigureAwait(false);
            var warnings = new List<string>();
            var probe = properties;
            if (properties.ExitCode != 0 || !properties.Properties.ContainsKey("java.version"))
            {
                probe = await RunProbeAsync(javaPath, false, ct).ConfigureAwait(false);
                warnings.Add("Java properties could not be fully probed: " + properties.Output.Trim());
            }

            var values = properties.ExitCode == 0 ? properties.Properties : new Dictionary<string, string>(StringComparer.Ordinal);
            var fullMatch = FullVersionRegex().Match(probe.Output);
            var full = values.GetValueOrDefault("java.version") ?? (fullMatch.Success ? fullMatch.Groups[1].Value : null);
            var major = JavaVersionInfo.Parse(full)?.Major;
            var architecture = values.GetValueOrDefault("os.arch")?.ToLowerInvariant() switch
            {
                "amd64" or "x86_64" or "x64" => "x64",
                "i386" or "i486" or "i586" or "i686" or "x86" => "x86",
                "aarch64" or "arm64" => "arm64",
                var other => other
            };
            int? bitness = int.TryParse(values.GetValueOrDefault("sun.arch.data.model"), out var bits) && bits > 0 ? bits : architecture switch
            {
                "x86" => 32,
                "x64" or "arm64" => 64,
                _ => null
            };
            return new(javaPath, major, probe.Output.Trim(), probe.ExitCode == 0, architecture, bitness)
            {
                FullVersion = full,
                FileEncoding = values.GetValueOrDefault("file.encoding"),
                NativeEncoding = values.GetValueOrDefault("native.encoding"),
                StdoutEncoding = values.GetValueOrDefault("stdout.encoding") ?? values.GetValueOrDefault("sun.stdout.encoding"),
                StderrEncoding = values.GetValueOrDefault("stderr.encoding") ?? values.GetValueOrDefault("sun.stderr.encoding"),
                JavaHome = values.GetValueOrDefault("java.home"),
                Warnings = [.. warnings]
            };
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return new(javaPath, null, ex.Message, false);
        }
    }

    private static async Task<(int ExitCode, string Output, Dictionary<string, string> Properties)> RunProbeAsync(string javaPath, bool properties, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(javaPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        if (properties)
            start.ArgumentList.Add("-XshowSettings:properties");
        start.ArgumentList.Add("-version");
        using var process = Process.Start(start) ?? throw new IOException("Java did not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        string output;
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            output = (await stderr.ConfigureAwait(false)) + (await stdout.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try { await Task.WhenAll(stderr, stdout).ConfigureAwait(false); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }

            ct.ThrowIfCancellationRequested();
            return (-1, "Java probe timed out.", new(StringComparer.Ordinal));
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n'))
        {
            var separator = line.IndexOf('=');
            if (separator > 0)
                values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return (process.ExitCode, output, values);
    }

    private static string[] Directories(string root)
    {
        if (!Directory.Exists(root))
            return [];
        try { return Directory.GetDirectories(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Java directory skipped {0}: {1}", root, ex.Message);
            return [];
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistryHomes(Action<string> addHome)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                RegistryKey root;
                try { root = RegistryKey.OpenBaseKey(hive, view); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    Trace.TraceWarning("Java registry hive skipped {0}: {1}", hive, ex.Message);
                    continue;
                }

                using var rootDisposal = root;
                foreach (var location in new[]
                {
                    @"SOFTWARE\JavaSoft\Java Runtime Environment", @"SOFTWARE\JavaSoft\Java Development Kit",
                    @"SOFTWARE\JavaSoft\JRE", @"SOFTWARE\JavaSoft\JDK",
                    @"SOFTWARE\Eclipse Adoptium\JDK", @"SOFTWARE\Eclipse Adoptium\JRE",
                    @"SOFTWARE\Microsoft\JDK", @"SOFTWARE\Azul Systems\Zulu", @"SOFTWARE\BellSoft\Liberica"
                })
                {
                    try
                    {
                        using var key = root.OpenSubKey(location);
                        if (key is not null)
                            ReadRegistryHomes(key, addHome, 3);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        Trace.TraceWarning("Java registry location skipped {0}: {1}", location, ex.Message);
                    }
                }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ReadRegistryHomes(RegistryKey key, Action<string> addHome, int depth)
    {
        foreach (var value in new[] { "JavaHome", "Path", "InstallationPath" })
        {
            if (key.GetValue(value) is string home && !string.IsNullOrWhiteSpace(home))
                addHome(home);
        }

        if (depth == 0)
            return;
        foreach (var name in key.GetSubKeyNames())
        {
            using var child = key.OpenSubKey(name);
            if (child is not null)
                ReadRegistryHomes(child, addHome, depth - 1);
        }
    }

    [GeneratedRegex("(?:version\\s+\"|openjdk\\s+)(\\d[^\"\\s]*)", RegexOptions.CultureInvariant)]
    private static partial Regex FullVersionRegex();
}