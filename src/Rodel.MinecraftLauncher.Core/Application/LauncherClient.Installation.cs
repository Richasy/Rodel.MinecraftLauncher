// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Installation;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Loaders;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    public async Task<IInstanceImportResult> ImportInstanceAsync(string name, string directory, InstanceImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        using var cacheUse = await CacheActivity.EnterAsync(paths, cancellationToken).ConfigureAwait(false);
        if (options.PlanOnly)
            return Instances.PreviewImport(directory, options.Profile, options.MinecraftDirectory, options.LauncherConfiguration, options.VersionId, options.GameDirectory);
        if (options.ServerId is not null)
            _ = await Servers.GetAsync(options.ServerId, cancellationToken).ConfigureAwait(false);
        var instance = options.Migrate || options.Copy || options.Profile is not null || options.GameDirectory is not null ? await Instances.ImportMigrationAsync(name: name, directory: directory, profileId: options.Profile, copy: options.Copy, minecraftDirectory: options.MinecraftDirectory, version: options.VersionId, cancellationToken: cancellationToken, launcherConfig: options.LauncherConfiguration, gameDirectory: options.GameDirectory, progress: new InlineProgress<LauncherProgress>(value => Report(value))).ConfigureAwait(false) : await Instances.ImportAsync(name: name, gameDirectory: directory, version: options.VersionId, cancellationToken: cancellationToken, minecraftDirectory: options.MinecraftDirectory).ConfigureAwait(false);
        if (options.ServerId is not null)
        {
            try
            {
                var owned = await Servers.SetClientOwnerAsync(instance.Id, options.ServerId, ct: cancellationToken).ConfigureAwait(false);
                return owned;
            }
            catch (OperationCanceledException canceled)
            {
                throw new LauncherOperationCanceledException("Import retained as a local instance; server client registration was canceled.", new JsonObject { ["instance"] = instance.ToJson() }, canceled);
            }
            catch (Exception exception)
            {
                throw new LauncherOperationException("SERVER_CLIENT_REGISTRATION_FAILED", "Import retained as a local instance; server client registration failed. " + exception.Message, new JsonObject { ["instance"] = instance.ToJson() });
            }
        }

        return instance.Describe();
    }

    /// <summary>Installs the selected instance's game files and optionally prepares Java without starting a game process.</summary>
    public async Task<IInstallationResult> InstallInstanceAsync(string instanceId, InstanceInstallationOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        using var cacheUse = await CacheActivity.EnterAsync(paths, cancellationToken).ConfigureAwait(false);
        var instance = Instances.Get(instanceId);
        var effective = Settings.GetEffective(instance);
        var version = options.VersionId ?? effective.Values["versionId"]?.GetValue<string>() ?? instance.VersionId ?? throw new ArgumentException("Select a runtime version.");
        if (options.PlanOnly)
            return await new LaunchPlanner(paths).BuildAsync(minecraftDirectory: instance.MinecraftDirectory, gameDirectory: instance.GameDirectory, versionId: version, javaPath: SelectJavaPath(effective, options.JavaPath, instance.JavaPath), ct: cancellationToken).ConfigureAwait(false);
        if (Sessions.HasActiveGame(instance.GameDirectory))
            throw new IOException("Retry installation after this game exits.");
        var result = await Installation.InstallAsync(instance.MinecraftDirectory, version, progress: InstallationEvents, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (options.PrepareJava)
            await PrepareInstalledJavaAsync(instance, version, options.JavaPath, result, cancellationToken).ConfigureAwait(false);

        return result;
    }

    public async Task<LoaderInstallationResult> InstallLoaderAsync(string instanceId, string loader, LoaderInstallationOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        using var cacheUse = await CacheActivity.EnterAsync(paths, cancellationToken).ConfigureAwait(false);
        var instance = Instances.Get(instanceId);
        if (Sessions.HasActiveGame(instance.GameDirectory))
            throw new IOException("Retry loader installation after this game exits.");
        var context = await GetContentContextAsync(instance.Id, options.MinecraftVersion, ct: cancellationToken).ConfigureAwait(false);
        var installed = await new LoaderService(paths, libraryMirrors: options.LibraryMirrors).InstallAsync(instance.MinecraftDirectory, context.MinecraftVersion ?? throw new ArgumentException("A Minecraft version is required."), loader, options.LoaderVersion, options.JavaPath, progress: LoaderEvents, cancellationToken: cancellationToken).ConfigureAwait(false);
        using var gameLease = await ResourceLease.AcquireAsync(resource: instance.GameDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (Sessions.HasActiveGame(instance.GameDirectory))
            throw new IOException("Game started during loader preparation; selection remains unchanged and prepared files are reusable.");
        using var metadataLease = await ResourceLease.AcquireAsync(resource: instance.MetadataPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        var record = JsonFiles.ReadObject(instance.MetadataPath);
        var settings = record["settings"] as JsonObject ?? [];
        settings["versionId"] = installed.VersionId;
        settings["loader"] = loader;
        if (record["settings"] is null)
            record["settings"] = settings;
        await JsonFiles.WriteAsync(instance.MetadataPath, record, cancellationToken).ConfigureAwait(false);
        return installed;
    }
}