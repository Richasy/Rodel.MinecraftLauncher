// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Core.Instances;

/// <summary>Identifies one existing version and its explicitly chosen game directory, without transferring file ownership.</summary>
/// <param name="VersionId">The exact local version identifier.</param>
/// <param name="GameDirectory">An optional explicit resolution of an ambiguous source directory.</param>
/// <param name="ProfileId">An optional official-launcher profile whose runtime settings and game directory are retained.</param>
public sealed record InstanceLinkSelection(string VersionId, string? GameDirectory = null, string? ProfileId = null);

public sealed partial class InstanceStore
{
    /// <summary>Discovers each local version using the existing read-only migration adapters, without copying files or registering instances.</summary>
    public IReadOnlyList<InstanceImportPreview> PreviewLinks(string directory)
    {
        var source = PreviewImport(directory);
        if (source.LocalVersions.Count == 0)
            return source.VersionId is null ? [] : [source];
        var previews = new List<InstanceImportPreview>();
        if (source.SourceSettings["profiles"] is System.Text.Json.Nodes.JsonArray profiles)
        {
            foreach (var profile in profiles)
            {
                var version = profile?["versionId"]?.ToString();
                if (version is not null && source.LocalVersions.Contains(version, StringComparer.Ordinal))
                    previews.Add(PreviewImport(directory, profile?["id"]?.ToString(), versionId: version));
            }
        }

        foreach (var version in source.LocalVersions.Where(version => !previews.Any(preview => preview.VersionId == version)))
            previews.Add(PreviewImport(directory, versionId: version));
        return previews;
    }

    /// <summary>Links all discovered versions, or an explicit selection, as unmanaged instances. Repeated links reuse matching registrations; failures roll back only new metadata.</summary>
    /// <param name="directory">The existing Minecraft root or its launcher parent.</param>
    /// <param name="selections">Exact versions and optional directory choices; null selects all discovered versions.</param>
    /// <param name="cancellationToken">Cancels before commit and removes registrations created by this attempt, never source files.</param>
    public async Task<IReadOnlyList<InstanceSummary>> LinkExistingAsync(string directory, IReadOnlyList<InstanceLinkSelection>? selections = null, CancellationToken cancellationToken = default)
    {
        var discovered = PreviewLinks(directory);
        selections ??= discovered.Select(preview => new InstanceLinkSelection(preview.VersionId!, ProfileId: preview.ProfileId)).ToArray();
        if (selections.Count == 0 || selections.Select(item => (item.VersionId, item.ProfileId)).Distinct().Count() != selections.Count)
            throw new ArgumentException("Select at least one distinct local version.", nameof(selections));
        var previews = selections.Select(selection =>
        {
            var candidates = discovered.Where(item => item.VersionId == selection.VersionId).ToArray();
            var profileId = selection.ProfileId ?? (candidates.Length == 1 ? candidates[0].ProfileId : null);
            if (!candidates.Any(item => item.ProfileId == profileId))
                throw new ArgumentException("Unknown local version: " + selection.VersionId, nameof(selections));
            var preview = PreviewImport(directory, profileId: profileId, versionId: selection.VersionId, gameDirectory: selection.GameDirectory);
            if (preview.NeedsGameDirectory)
                throw new InvalidOperationException("Select the game directory for version: " + selection.VersionId);
            if (!Directory.Exists(preview.GameDirectory))
                throw new DirectoryNotFoundException(preview.GameDirectory);
            return preview;
        }).ToArray();
        using var lease = await ResourceLease.AcquireAsync(Path.Combine(paths.InstancesDirectory, ".link-existing"), cancellationToken).ConfigureAwait(false);
        var existing = List().ToList();
        var created = new List<string>();
        var result = new List<InstanceSummary>();
        try
        {
            foreach (var preview in previews)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var match = existing.FirstOrDefault(instance => instance.VersionId == preview.VersionId
                    && instance.ToMetadata()["sourceSettings"]?["linkProfileId"]?.ToString() == preview.ProfileId
                    && SamePath(PhysicalPath(instance.GameDirectory), PhysicalPath(preview.GameDirectory))
                    && SamePath(PhysicalPath(instance.MinecraftDirectory), PhysicalPath(preview.MinecraftDirectory)));
                if (match is null)
                {
                    var sourceSettings = preview.SourceSettings.DeepClone().AsObject();
                    if (preview.ProfileId is not null)
                        sourceSettings["linkProfileId"] = preview.ProfileId;
                    match = await ImportAsync(string.IsNullOrWhiteSpace(preview.Name) ? preview.VersionId! : preview.Name, preview.GameDirectory, preview.VersionId, preview.MinecraftDirectory,
                        preview.Settings, sourceSettings, cancellationToken).ConfigureAwait(false);
                    created.Add(match.Id);
                    existing.Add(match);
                }

                result.Add(match.Describe());
            }

            return result;
        }
        catch
        {
            foreach (var id in created)
                File.Delete(MetadataPath(id));
            throw;
        }
    }
}
