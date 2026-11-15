// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Backups;
using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Installation;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Loaders;
using Rodel.MinecraftLauncher.Core.Packs;
using Rodel.MinecraftLauncher.Core.Runtime;
using Rodel.MinecraftLauncher.Core.Servers;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>Direct application API shared by desktop hosts and protocol adapters. No command parser or executable is required.</summary>
/// <remarks>Services use the same data root. Progress callbacks run on the operation thread; UI hosts can supply Progress&lt;LauncherProgress&gt;.</remarks>
public sealed partial class LauncherClient(LauncherPaths paths, ISessionHost? sessionHost = null, IProgress<LauncherProgress>? progress = null)
{
    private readonly ISessionHost _sessionHost = sessionHost ?? new InProcessSessionHost();
    private readonly AsyncLocal<string?> _activeTask = new();
    private readonly AsyncLocal<PackPreparation?> _packPreparation = new();
    private readonly ConcurrentDictionary<string, long> _progressTicks = new();
    private readonly ConcurrentDictionary<string, string> _progressStages = new();
    private readonly ConcurrentDictionary<string, (int Completed, int Shared, int? Total)> _installationCounts = new();
    public LauncherTaskService Tasks => LazyInitializer.EnsureInitialized(ref field, () => new LauncherTaskService(this));
    public LauncherPaths Paths => paths;
    public InstanceStore Instances { get; } = new(paths);
    public SettingsService Settings => new(paths);
    public IdentityService Identities => new(paths);
    public RuntimeService Runtimes => new(paths, InstallationEvents, LoaderEvents);
    public InstallationService Installation => new(paths);
    /// <summary>Provides the data-root room lifecycle, protected invitations and game-bound automatic LAN publication.</summary>
    public Multiplayer.MultiplayerService Multiplayer => LazyInitializer.EnsureInitialized(ref field, () => new Multiplayer.MultiplayerService(this));
    public LoaderService Loaders => new(paths);
    public JavaDiscovery Java => new(paths);
    public JavaRuntimeService JavaRuntimes => new(paths);
    public ContentService Content => new(paths);
    /// <summary>Provides instance screenshot inventories and non-overwriting file management without requiring a stopped game.</summary>
    public Screenshots.ScreenshotService Screenshots => new(paths);
    public ContentPlatformService ContentPlatform => new(paths);
    public PackService Packs => new(paths, progress: DownloadEvents);
    public OnlinePackService OnlinePacks => new(paths);
    public GameSettingsService GameSettings => new(paths);
    public BackupService Backups => new(paths);
    /// <summary>Provides durable skin editing and candidate-review APIs shared with CLI/MCP.</summary>
    public Skins.SkinService Skins => new(paths);
    /// <summary>Provides exact-version original seed maps, bounded searches and persistent exploration data.</summary>
    public Worlds.Seeds.SeedExplorerService Seeds => new(paths);
    /// <summary>Provides read-only instance-world maps and saved/live player provenance through the same Core used by CLI and App.</summary>
    public Worlds.Maps.WorldMapService WorldMaps => new(paths);
    public ServerService Servers => new(paths);
    public ServerConnectionService ServerConnections => new(paths);
    public SessionService Sessions => new(paths);
    /// <summary>Provides typed observations and guarded local edits through the selected game's actual monitor.</summary>
    public GameBridge.GameBridgeService GameBridge => new(paths);
    public LogService Logs => new(paths);
    public LogEventService LogEvents => new(paths);
    public JobService Jobs => new(paths);
    public LauncherActivityLog Activity => new(paths);
    public InstallationRecoveryService InstallationRecovery => new(this);
    public MaintenanceService Maintenance => new(paths);
    public CacheService Cache => new(paths);
    public AI.AssistantService Assistant => new(paths);
    /// <summary>Provides incident-scoped reversible repair and verification using this client's real session host.</summary>
    public AI.AiRepairService Repairs => new(this);
    /// <summary>Controls App-owned foreground state through an explicitly selected live host, not package-file editing.</summary>
    public AppControlService AppControl => new(paths);

    /// <summary>Rejects conflicting game or task activity with its observed reason; this check does not cancel work or replace transaction-time protection.</summary>
    /// <param name="instanceId">The registered instance whose files are about to be inspected for mutation.</param>
    /// <param name="allowOnlineContent">Allows independent frozen online content tasks, which retain their own commit-time drift checks.</param>
    public void EnsureInstanceIdle(string instanceId, bool allowOnlineContent = false)
    {
        var instance = Instances.Get(instanceId);
        if (Sessions.HasActiveGame(instance.GameDirectory))
        {
            throw new LauncherOperationException("INSTANCE_BUSY", "This instance's game is still active. Exit the game before changing its files.",
                JsonSerializer.SerializeToNode(new InstanceActivityConflict("game"), ApplicationJsonContext.Default.InstanceActivityConflict));
        }

        var blockingJob = Jobs.List().FirstOrDefault(job => job.Id != _activeTask.Value
                && job.Request?.Parameters?["instanceId"]?.ToString() == instanceId
                && (job.Status is "queued" or "running" || job.ActivityUnknown == true)
                && !(allowOnlineContent && job.ActivityUnknown != true && IsOnlineContentTask(job, instanceId)));
        if (blockingJob is not null)
        {
            var reason = blockingJob.ActivityUnknown == true ? "unknown-task"
                : IsOnlineContentTask(blockingJob, instanceId) ? "content-download" : "task";
            var message = reason switch
            {
                "unknown-task" => "The activity of an instance task could not be verified. Inspect that task before changing files.",
                "content-download" => "This instance has an active content download or installation. Wait for it to finish or cancel that task.",
                _ => "This instance has an active launcher task. Wait for it to finish or cancel that task.",
            };
            throw new LauncherOperationException("INSTANCE_BUSY", message,
                JsonSerializer.SerializeToNode(new InstanceActivityConflict(reason, blockingJob.Id), ApplicationJsonContext.Default.InstanceActivityConflict));
        }
    }

    private bool IsOnlineContentTask(JobStatus job, string instanceId)
    {
        if (job.Request is not { Operation: "launcher-task", Parameters: { } parameters } || parameters["kind"]?.ToString() != "apply-content-review"
            || parameters["reviewId"]?.ToString() is not { } reviewId)
        {
            return false;
        }

        try
        {
            var review = JsonFiles.ReadSnapshot(ContentReviewPath(reviewId), stream => JsonSerializer.Deserialize(stream, ContentJsonContext.Review));
            return review is { Mode: "online", OnlineBaseline: not null } && review.InstanceId == instanceId;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Copies an instance with the same progress and cancellation contract used by typed tasks.</summary>
    public async Task<InstanceSummary> CopyInstanceAsync(string instanceId, string name, CancellationToken cancellationToken = default)
    {
        using var cacheUse = await CacheActivity.EnterAsync(paths, cancellationToken).ConfigureAwait(false);
        var result = await Instances.CopyAsync(instanceId, name, new InlineProgress<LauncherProgress>(value => Report(value)), cancellationToken).ConfigureAwait(false);
        await Activity.WriteAsync("instance.copied", result.Id, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
        return result.Describe();
    }

    /// <summary>Removes registration and its backups, optionally deleting owned game files and private runtime metadata while preserving shared dependencies.</summary>
    public async Task<InstanceRemovalResult> RemoveInstanceAsync(string instanceId, bool deleteFiles = false, CancellationToken cancellationToken = default)
    {
        await Instances.RemoveAsync(instanceId, deleteFiles, cancellationToken).ConfigureAwait(false);
        await Activity.WriteAsync("instance.removed", instanceId, code: deleteFiles ? "FILES" : "REGISTRATION", cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
        return new(instanceId, deleteFiles);
    }

    /// <summary>Reads the build identity and dynamic-code capability of this process.</summary>
    public static LauncherVersion GetVersion() => new("Rodel.MinecraftLauncher", "0.1.0-dev", System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported);
    /// <summary>Reads the selected runtime's content compatibility evidence, retaining manual-profile fallback advice.</summary>
    public Task<ContentCompatibility> GetContentContextAsync(string instanceId, string? minecraftVersion = null, string? loader = null, CancellationToken ct = default)
    {
        var instance = Instances.Get(instanceId);
        return ContentCompatibilityContext.ResolveAsync(instance, Settings.GetEffective(instance).Values, minecraftVersion, loader, ct);
    }

    /// <summary>Lists readable registrations and reports each unreadable registration separately.</summary>
    public IReadOnlyList<InstanceSummary> ListInstances()
    {
        var unreadable = new List<InstanceSummary>();
        var known = Instances.List((file, exception) => unreadable.Add(new() { Id = Path.GetFileName(Path.GetDirectoryName(file))!, Status = "unreadable", MetadataPath = file, Error = LauncherOperationException.ToResult(exception)["error"]!.DeepClone() }));
        return [.. known.Select(instance => instance.Describe()), .. unreadable];
    }

    /// <summary>Reads installed resource packs and the game's real selection without changing settings.</summary>
    public Task<ResourcePackSelectionSummary> GetResourcePacksAsync(string instanceId, string? encoding = null, CancellationToken cancellationToken = default) => ReadResourcePacksAsync(instanceId, encoding, cancellationToken);
    public Task<ResourcePackSelectionResult> SelectResourcePacksAsync(string instanceId, ResourcePackSelectionOptions options, CancellationToken cancellationToken = default) => ResourcePacksAsync(instanceId, options, cancellationToken);
    internal async Task<JsonNode?> ExecuteTaskScopedAsync(string id, Func<CancellationToken, Task<JsonNode?>> execute, CancellationToken cancellationToken)
    {
        var previous = _activeTask.Value;
        _activeTask.Value = id;
        string? instanceId = null;
        try { instanceId = Jobs.Get(id).Request?.Parameters?["instanceId"]?.ToString(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }

        using var logScope = LauncherActivityLog.Scope(id, instanceId);
        try
        {
            Report(new LauncherProgress { Stage = "started" }, true);
            var result = await execute(cancellationToken).ConfigureAwait(false);
            Report(new LauncherProgress { Stage = "completed" }, true);
            return result;
        }
        catch (Exception exception)
        {
            Report(new LauncherProgress { Stage = exception is OperationCanceledException ? "canceled" : "failed" }, true);
            throw;
        }
        finally
        {
            _activeTask.Value = previous;
            _progressTicks.TryRemove(id, out _);
            _progressStages.TryRemove(id, out _);
            _installationCounts.TryRemove(id, out _);
        }
    }

    private void Report(LauncherProgress data, bool force = false)
    {
        if (_activeTask.Value is { } id)
        {
            if (data.CompletedFiles is { } completed)
            {
                _installationCounts[id] = (completed, data.SharedReusedFiles ?? 0, data.TotalFiles);
            }
            else if (_installationCounts.TryGetValue(id, out var counts))
            {
                data = data with { CompletedFiles = counts.Completed, SharedReusedFiles = counts.Shared, TotalFiles = counts.Total, IncludesFileTotal = true };
            }

            var stage = data.Stage ?? "unknown";
            var changed = !_progressStages.TryGetValue(id, out var previousStage) || previousStage != stage;
            var now = Environment.TickCount64;
            var fileActivityChanged = changed && (stage is "download" or "pack-extract" or "pack-verify")
                && (previousStage is "download" or "pack-extract" or "pack-verify");
            if (!force && (!changed || fileActivityChanged) && now - _progressTicks.GetOrAdd(id, 0) < 250) return;
            _progressStages[id] = stage;
            if (changed && !Activity.WriteAsync("task.stage", jobId: id, stage: stage).GetAwaiter().GetResult())
                data = data with { ActivityLogWarning = "ACTIVITY_LOG_UNAVAILABLE" };
            _progressTicks[id] = now;
            data = data with { JobId = id, Event = "progress", Time = DateTimeOffset.UtcNow.ToString("O") };
            if (_packPreparation.Value is { } preparation)
                data = data with { PackPreparation = preparation.Snapshot() };
            var file = Path.Combine(paths.JobsDirectory, id + ".progress");
            try
            {
                using var lease = ResourceLease.AcquireAsync(resource: file).GetAwaiter().GetResult();
                JsonFiles.WriteAsync(file, JsonSerializer.SerializeToNode(data, ApplicationJsonContext.Progress)!).GetAwaiter().GetResult();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Progress is ancillary; losing it must not undo a successful installation or prevent launch.
                data = data with { ProgressWarning = exception.Message };
            }
        }

        else if (_packPreparation.Value is { } preparation)
        {
            data = data with { PackPreparation = preparation.Snapshot() };
        }

        progress?.Report(data);
    }
    private IProgress<InstallationProgress> InstallationEvents => new InlineProgress<InstallationProgress>(value => Report(new LauncherProgress { Stage = value.Stage, Path = value.Path, DisplayName = value.DisplayName, Bytes = value.Bytes, TotalBytes = value.TotalBytes, OverallPercent = value.OverallPercent ?? (value.TotalFiles is > 0 and var total ? Math.Clamp(value.CompletedFiles * 100d / total, 0d, 100d) : null), Reused = value.Reused, CompletedFiles = value.CompletedFiles, TotalFiles = value.TotalFiles, SharedReusedFiles = value.SharedReusedFiles, IncludesByteTotal = true, IncludesFileTotal = true, IncludesDisplayName = true }));
    private IProgress<DownloadProgress> DownloadEvents => new InlineProgress<DownloadProgress>(value => Report(new LauncherProgress { Stage = value.Stage ?? "download", SourceUri = value.SourceUri, Path = value.Path, Bytes = value.Bytes, TotalBytes = value.TotalBytes, OverallPercent = value.OverallPercent, Reused = value.Reused, IncludesByteTotal = true, IncludesSourceUri = true }));
    private IProgress<LoaderProgress> LoaderEvents => new InlineProgress<LoaderProgress>(value => Report(new LauncherProgress { Stage = value.Stage, Message = value.Message, OverallPercent = value.OverallPercent, ProcessorIndex = value.ProcessorIndex, ProcessorCount = value.ProcessorCount, IncludesProcessors = true }));

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}