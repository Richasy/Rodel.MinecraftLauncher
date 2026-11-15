// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Storage;

/// <summary>Resolves the data root and an optional host-owned location for disposable caches.</summary>
/// <param name="root">The selected data directory, or null for the user's .rmcl directory.</param>
/// <param name="applicationDataDirectory">Optional host-owned package data directory exposed to AI for diagnostics; null grants no package directory.</param>
/// <param name="cacheDirectory">An optional cache location, resolved once against the current directory; null preserves data-root/cache.</param>
public sealed class LauncherPaths(string? root = null, string? cacheDirectory = null, string? applicationDataDirectory = null)
{
    public string Root { get; } = Path.GetFullPath(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rmcl"));
    /// <summary>Gets the host package data directory, when supplied, for fixed read-only AI diagnostics access.</summary>
    public string? ApplicationDataDirectory { get; } = applicationDataDirectory is null ? null : Path.GetFullPath(applicationDataDirectory);
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string InstancesDirectory => Path.Combine(Root, "instances");
    /// <summary>Gets disposable cache storage without changing instance, runtime, configuration or player-data locations.</summary>
    public string CacheDirectory { get => field ?? Path.Combine(Root, "cache"); } = cacheDirectory is null ? null : Path.GetFullPath(cacheDirectory);
    public string DependenciesDirectory => Path.Combine(Root, "dependencies");
    public string RuntimesDirectory => Path.Combine(DependenciesDirectory, "java");
    internal string LegacyRuntimesDirectory => Path.Combine(Root, "runtimes");
    public string JobsDirectory => Path.Combine(Root, "jobs");
    public string SessionsDirectory => Path.Combine(Root, "sessions");
    public string BackupsDirectory => Path.Combine(Root, "backups");
    public string LogsDirectory => Path.Combine(Root, "logs");

    public void EnsureDirectories()
    {
        foreach (var directory in new[]
        {
            Root,
            InstancesDirectory,
            CacheDirectory,
            RuntimesDirectory,
            JobsDirectory,
            SessionsDirectory,
            BackupsDirectory,
            LogsDirectory
        }

        )
        {
            Directory.CreateDirectory(directory);
        }
    }
}