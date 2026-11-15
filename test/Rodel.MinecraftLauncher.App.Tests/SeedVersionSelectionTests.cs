// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Features.SeedExplorer;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects opened-runtime identity in the grouped version selector; draft storage tests do not exercise this mapping.</summary>
[TestClass]
public sealed class SeedVersionSelectionTests
{
    /// <summary>A retained runtime must remain selected with its actual profile even when the current representative changed loader, Java, path spelling or installation.</summary>
    [TestMethod]
    [DataRow("loader")]
    [DataRow("java")]
    [DataRow("path")]
    [DataRow("installation")]
    public void OpenedHistory_GroupRepresentativeDiffers_SelectsActualRuntime(string difference)
    {
        // The independent oracle is the profile returned by Core, not a guessed compatible candidate.
        // The old exact-record lookup returns -1 for every variant; selecting the same-name candidate
        // without rebinding its profile would instead use current Java or another installation.
        var root = Path.Combine(Path.GetTempPath(), "rodel-seed-selection", "current");
        var current = new SeedRuntimeProfile { MinecraftDirectory = root, VersionId = "26.3-fabric", JavaPath = Path.Combine(root, "java-current", "bin", "java.exe") };
        var opened = difference switch
        {
            "loader" => current with { VersionId = "26.3" },
            "java" => current with { JavaPath = Path.Combine(root, "java-retained", "bin", "java.exe") },
            "path" => current with { MinecraftDirectory = root.ToUpperInvariant() + Path.DirectorySeparatorChar },
            "installation" => current with { MinecraftDirectory = Path.Combine(Path.GetTempPath(), "rodel-seed-selection", "retained") },
            _ => throw new ArgumentOutOfRangeException(nameof(difference)),
        };
        var other = new SeedVersionOption(current with { VersionId = "26.2" }, "26.2", true);
        SeedVersionOption[] versions = [other, new(current, "26.3", true) { Type = "release" }];

        var selection = SeedVersionSelection.Opened(versions, opened, "26.3");

        Assert.IsGreaterThanOrEqualTo(0, selection.Index);
        Assert.AreEqual("26.3", selection.Versions[selection.Index].MinecraftVersion);
        Assert.AreEqual(opened, selection.Versions[selection.Index].Profile);
        Assert.IsTrue(selection.Versions[selection.Index].Installed);
        Assert.AreEqual(other, selection.Versions.Single(option => option.MinecraftVersion == "26.2"));
        Assert.AreEqual(current, versions[1].Profile, "Publishing a new selection must not mutate an earlier snapshot's choices.");
    }

    /// <summary>A verified historical runtime missing from the catalog must be selectable without discarding the remaining installed/uninstalled choices.</summary>
    [TestMethod]
    public void OpenedHistory_VersionAbsent_AddsVerifiedOption()
    {
        var opened = new SeedRuntimeProfile { MinecraftDirectory = Path.Combine(Path.GetTempPath(), "rodel-retained-version"), VersionId = "local-snapshot" };
        SeedVersionOption[] versions = [new(new() { VersionId = "26.3" }, "26.3", false, "SEED_VERSION_NOT_INSTALLED")];

        var selection = SeedVersionSelection.Opened(versions, opened, "local-snapshot");

        Assert.IsGreaterThanOrEqualTo(0, selection.Index);
        Assert.AreEqual(opened, selection.Versions[selection.Index].Profile);
        Assert.AreEqual("local-snapshot", selection.Versions[selection.Index].MinecraftVersion);
        Assert.IsTrue(selection.Versions[selection.Index].Installed);
        Assert.IsNull(selection.Versions[selection.Index].Error);
        Assert.AreEqual(versions[0], selection.Versions.Single(option => option.MinecraftVersion == "26.3"));
    }
}