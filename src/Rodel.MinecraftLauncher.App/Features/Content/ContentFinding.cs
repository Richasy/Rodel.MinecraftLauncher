// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Content;

namespace Rodel.MinecraftLauncher.App.Features.Content;

/// <summary>Projects frozen install/update findings into localized player-facing explanations.</summary>
internal sealed record ContentFinding(int Key, string Summary, bool Warning)
{
    internal static IReadOnlyList<ContentFinding> ReadAll(ContentInstallPlan plan)
    {
        var nodes = plan.Nodes;
        var versions = nodes.Select(node => (Id: (string?)node.VersionId, Number: node.Upstream["version_number"]?.ToString())).Concat(nodes.SelectMany(node => node.ExistingFiles).Select(record => (Id: record["versionId"]?.ToString(), Number: record["upstream"]?["version_number"]?.ToString() ?? record["metadata"]?["version"]?.ToString()))).ToArray();
        string Name(string? id)
        {
            var node = nodes.FirstOrDefault(node => node.ProjectId == id);
            return id is null ? Resources.ValueNotAvailable : node?.Title ?? node?.FileName ?? id;
        }

        string Version(string? id)
        {
            if (id is null)
                return Resources.ValueNotAvailable;
            var (Id, Number) = versions.FirstOrDefault(node => node.Id == id);
            return "\u2066" + (Number ?? id) + "\u2069";
        }

        var findings = new List<ContentFinding>();
        foreach (var warning in new[]
        {
            false,
            true
        }
        )
        {
            foreach (var value in warning ? plan.Warnings : plan.Conflicts)
            {
                var reason = value.Reason;
                var subject = value.Path ?? Name(value.ProjectId ?? value.RequiredBy);
                var parent = value.ConsumerPath ?? Name(value.RequiredBy ?? value.RetainedConsumer);
                var required = Version(value.RequiredVersion ?? value.RequestedVersion ?? value.PinnedVersion);
                var selected = Version(value.SelectedVersion ?? value.FirstVersion ?? value.VersionId);
                var explanation = reason switch
                {
                    "retained-consumer-fixed-version" or "different-fixed-versions" or "explicit-selection-overrides-retained-consumer" => string.Format(Resources.ContentUpdatesVersionConflict, parent, required, selected),
                    "pinned-dependency" => string.Format(Resources.ContentUpdatesPinnedConflict, Version(value.PinnedVersion), Version(value.RequestedVersion)),
                    "locally-modified-content" => Resources.ContentUpdatesModifiedConflict,
                    "identical-file-has-different-source" => Resources.ContentUpdatesSourceConflict,
                    "different-update-contexts" => Resources.ContentUpdatesContextConflict,
                    "not-a-content-package" or "manual-download-required" or "manual-external-dependency" or "dependencies-managed-manually" => Resources.ContentUpdatesManualDependency,
                    "explicit-version-game-metadata-mismatch" => Resources.ContentUpdatesCompatibilityWarning,
                    "explicit-version-loader-metadata-mismatch" => Resources.ContentReviewLoaderCompatibilityWarning,
                    "explicit-version-channel-override" => Resources.ContentUpdatesChannelWarning,
                    "project-metadata-unavailable" or "retained-dependency-metadata-unavailable" => Resources.ContentUpdatesMetadataWarning,
                    "explicit-version-selection" => string.Format(Resources.ContentUpdatesExactVersion, selected),
                    _ => Resources.ContentUpdatesDependencyConflict,
                };
                findings.Add(new(findings.Count, string.Format(Resources.ContentUpdatesFinding, subject, explanation), warning));
            }
        }

        return findings;
    }
}