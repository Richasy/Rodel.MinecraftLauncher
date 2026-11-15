// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    /// <summary>Checks selected installed files and freezes their combined update and dependency plan.</summary>
    public async Task<ContentReview> ReviewContentUpdatesAsync(string instanceId, IReadOnlyList<string> fileNames,
        ContentWorkflowOptions? options = null, CancellationToken ct = default)
    {
        if (fileNames.Count == 0) throw new ArgumentException("Select at least one installed file.", nameof(fileNames));
        options ??= new();
        var instance = Instances.Get(instanceId);
        var fingerprint = await ContentInventoryFingerprintAsync(instanceId, ct).ConfigureAwait(false);
        // Compatibility overrides choose candidates; staleness compares the actual runtime, not those overrides.
        var context = await GetContentContextAsync(instanceId, ct: ct).ConfigureAwait(false);
        var updateOptions = await ContentUpdateOptionsAsync(instanceId, options, ct).ConfigureAwait(false);
        var result = await ContentPlatform.PlanUpdatesAsync(instance.GameDirectory, options.Kind, fileNames, options.World, updateOptions,
            new InlineProgress<ContentCheckProgress>(value => Report(new LauncherProgress
            {
                Stage = value.Stage,
                CompletedFiles = value.CompletedFiles,
                TotalFiles = value.TotalFiles
            })), ct).ConfigureAwait(false);
        var review = new ContentReview
        {
            Id = Guid.NewGuid().ToString("N"),
            InstanceId = instanceId,
            Mode = "online",
            Operation = "update",
            Kind = options.Kind,
            GameDirectory = instance.GameDirectory,
            Context = context,
            Fingerprint = fingerprint,
            LockFingerprint = result.LockFingerprint,
            Items = result.Items,
            Plan = result.Plan,
            PlanningFailure = result.PlanningFailure,
            CanApply = result.CanApply,
        };
        await JsonFiles.WriteAsync(ContentReviewPath(review.Id), JsonSerializer.SerializeToNode(review, ContentJsonContext.Review)!, ct).ConfigureAwait(false);
        return review;
    }
}