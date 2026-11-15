// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Installation;
using Rodel.MinecraftLauncher.Core.Packs;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    /// <summary>Completes a retained pack and automatically prepares its declared runtime and Java without launching the game.</summary>
    /// <param name="instanceId">The existing pack instance; recovery never imports another instance.</param>
    /// <param name="options">Local files, network permission and content-only selection for this attempt.</param>
    /// <param name="archive">An optional replacement location of the original manifest for missing-file completion.</param>
    /// <param name="cancellationToken">Cancels the current step while retaining published content and completed-step evidence.</param>
    /// <returns>The retained instance, pack results and ordered preparation steps, including skipped content-only steps.</returns>
    public async Task<PackState> PreparePackAsync(string instanceId, PackWorkflowOptions? options = null, string? archive = null, CancellationToken cancellationToken = default)
    {
        EnsureInstanceIdle(instanceId);
        using var cacheUse = await Storage.CacheActivity.EnterAsync(paths, cancellationToken).ConfigureAwait(false);
        return await PreparePackWorkflowAsync(instanceId, archive, options ?? new(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<PackState> PreparePackWorkflowAsync(string? instanceId, string? archive, PackWorkflowOptions options, CancellationToken ct, OnlinePackSelection? onlineSource = null)
    {
        using var cacheUse = await Storage.CacheActivity.EnterAsync(paths, ct).ConfigureAwait(false);
        PackPreparationStep[] steps = [new() { Id = "files" }, new() { Id = "runtime" }, new() { Id = "java" }];
        var preparation = new PackPreparation { InstanceId = instanceId, Steps = steps };
        var previous = _packPreparation.Value;
        _packPreparation.Value = preparation;
        var result = new PackState();
        var current = 0;

        void Begin(int index)
        {
            current = index;
            preparation.ActiveStep = steps[index].Id;
            steps[index].Status = "running";
            if (_activeTask.Value is { } jobId)
                _installationCounts.TryRemove(jobId, out _);
            ReportStep("pack-" + steps[index].Id);
        }

        void ReportStep(string stage)
        {
            Report(new LauncherProgress { Stage = stage }, true);
        }

        void Finish(int index, string status = "completed")
        {
            steps[index].Status = status;
            ReportStep("pack-" + steps[index].Id + "-completed");
        }

        try
        {
            Begin(0);
            ct.ThrowIfCancellationRequested();
            if (instanceId is null && options.ServerId is not null)
                _ = await Servers.GetAsync(options.ServerId, ct).ConfigureAwait(false);
            if (instanceId is null)
            {
                result = await Packs.ImportAsync(archive!, options.Name, !options.Offline, options.ManualDirectory, options.OptionalSelection, onlineSource, ct).ConfigureAwait(false);
                instanceId = result.Instance!.Id;
                preparation.InstanceId = instanceId;
                if (options.ServerId is not null)
                    result.Instance = await Servers.SetClientOwnerAsync(instanceId, options.ServerId, ct: ct).ConfigureAwait(false);
                if (onlineSource is not null)
                    result.OnlineSource = onlineSource;
            }
            else
            {
                result = Packs.GetState(instanceId);
                if (result.Missing.Count > 0)
                {
                    await Packs.CompleteAsync(instanceId, archive, !options.Offline, options.ManualDirectory, ct).ConfigureAwait(false);
                    result = Packs.GetState(instanceId);
                }
            }

            if (result.Missing.Count > 0)
            {
                steps[0].Status = "blocked";
                steps[0].MissingCount = result.Missing.Count;
                preparation.Status = "blocked";
                if (!options.ContentOnly && !options.Offline)
                    throw new LauncherOperationException("PACK_FILES_MISSING", "Required pack files are missing. Supply local files and continue this retained instance.", JsonSerializer.SerializeToNode(result, PackJsonContext.Default.PackState));
            }
            else
            {
                steps[0].Acquisition = result.Pack?.Acquisition;
                Finish(0, result.Consistency?.MatchesRelease == false ? "completed-with-notices" : "completed");
            }

            if (options.ContentOnly || options.Offline)
            {
                steps[1].Status = "skipped";
                steps[2].Status = "skipped";
                if (preparation.Status != "blocked") preparation.Status = "content-only";
                preparation.ActiveStep = null;
                result.Preparation = preparation;
                result.Message = preparation.Status == "blocked"
                    ? "Pack content is retained with missing files; runtime and Java preparation were not requested."
                    : "Pack content processing completed; runtime and Java preparation were not requested.";
                return result;
            }

            Begin(1);
            ct.ThrowIfCancellationRequested();
            result.Runtime = await Runtimes.PreparePackAsync(instanceId, options.LibraryMirrors, ct).ConfigureAwait(false);
            result.Instance = Instances.Get(instanceId).Describe();
            result.RuntimePrepared = result.Runtime.Prepared;
            var runtimeNotices = !result.RuntimePrepared;
            var hasNotices = runtimeNotices || result.Consistency?.MatchesRelease == false;
            steps[1].Runtime = result.Runtime;
            Finish(1, runtimeNotices ? "completed-with-notices" : "completed");

            Begin(2);
            ct.ThrowIfCancellationRequested();
            var javaResult = await PreparePackJavaAsync(instanceId, result.Runtime.VersionId, ct).ConfigureAwait(false);
            result.Java = javaResult.Java;
            result.JavaWarning = javaResult.JavaWarning;
            var javaNotices = result.JavaWarning is not null || result.Java?.CompatibilityNote is not null;
            steps[2].Java = javaResult;
            Finish(2, javaNotices ? "completed-with-notices" : "completed");
            preparation.Status = hasNotices || javaNotices
                ? "completed-with-notices" : "completed";
            preparation.ActiveStep = null;
            result.Preparation = preparation;
            result.Message = preparation.Status == "completed"
                ? "Selected preparation steps completed without starting the game."
                : "Preparation finished with notices. Review unhandled declarations and compatibility advice; the game has not started.";
            return result;
        }
        catch (Exception exception)
        {
            steps[current].Error = exception.Message;
            if (steps[current].Status == "running")
                steps[current].Status = exception is OperationCanceledException ? "canceled" : "failed";
            if (preparation.Status != "blocked")
                preparation.Status = exception is OperationCanceledException ? "canceled" : "failed";
            if (current == 2 && exception is LauncherOperationException { Details: { } details })
                result.Java = details.Deserialize(PackJsonContext.Default.PackJavaPreparation)?.Java;
            result.Preparation = preparation;
            result.Message = exception.Message;
            if (exception is OperationCanceledException canceled)
                throw new LauncherOperationCanceledException("Pack preparation canceled; completed steps and the retained instance are available for retry.", JsonSerializer.SerializeToNode(result, PackJsonContext.Default.PackState)!, canceled);
            var code = exception is LauncherOperationException operation ? operation.Code : current == 0 ? "PACK_CONTENT_FAILED" : current == 1 ? "PACK_RUNTIME_FAILED" : "PACK_JAVA_FAILED";
            throw new LauncherOperationException(code, exception.Message, JsonSerializer.SerializeToNode(result, PackJsonContext.Default.PackState));
        }
        finally
        {
            _packPreparation.Value = previous;
        }
    }

    private async Task<PackJavaPreparation> PreparePackJavaAsync(string instanceId, string version, CancellationToken ct)
    {
        var instance = Instances.Get(instanceId);
        var preparation = new InstallationResult { Id = version, MinecraftDirectory = instance.MinecraftDirectory };
        await PrepareInstalledJavaAsync(instance, version, null, preparation, ct).ConfigureAwait(false);
        return new() { Java = preparation.Java, JavaWarning = preparation.JavaWarning };
    }
}