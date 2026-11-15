// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Backups;
using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Installation;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Loaders;
using Rodel.MinecraftLauncher.Core.Packs;
using Rodel.MinecraftLauncher.Core.Runtime;
using Rodel.MinecraftLauncher.Core.Worlds;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>Persists and executes typed application requests without a command host. Start/Resume run in the calling process.</summary>
public sealed class LauncherTaskService(LauncherClient client)
{
    private readonly ConcurrentDictionary<string, byte> _running = new();

    /// <summary>Persists a request for execution by this process or a host's independent helper.</summary>
    public async Task<JobStatus> QueueAsync(LauncherTaskRequest request, string? clientRequestId = null, CancellationToken cancellationToken = default)
    {
        if (request is LaunchTask launch)
            request = await client.CreateLaunchAsync(launch.InstanceId, launch.Options, cancellationToken).ConfigureAwait(false);
        if (request is MultiplayerTask multiplayer)
            request = new MultiplayerTask(await client.Multiplayer.PrepareAsync(multiplayer.Request, cancellationToken).ConfigureAwait(false));
        var parameters = JsonSerializer.SerializeToNode(request, LauncherTaskJsonContext.Default.LauncherTaskRequest)!.AsObject();
        var resumable = request is InstallInstanceTask or InstallLoaderTask or InstallJavaTask or PrepareMigrationTask or RecoverContentTask or SearchSeedsTask or PrepareSeedRuntimeTask;
        return await client.Jobs.CreateAsync(new JobDefinition("launcher-task", parameters, resumable), ct: cancellationToken, clientRequestId: clientRequestId).ConfigureAwait(false);
    }

    /// <summary>Queues a typed request and schedules its execution in this process.</summary>
    /// <param name="request">The actual workflow to persist and run.</param>
    /// <param name="cancellationToken">Cancels submission; the recorded job has its own execution lifetime.</param>
    /// <returns>The persisted job identity and current submission state.</returns>
    public Task<JobStatus> StartAsync(LauncherTaskRequest request, CancellationToken cancellationToken = default)
        => StartAsync(request, null, cancellationToken);

    /// <summary>Queues a typed tool request using its durable submission identity, then schedules it in this process.</summary>
    /// <param name="request">The workflow to persist and run.</param>
    /// <param name="clientRequestId">Optional durable caller identity for observing a submitted job after lost output.</param>
    /// <param name="cancellationToken">Cancels submission; the job has an independent execution lifetime.</param>
    /// <returns>The persisted job identity and current submission state.</returns>
    internal async Task<JobStatus> StartAsync(LauncherTaskRequest request, string? clientRequestId, CancellationToken cancellationToken)
    {
        var job = await QueueAsync(request, clientRequestId, cancellationToken).ConfigureAwait(false);
        if (job.Status == "queued") Schedule(job.Id!);
        return job;
    }

    /// <summary>Creates and schedules a resumable attempt, or returns an already active or successful task unchanged.</summary>
    public async Task<JobStatus> ResumeAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var job = await client.Jobs.ResumeAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job.Status == "queued") Schedule(job.Id!);
        return job;
    }

    public Task<JsonNode?> RunAsync(string jobId, CancellationToken cancellationToken = default) =>
        client.Jobs.RunAsync(jobId, (definition, token) => client.ExecuteTaskScopedAsync(jobId, inner => ExecuteAsync(definition, inner), token), cancellationToken);

    /// <summary>Reads a typed task for inspection or a host's independent worker.</summary>
    public LauncherTaskRequest GetRequest(string jobId)
    {
        var request = client.Jobs.Get(jobId).Request ?? throw new InvalidDataException("The job has no request.");
        if (request.Operation != "launcher-task") throw new ArgumentException("This job belongs to another executor.");
        return JsonSerializer.Deserialize(request.Parameters, LauncherTaskJsonContext.Default.LauncherTaskRequest)
            ?? throw new InvalidDataException("The task request is missing.");
    }

    private void Schedule(string id)
    {
        if (!_running.TryAdd(id, 0)) return;
        _ = Task.Run(async () =>
        {
            try { await RunAsync(id).ConfigureAwait(false); }
            catch (Exception) { /* JobService persists operation failures; a detached caller reads the job state. */ }
            finally { _running.TryRemove(id, out _); }
        });
    }

    private async Task<JsonNode?> ExecuteAsync(JobDefinition definition, CancellationToken cancellationToken)
    {
        if (definition.Operation != "launcher-task") throw new ArgumentException("This request belongs to another task executor.");
        var request = JsonSerializer.Deserialize(definition.Parameters, LauncherTaskJsonContext.Default.LauncherTaskRequest)
            ?? throw new InvalidDataException("The task request is missing.");
        var managedInstance = request switch
        {
            CopyInstanceTask task => task.InstanceId,
            CreateBackupTask task => task.InstanceId,
            RestoreBackupTask task => task.InstanceId,
            ExportInstanceTask task => task.InstanceId,
            RemoveInstanceTask task => task.InstanceId,
            _ => null,
        };
        if (managedInstance is not null) client.EnsureInstanceIdle(managedInstance);
        return request switch
        {
            MultiplayerTask task => JsonSerializer.SerializeToNode(await client.Multiplayer.RunAsync(task.Request, cancellationToken).ConfigureAwait(false), Multiplayer.MultiplayerJsonContext.Default.MultiplayerRoom),
            LaunchTask task => JsonSerializer.SerializeToNode(await client.LaunchAsync(task.InstanceId, task.Options, cancellationToken).ConfigureAwait(false), LaunchJsonContext.Default.ILaunchResult),
            InstallInstanceTask task => JsonSerializer.SerializeToNode(await client.InstallInstanceAsync(task.InstanceId, task.Options, cancellationToken).ConfigureAwait(false), InstallationJsonContext.Default.IInstallationResult),
            InstallLoaderTask task => JsonSerializer.SerializeToNode(await client.InstallLoaderAsync(task.InstanceId, task.Loader, task.Options, cancellationToken).ConfigureAwait(false), LoaderJsonContext.Default.LoaderInstallationResult),
            ChangeRuntimeTask task => JsonSerializer.SerializeToNode(await client.Runtimes.ChangeAsync(task.InstanceId, task.Options, cancellationToken).ConfigureAwait(false), RuntimeJsonContext.Default.RuntimeChangeResult),
            PreparePackWorkflowTask task => JsonSerializer.SerializeToNode(await client.PreparePackAsync(task.InstanceId, task.Options, task.Archive, cancellationToken).ConfigureAwait(false), PackJsonContext.Default.PackState),
            PrepareMigrationTask task => JsonSerializer.SerializeToNode(await client.Runtimes.PrepareMigrationAsync(task.InstanceId, cancellationToken).ConfigureAwait(false), RuntimeJsonContext.Default.RuntimePreparationResult),
            RecoverContentTask task => await client.RecoverContentAsync(task.InstanceId, cancellationToken).ConfigureAwait(false),
            InstallContentTask task => JsonSerializer.SerializeToNode(await client.InstallContentAsync(task.InstanceId, task.ProjectId, task.Options, cancellationToken).ConfigureAwait(false), ContentJsonContext.Result),
            RepairDependenciesTask task => JsonSerializer.SerializeToNode(await client.ApplyDependencyRepairAsync(task.InstanceId, task.ReviewId, cancellationToken).ConfigureAwait(false), ContentJsonContext.Result),
            ApplyContentReviewTask task => JsonSerializer.SerializeToNode(await client.ApplyContentReviewAsync(task.InstanceId, task.ReviewId, cancellationToken).ConfigureAwait(false), ContentJsonContext.Result),
            UpdateContentTask task => JsonSerializer.SerializeToNode(await client.UpdateContentAsync(task.InstanceId, task.Files, task.Options, cancellationToken).ConfigureAwait(false), ContentJsonContext.Result),
            ImportPackTask task => JsonSerializer.SerializeToNode(await client.ImportPackAsync(task.Archive, task.Options, cancellationToken).ConfigureAwait(false), PackJsonContext.Default.PackState),
            CompletePackTask task => JsonSerializer.SerializeToNode(await client.Packs.CompleteAsync(task.InstanceId, task.Archive, task.Download, task.ManualDirectory, cancellationToken).ConfigureAwait(false), PackJsonContext.Default.PackReview),
            UpdatePackTask task => JsonSerializer.SerializeToNode(await client.UpdatePackAsync(task.InstanceId, task.Archive, task.Options, cancellationToken).ConfigureAwait(false), PackJsonContext.Default.PackReview),
            ApplyPackUpdateTask task => JsonSerializer.SerializeToNode(await client.ApplyPackUpdateAsync(task.OperationId, task.Options, cancellationToken).ConfigureAwait(false), PackJsonContext.Default.PackReview),
            ReviewPackDistributionTask task => JsonSerializer.SerializeToNode(await client.Packs.ReviewDistributionAsync(task.InstanceId, cancellationToken).ConfigureAwait(false), PackJsonContext.Default.PackReview),
            ReviewPackUpdateTask task => JsonSerializer.SerializeToNode(await client.Packs.ReviewAsync(task.InstanceId, task.Archive, !task.Options.Offline, task.Options.ManualDirectory,
                task.Options.OptionalSelection, cancellationToken: cancellationToken).ConfigureAwait(false), PackJsonContext.Default.PackReview),
            RollbackPackUpdateTask task => JsonSerializer.SerializeToNode(await client.Packs.RollbackAsync(task.OperationId, cancellationToken).ConfigureAwait(false), PackJsonContext.Default.PackResult),
            InstallOnlinePackTask task => JsonSerializer.SerializeToNode(await client.InstallOnlinePackAsync(task.ProjectId, task.Options, cancellationToken).ConfigureAwait(false), PackJsonContext.Default.PackResult),
            UpdateOnlinePackTask task => JsonSerializer.SerializeToNode(await client.UpdateOnlinePackAsync(task.InstanceId, task.Options, cancellationToken).ConfigureAwait(false), PackJsonContext.Default.PackResult),
            ReviewOnlinePackTask task => JsonSerializer.SerializeToNode(await client.ReviewOnlinePackAsync(task.InstanceId, task.Options, cancellationToken).ConfigureAwait(false), PackJsonContext.Default.PackResult),
            ImportInstanceTask task => JsonSerializer.SerializeToNode(await client.ImportInstanceAsync(task.Name, task.Directory, task.Options, cancellationToken).ConfigureAwait(false), InstanceJsonContext.Default.IInstanceImportResult),
            InstallJavaTask task => JsonSerializer.SerializeToNode(await client.InstallJavaAsync(component: task.Component, architecture: task.Architecture, cancellationToken: cancellationToken, expectedVersion: task.ExpectedVersion).ConfigureAwait(false), JavaOperationJsonContext.Default.JavaRegistrationResult),
            ImportJavaTask task => JsonSerializer.SerializeToNode(await client.JavaRuntimes.ImportAsync(task.Source, task.Id, cancellationToken).ConfigureAwait(false), JavaOperationJsonContext.Default.JavaRegistrationResult),
            RemoveJavaTask task => JsonSerializer.SerializeToNode(await client.JavaRuntimes.RemoveAsync(task.Id, cancellationToken).ConfigureAwait(false), JavaOperationJsonContext.Default.JavaRemovalResult),
            CopyInstanceTask task => JsonSerializer.SerializeToNode(await client.CopyInstanceAsync(task.InstanceId, task.Name, cancellationToken).ConfigureAwait(false), InstanceJsonContext.Default.InstanceSummary),
            CreateBackupTask task => JsonSerializer.SerializeToNode(await client.Backups.CreateAsync(task.InstanceId, excludeRedownloadable: false, cancellationToken: cancellationToken).ConfigureAwait(false), BackupJsonContext.Default.BackupManifest),
            RestoreBackupTask task => JsonSerializer.SerializeToNode(await client.Backups.RestoreAsync(task.BackupId, task.InstanceId, cancellationToken).ConfigureAwait(false), BackupJsonContext.Default.BackupRestoreResult),
            ExportInstanceTask task => JsonSerializer.SerializeToNode(await client.Packs.ExportAsync(task.InstanceId, task.Destination, includeLocalMods: true, cancellationToken: cancellationToken).ConfigureAwait(false), PackJsonContext.Default.PackExportResult),
            RemoveInstanceTask task => JsonSerializer.SerializeToNode(await client.RemoveInstanceAsync(task.InstanceId, task.DeleteFiles, cancellationToken).ConfigureAwait(false), ApplicationJsonContext.Default.InstanceRemovalResult),
            ManageWorldTask task => JsonSerializer.SerializeToNode(await client.ManageWorldAsync(task.InstanceId, task.Action, task.World, task.Source, task.Destination, cancellationToken).ConfigureAwait(false), WorldJsonContext.Default.WorldOperationResult),
            MapSeedTask task => JsonSerializer.SerializeToNode(await client.Seeds.MapAsync(task.Query, cancellationToken).ConfigureAwait(false), Rodel.Minecraft.Seeds.SeedJsonContext.Default.SeedMapTile),
            SearchSeedsTask task => JsonSerializer.SerializeToNode(await client.SearchSeedsAsync(task.SearchId, cancellationToken).ConfigureAwait(false), Worlds.Seeds.SeedStoreJsonContext.Default.SeedSearchState),
            PrepareSeedRuntimeTask task => JsonSerializer.SerializeToNode(await client.PrepareSeedRuntimeAsync(task.Version, cancellationToken).ConfigureAwait(false), Rodel.Minecraft.Seeds.SeedJsonContext.Default.SeedRuntimeProfile),
            _ => throw new InvalidDataException("Unknown launcher task type.")
        };
    }
}