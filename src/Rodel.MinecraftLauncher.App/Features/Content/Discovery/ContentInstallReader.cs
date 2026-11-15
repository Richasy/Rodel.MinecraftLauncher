// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Content;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Content;

/// <summary>Reads actual Core compatibility and creates the exact frozen review for one installation dialog.</summary>
internal sealed class ContentInstallReader(
    LauncherClient client,
    string projectId,
    string? minecraft,
    string? loader)
{
    internal Task<ContentVersionCatalog> LoadAsync(ContentInstallTarget target, CancellationToken cancellation) => Task.Run(async () =>
        {
            var result = await client.ContentPlatform.GetVersionsAsync("modrinth", projectId, ct: cancellation).ConfigureAwait(false);
            var versions = result.OfType<JsonObject>().Select(ContentVersion.Read).ToArray();
            var context = target.Id.Length > 0 ? await client.GetContentContextAsync(target.Id, ct: cancellation).ConfigureAwait(false) : null;
            return new ContentVersionCatalog(versions, context?.MinecraftVersion ?? minecraft, context?.Loader ?? loader);
        }, cancellation);

    internal Task<ContentReview> ReviewAsync(ContentReviewSelection selection, CancellationToken cancellation) => Task.Run(() => client.ReviewContentInstallAsync(selection.InstanceId, selection.ProjectId, new()
    {
        Kind = selection.Kind.Directory(),
        VersionId = selection.VersionId
    }, cancellation), cancellation);
}