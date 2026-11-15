// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Imaging;
using Rodel.MinecraftLauncher.App.Localization;
using Rodel.MinecraftLauncher.Core.Application;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Tasks;

/// <summary>Observes owned typed tasks without introducing another executor or scheduler.</summary>
internal sealed class FeatureTaskController(LauncherClient client, HomeController home) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    internal event Action? Changed;
    internal bool Active { get; private set; }
    internal bool CancelRequested { get; private set; }
    internal JobStatus? Job { get; private set; }
    internal string Error { get; private set; } = string.Empty;
    internal string Outcome { get; private set; } = string.Empty;
    internal string Status => Active
        ? CancelRequested ? Resources.LaunchCanceling : Job?.Progress?.Stage switch
        {
            "metadata" => Resources.CreateStageMetadata,
            "base" => Resources.RuntimePreparingBase,
            "profile" => Resources.RuntimePreparingProfile,
            "installer" => Resources.RuntimePreparingInstaller,
            "processor" or "processor-reused" or "processor-fallback" => Resources.RuntimeProcessor,
            "library" or "library-mirror" or "library-fallback" or "native" => Resources.CreateStageLibraries,
            "asset" or "assetIndex" or "logging" => Resources.CreateStageAssets,
            "completed" => Resources.RuntimeSelecting,
            "java-discovery" => Resources.CreateJavaDiscovery,
            "java-install" => Resources.CreateJavaInstalling,
            "pack-extract" => Resources.PackExtracting,
            "pack-verify" => Resources.PackVerifying,
            "download" when Job?.Progress?.Reused == true => Resources.FeatureTaskReusing,
            "download" => Resources.FeatureTaskDownloading,
            _ => Resources.FeatureTaskWorking,
        }
        : Outcome == "succeeded"
        ? Resources.FeatureTaskSucceeded
        : Outcome == "canceled"
        ? Resources.FeatureTaskCanceled
        : Outcome.Length > 0 ? Resources.FeatureTaskFailed : string.Empty;
    internal string Report => new JsonObject
    {
        ["job"] = JsonSerializer.SerializeToNode(Job, ApplicationJsonContext.Job),
        ["error"] = Error
    }.ToJsonString(new() { WriteIndented = true });
    private string ProgressValue => Job?.Progress is not { } progress
        ? string.Empty
        : progress.ProcessorCount is { } processors && progress.ProcessorIndex is { } step
        ? string.Format(Resources.FeatureTaskProcessorProgress, step, processors)
        : progress.Stage is not ("download" or "java-install" or "java-discovery") && progress.TotalFiles is { } assetFiles
        ? string.Format(Resources.FeatureTaskFileProgress, progress.CompletedFiles ?? 0, assetFiles)
        : progress.TotalBytes is > 0 and var bytes
        ? string.Format(Resources.ContentDownloadProgress, AppImageCache.FormatSize(progress.Bytes ?? 0), AppImageCache.FormatSize(bytes))
        : progress.TotalFiles is { } files ? string.Format(Resources.FeatureTaskFileProgress, progress.CompletedFiles ?? 0, files) : string.Empty;
    internal string Progress => Job?.Progress is { } progress && progress.TotalFiles is > 0 && progress.Stage is not ("download" or "java-install" or "java-discovery")
        ? ProgressValue
        : Job?.Progress?.DisplayName is { Length: > 0 } name
        ? string.Format(Resources.FeatureTaskFileDetail, name, ProgressValue)
        : Job?.Progress?.Path is { Length: > 0 } path ? string.Format(Resources.FeatureTaskFileDetail, Path.GetFileName(path), ProgressValue) : ProgressValue;

    internal async Task RunAsync(LauncherTaskRequest request, string? instanceId = null, bool refreshHome = true)
    {
        if (Active || _lifetime.IsCancellationRequested)
            return;
        if (instanceId is not null && home.State.PendingInstances.Contains(instanceId))
        {
            Error = Resources.InstanceTaskBusy;
            Outcome = "failed";
            Changed?.Invoke();
            return;
        }

        Active = true;
        CancelRequested = false;
        Job = null;
        Error = string.Empty;
        Outcome = string.Empty;
        using var ownedWork = home.TrackManagementWork();
        if (instanceId is not null)
            home.SetManagementBusy(instanceId, true);
        Changed?.Invoke();
        try
        {
            if (instanceId is not null)
            {
                await Task.Run(() => client.EnsureInstanceIdle(instanceId), _lifetime.Token);
            }

            Job = await Task.Run(() => client.Tasks.StartAsync(request, _lifetime.Token));
            Changed?.Invoke();
            if (CancelRequested)
                await CancelAsync();
            var id = Job.Id!;
            while (!_lifetime.IsCancellationRequested)
            {
                try
                {
                    Job = await Task.Run(() => client.Jobs.Get(id), _lifetime.Token);
                    Error = string.Empty;
                    var status = Job.Status;
                    if (status is "succeeded" or "failed" or "canceled" or "interrupted")
                    {
                        Outcome = status;
                        Error = status == "canceled" ? string.Empty
                            : LauncherErrorText.Describe(Job.Failure, Job.Failure?["error"]?["message"]?.ToString() ?? Job.Error ?? string.Empty);
                        break;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    // An observation error does not mean that an owned task stopped.
                    Error = LauncherErrorText.Describe(exception);
                }

                Changed?.Invoke();
                await Task.Delay(250, _lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Error = exception.Message;
            Outcome = "failed";
        }
        finally
        {
            Active = false;
            CancelRequested = false;
            if (instanceId is not null)
                home.SetManagementBusy(instanceId, false);
            if (!_lifetime.IsCancellationRequested)
            {
                Changed?.Invoke();
                if (refreshHome)
                    await home.RefreshAsync();
            }
        }
    }

    internal async Task CancelAsync()
    {
        if (!Active || _lifetime.IsCancellationRequested)
            return;
        CancelRequested = true;
        Changed?.Invoke();
        if (Job?.Id is not { } id)
            return;
        try
        {
            await client.Jobs.CancelAsync(id, _lifetime.Token);
        }
        catch (Exception exception)
        {
            CancelRequested = false;
            Error = exception.Message;
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}