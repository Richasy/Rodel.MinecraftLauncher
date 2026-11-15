// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.SeedExplorer;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects the real persisted draft boundary when older preferences lack newly introduced source choices.</summary>
[TestClass]
public sealed class SeedWorkspaceDraftTests
{
    /// <summary>Opening existing manual preferences must default to saved maps and tracking, while retaining explicit false values after a round trip.</summary>
    [TestMethod]
    public void ExistingDraft_NewWorldOptionsKeepDefaultsAndRoundTripExplicitChoices()
    {
        var old = SeedWorkspaceDraft.Read("""{"seedInput":"manual draft","world":{"dimension":"overworld","preset":"normal"},"conditions":{}}""");
        Assert.IsTrue(old.SavedMap); Assert.IsTrue(old.TrackPlayer); Assert.AreEqual("manual draft", old.SeedInput);
        var changed = old with { WorldSource = true, WorldInstanceId = "selected-instance", WorldFolder = "selected-world", SavedMap = false, TrackPlayer = false };
        var restored = SeedWorkspaceDraft.Read(changed.Serialize());
        Assert.IsFalse(restored.SavedMap); Assert.IsFalse(restored.TrackPlayer);
        Assert.AreEqual("selected-instance", restored.WorldInstanceId); Assert.AreEqual("selected-world", restored.WorldFolder);
        Assert.AreEqual("manual draft", restored.SeedInput);
    }
}