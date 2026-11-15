// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Installation;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Loaders;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Runtime;

/// <summary>Prepares runtimes and changes selections without depending on CLI or MCP syntax.</summary>
/// <remarks>Progress callbacks run on the operation thread. UI callers should supply Progress&lt;T&gt; to marshal updates.</remarks>
public sealed class RuntimeService(LauncherPaths paths, IProgress<InstallationProgress>? installationProgress = null, IProgress<LoaderProgress>? loaderProgress = null)
{
    private readonly InstanceStore _instances = new(paths);
    private readonly RuntimeSelectionStore _selection = new(paths);

    /// <summary>Reads recognized runtime metadata, retaining fallback advice for manually configured instances.</summary>
    public async Task<RuntimeStatus> ShowAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        var instance = _instances.Get(instanceId);
        var settings = new SettingsService(paths).GetEffective(instance).Values;
        var current = settings["versionId"]?.ToString() ?? instance.VersionId ?? throw new ArgumentException("No selected runtime version.");
        var context = await GetContextAsync(instance, cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await RuntimeProfile.DescribeAsync(instance.MinecraftDirectory, current, cancellationToken).ConfigureAwait(false);
            return new ResolvedRuntimeStatus(instance.Id, result);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            return new FallbackRuntimeStatus(instance.Id, context.LaunchVersion,
                context.MinecraftVersion, context.Loader, context.Source,
                context.Warnings, ex.Message, context.LoaderSource);
        }
    }

    /// <summary>Plans or installs a runtime, then commits its selection against the original snapshot.</summary>
    public async Task<RuntimeChangeResult> ChangeAsync(string instanceId, RuntimeChangeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var cacheUse = await CacheActivity.EnterAsync(paths, cancellationToken).ConfigureAwait(false);
        var instance = _instances.Get(instanceId);
        var settings = new SettingsService(paths).GetEffective(instance).Values;
        var current = settings["versionId"]?.ToString() ?? instance.VersionId ?? throw new ArgumentException("No selected runtime version.");
        var context = await GetContextAsync(instance, cancellationToken).ConfigureAwait(false);
        var minecraft = options.MinecraftVersion ?? context.MinecraftVersion ?? current;
        var loader = options.RemoveLoader ? null : options.Loader;
        if (loader is "none" or "vanilla")
            loader = null;
        var desired = options.VersionId ?? (loader is null ? minecraft : options.LoaderVersion is { } lv ? $"{minecraft}-{loader}-{lv}" : null);
        var warnings = new List<string> { "Runtime changes retain local mods, game settings and worlds; compatibility advice does not block launch." };
        var resultPlan = new RuntimeChangeResult
        {
            InstanceId = instance.Id,
            PreviousVersion = current,
            MinecraftVersion = minecraft,
            Loader = loader,
            LoaderVersion = options.LoaderVersion,
            TargetVersion = desired,
            State = "planned",
            Warnings = warnings
        };
        if (options.PlanOnly)
            return resultPlan;
        var baseline = RuntimeSelectionStore.Snapshot(instance.MetadataPath);
        if (new SessionService(paths).HasActiveGame(instance.GameDirectory))
            throw new IOException("This runtime cannot be changed while its game is active; retry after exit.");
        if (options.Offline)
        {
            if (desired is null)
                throw new ArgumentException("Select an exact --loader-version or --version for an offline runtime change.");
            await new VersionResolver().ResolveAsync(instance.MinecraftDirectory, desired, cancellationToken).ConfigureAwait(false);
        }
        else if (options.VersionId is null && loader is not null)
        {
            var installed = await new LoaderService(paths, libraryMirrors: options.LibraryMirrors).InstallAsync(instance.MinecraftDirectory, minecraft, loader, options.LoaderVersion, options.JavaPath, loaderProgress, cancellationToken).ConfigureAwait(false);
            desired = installed.VersionId;
        }
        else
        {
            desired ??= minecraft;
            await new InstallationService(paths).InstallAsync(instance.MinecraftDirectory, desired, progress: installationProgress, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var committed = await _selection.CommitAsync(instance.Id, desired!, baseline, "runtime-change", false, cancellationToken).ConfigureAwait(false);
        resultPlan.TargetVersion = desired;
        resultPlan.State = "applied";
        resultPlan.OperationId = committed.OperationId;
        resultPlan.Target = committed.Target;
        warnings.AddRange(committed.Warnings);
        return resultPlan;
    }

    /// <summary>Lists recoverable selection operations; unreadable records are reported independently.</summary>
    public RuntimeOperationList GetOperations(string instanceId) => _selection.List(_instances.Get(instanceId).Id);

    /// <summary>Reads one operation and compares its snapshot with the current selection.</summary>
    public RuntimeOperationSummary GetOperation(string instanceId, string operationId) => _selection.Get(_instances.Get(instanceId).Id, operationId);

    /// <summary>Restores only runtime-owned settings, retaining unrelated user edits.</summary>
    public Task<RuntimeRollbackResult> RollbackAsync(string instanceId, string operationId, CancellationToken cancellationToken = default) => _selection.RollbackAsync(instanceId, operationId, cancellationToken);
    private Task<ContentCompatibility> GetContextAsync(InstanceRecord instance, CancellationToken cancellationToken) => ContentCompatibilityContext.ResolveAsync(instance, new SettingsService(paths).GetEffective(instance).Values, null, null, cancellationToken);

    /// <summary>Installs a pack's declared dependencies and selects the prepared runtime.</summary>
    public async Task<RuntimePreparationResult> PreparePackAsync(string instanceId, IEnumerable<string>? libraryMirrors = null, CancellationToken cancellationToken = default)
    {
        var instance = _instances.Get(instanceId);
        var pack = JsonFiles.ReadObject(Path.Combine(instance.GameDirectory, ".rodel", "pack-lock.json"));
        var baseline = RuntimeSelectionStore.Snapshot(instance.MetadataPath);
        var result = await PrepareDependenciesAsync(instance, pack["dependencies"]!.AsObject(), baseline, "pack-prepare", false, libraryMirrors, cancellationToken).ConfigureAwait(false);
        result.SelectionSource = "pack-dependencies";
        return result;
    }

    /// <summary>Prepares a migrated instance while retaining its migration dependency declarations.</summary>
    public async Task<RuntimePreparationResult> PrepareMigrationAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        var instance = _instances.Get(instanceId);
        var baseline = RuntimeSelectionStore.Snapshot(instance.MetadataPath);
        var dependencies = new SettingsService(paths).GetEffective(instance).Values["migrationRuntime"] as JsonObject ?? throw new ArgumentException("No recognized migration runtime. Use install/loader install for manually configured instances.");
        var result = await PrepareDependenciesAsync(instance, dependencies, baseline, "migration-prepare", true, null, cancellationToken).ConfigureAwait(false);
        result.InstanceId = instanceId;
        return result;
    }

    private async Task<RuntimePreparationResult> PrepareDependenciesAsync(InstanceRecord instance, JsonObject dependencies, JsonObject baseline, string source, bool preserveMigration, IEnumerable<string>? libraryMirrors, CancellationToken cancellationToken)
    {
        using var cacheUse = await CacheActivity.EnterAsync(paths, cancellationToken).ConfigureAwait(false);
        if (new SessionService(paths).HasActiveGame(instance.GameDirectory))
            throw new IOException("Retry runtime preparation after the game exits.");
        var minecraft = dependencies["minecraft"]!.GetValue<string>();
        var loaderCount = dependencies.Count(item => item.Key is "fabric-loader" or "quilt-loader" or "forge" or "neoforge");
        var work = new RuntimePreparationProgress(installationProgress, loaderProgress, loaderCount + 1);
        await new InstallationService(paths).InstallAsync(instance.MinecraftDirectory, minecraft, progress: work, cancellationToken: cancellationToken).ConfigureAwait(false);
        work.CompletePhase();
        var selected = minecraft;
        var warnings = new List<string>();
        foreach (var dependency in dependencies.Where(x => x.Key != "minecraft"))
        {
            var loader = dependency.Key switch
            {
                "fabric-loader" => "fabric",
                "quilt-loader" => "quilt",
                "forge" => "forge",
                "neoforge" => "neoforge",
                _ => null
            };
            if (loader is null)
            {
                // Preserve the established migration behavior; unknown pack declarations remain visible as advice.
                if (!preserveMigration)
                    warnings.Add("Unknown pack dependency retained: " + dependency.Key);
                continue;
            }

            var installed = await new LoaderService(paths, libraryMirrors: libraryMirrors).InstallAsync(instance.MinecraftDirectory, minecraft, loader, dependency.Value!.GetValue<string>(), progress: work, cancellationToken: cancellationToken).ConfigureAwait(false);
            work.CompletePhase();
            selected = installed.VersionId;
        }

        var committed = await _selection.CommitAsync(instance.Id, selected, baseline, source, preserveMigration, cancellationToken).ConfigureAwait(false);
        warnings.AddRange(committed.Warnings);
        return new(preserveMigration || warnings.Count == 0, selected, warnings, committed.OperationId, committed.Target);
    }
}