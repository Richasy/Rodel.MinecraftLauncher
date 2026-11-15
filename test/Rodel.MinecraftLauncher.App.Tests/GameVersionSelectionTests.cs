// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Versions;
using Rodel.MinecraftLauncher.Core.Installation;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects catalog grouping and selection identities shared by instance creation and room preparation.</summary>
[TestClass]
public sealed class GameVersionSelectionTests
{
    private static readonly string[] expected = ["stable-a", "snapshot-named-release", "preview-a", "legacy-a", "legacy-b", "custom-a"];

    /// <summary>Review: inserting headings must never select a heading or shift an index onto another game version. An interleaved catalog also proves grouping uses manifest types, preserves unfamiliar versions and leaves the shared snapshot unchanged. Existing cache tests cover fetching, not this presentation boundary.</summary>
    [TestMethod]
    public void GroupedCatalog_PreservesVersionIdentityAndRejectsHeaderSelection()
    {
        var catalog = new VersionCatalog
        {
            Versions = JsonNode.Parse("""
                [
                  {"id":"preview-a","type":"snapshot"},
                  {"id":"stable-a","type":"release"},
                  {"id":"legacy-a","type":"old_alpha"},
                  {"id":"custom-a","type":"future-kind"},
                  {"id":"snapshot-named-release","type":"release"},
                  {"id":"legacy-b","type":"old_beta"}
                ]
                """)!.AsArray(),
        };
        var original = catalog.Versions.ToJsonString();
        var choices = new GameVersionChoices(catalog);
        CollectionAssert.AreEqual(expected,
            Enumerable.Range(0, choices.Rows.Count).Select(choices.VersionAt).OfType<string>().ToArray());
        foreach (var index in Enumerable.Range(0, choices.Rows.Count).Where(index => choices.Rows[index].Header))
            Assert.IsNull(choices.VersionAt(index), "A category heading must not become a submitted game version.");
        Assert.IsNull(choices.VersionAt(-1));
        Assert.IsNull(choices.VersionAt(choices.Rows.Count));
        Assert.AreEqual(original, catalog.Versions.ToJsonString());
    }

    /// <summary>Review: search must keep a heading only when its group has matching versions; otherwise a header-only result looks selectable and masks usable results. Independent query expectations cover mixed-case search and prevent an unknown manifest type from being dropped.</summary>
    [TestMethod]
    public void Search_OnlyRetainsHeadingsWithMatchingVersions()
    {
        var catalog = new VersionCatalog
        {
            Versions = JsonNode.Parse("""
                [{"id":"1.20.1","type":"release"},
                 {"id":"24w10a","type":"snapshot"},
                 {"id":"custom-fork","type":"future-kind"}]
                """)!.AsArray(),
        };
        var choices = new GameVersionChoices(catalog);
        Assert.IsTrue(choices.GroupMatches("snapshot", "24W"));
        Assert.IsFalse(choices.GroupMatches("release", "24W"));
        Assert.IsTrue(choices.GroupMatches("other", "FORK"));
        Assert.IsFalse(choices.GroupMatches("snapshot", "no-match"));
        Assert.DoesNotContain(row => row.Group == "historical", choices.Rows);
    }
}