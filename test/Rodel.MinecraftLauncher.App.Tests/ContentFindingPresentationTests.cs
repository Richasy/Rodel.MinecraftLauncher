// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Content;
using Rodel.MinecraftLauncher.Core.Content;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects readable content-review identities and severity, separately from Core compatibility decisions.</summary>
[TestClass]
public sealed class ContentFindingPresentationTests
{
    /// <summary>
    /// Review: raw provider project/version IDs make a dependency conflict unactionable. The shared projection
    /// must resolve real titles, filenames and release numbers while preserving conflicts versus advisories.
    /// Selection-store tests only bind reviews; this uses independent opaque IDs and readable labels to catch
    /// leaked protocol identities without pinning translations or duplicating the formatter's switch.
    /// </summary>
    [TestMethod]
    public void ReviewFindings_ResolveReadableIdentities_AndRetainSeverity()
    {
        var plan = new ContentInstallPlan
        {
            Platform = "modrinth",
            PrimaryProject = "opaque-root",
            GameDirectory = "unused",
            LockFingerprint = "unused",
            Nodes =
            [
                new()
                {
                    Platform = "modrinth", ProjectId = "opaque-root", VersionId = "opaque-new",
                    FileName = "chosen.jar", Kind = "mods", Title = "Chosen mod",
                    Upstream = new JsonObject { ["version_number"] = "2.0" },
                    ExistingFiles = [new JsonObject { ["versionId"] = "opaque-old", ["upstream"] = new JsonObject { ["version_number"] = "1.0" } }]
                },
                new()
                {
                    Platform = "modrinth", ProjectId = "opaque-pack", VersionId = "opaque-pack-version",
                    FileName = "Fallback Pack.zip", Kind = "resourcepacks", Upstream = []
                }
            ],
            Edges = [],
            Conflicts = [new() { Reason = "pinned-dependency", ProjectId = "opaque-root", PinnedVersion = "opaque-old", RequestedVersion = "opaque-new" }],
            Warnings = [new() { Reason = "explicit-version-game-metadata-mismatch", ProjectId = "opaque-pack", VersionId = "opaque-pack-version" }]
        };

        var findings = ContentFinding.ReadAll(plan);

        Assert.HasCount(2, findings);
        Assert.IsFalse(findings[0].Warning);
        Assert.Contains("Chosen mod", findings[0].Summary);
        Assert.Contains("1.0", findings[0].Summary);
        Assert.Contains("2.0", findings[0].Summary);
        Assert.IsTrue(findings[1].Warning);
        Assert.Contains("Fallback Pack.zip", findings[1].Summary);
        Assert.DoesNotContain(value => value.Summary.Contains("opaque-", StringComparison.Ordinal), findings);
        Assert.AreEqual("explicit-version-game-metadata-mismatch", plan.Warnings.Single().Reason, "Presentation must retain the original Core evidence.");
    }
}