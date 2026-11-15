// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Packs;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    private enum PackChange
    {
        Apply,
        Update
    }

    /// <summary>Imports a pack and automatically prepares its selected runtime and Java, retaining the instance when later steps fail.</summary>
    public Task<PackState> ImportPackAsync(string archive, PackWorkflowOptions? options = null, CancellationToken cancellationToken = default) => PreparePackWorkflowAsync(null, archive, options ?? new(), cancellationToken);
    /// <summary>Updates pack content and prepares its runtime and Java, rolling back the content transaction if preparation fails or is canceled.</summary>
    public Task<PackReview> UpdatePackAsync(string instanceId, string archive, PackWorkflowOptions? options = null, CancellationToken cancellationToken = default) => ChangePackAsync(PackChange.Update, instanceId, archive, options ?? new(), cancellationToken);
    /// <summary>Applies a frozen pack review and prepares its runtime and Java with the same compensation policy as direct updates.</summary>
    public Task<PackReview> ApplyPackUpdateAsync(string operationId, PackWorkflowOptions? options = null, CancellationToken cancellationToken = default) => ChangePackAsync(PackChange.Apply, operationId, null, options ?? new(), cancellationToken);
    private async Task<PackReview> ChangePackAsync(PackChange action, string target, string? archive, PackWorkflowOptions options, CancellationToken ct)
    {
        using var cacheUse = await Storage.CacheActivity.EnterAsync(paths, ct).ConfigureAwait(false);
        var packs = new PackService(paths, progress: DownloadEvents);
        var optional = options.OptionalSelection;
        if (action is PackChange.Apply or PackChange.Update)
        {
            var updated = action == PackChange.Apply ? await packs.ApplyAsync(target, ct).ConfigureAwait(false) : await packs.UpdateAsync(instanceId: target, archivePath: archive!, download: !options.Offline, manualDirectory: options.ManualDirectory, cancellationToken: ct, optionalSelection: optional).ConfigureAwait(false);
            return await PrepareAppliedPackAsync(updated, options, ct).ConfigureAwait(false);
        }

        throw new ArgumentOutOfRangeException(nameof(action));
    }

    private async Task<PackReview> PrepareAppliedPackAsync(PackReview updated, PackWorkflowOptions options, CancellationToken ct)
    {
        updated.RuntimePrepared = false;
        updated.JavaPrepared = false;
        if (options.ContentOnly || options.Offline)
            return updated;

        var instanceId = updated.InstanceId;
        var operationId = updated.OperationId;
        var stage = "runtime";
        try
        {
            if (updated.Complete != true)
                throw new LauncherOperationException("PACK_FILES_MISSING", "Required pack files are missing; the update cannot prepare its runtime.", JsonSerializer.SerializeToNode(updated, PackJsonContext.Default.PackReview));
            Report(new LauncherProgress { Stage = "pack-runtime", InstanceId = instanceId, OperationId = operationId }, true);
            ct.ThrowIfCancellationRequested();
            updated.Runtime = await Runtimes.PreparePackAsync(instanceId, options.LibraryMirrors, ct).ConfigureAwait(false);
            updated.RuntimePrepared = updated.Runtime.Prepared;
            stage = "java";
            Report(new LauncherProgress { Stage = "pack-java", InstanceId = instanceId, OperationId = operationId }, true);
            ct.ThrowIfCancellationRequested();
            var java = await PreparePackJavaAsync(instanceId, updated.Runtime.VersionId, ct).ConfigureAwait(false);
            updated.Java = java.Java;
            updated.JavaWarning = java.JavaWarning;
            updated.JavaPrepared = true;
            return updated;
        }
        catch (Exception failure)
        {
            updated.PreparationFailure = PackPreparationFailure.From(failure, stage);
            try
            {
                var rollback = await Packs.RollbackAsync(operationId, CancellationToken.None).ConfigureAwait(false);
                updated.Phase = rollback.Phase;
                updated.AppliedConsistency = updated.Consistency;
                updated.Consistency = null;
                updated.Rollback = rollback;
                updated.RuntimePrepared = false;
                updated.JavaPrepared = false;
            }
            catch (Exception rollbackFailure)
            {
                updated.RollbackFailure = LauncherOperationException.CreateResult(rollbackFailure).Error;
                throw new LauncherOperationException("PACK_ROLLBACK_FAILED", $"Pack preparation failed: {failure.Message}. Automatic rollback also failed: {rollbackFailure.Message}. Retry pack rollback {operationId} after resolving the file error.", JsonSerializer.SerializeToNode(updated, PackJsonContext.Default.PackReview));
            }

            if (failure is OperationCanceledException canceled)
                throw new LauncherOperationCanceledException("Pack preparation canceled; the update was rolled back.", JsonSerializer.SerializeToNode(updated, PackJsonContext.Default.PackReview)!, canceled);
            var code = failure is LauncherOperationException operation ? operation.Code : stage == "java" ? "PACK_JAVA_FAILED" : "PACK_RUNTIME_FAILED";
            throw new LauncherOperationException(code, "Pack preparation failed; the update was rolled back. " + failure.Message, JsonSerializer.SerializeToNode(updated, PackJsonContext.Default.PackReview));
        }
    }
}