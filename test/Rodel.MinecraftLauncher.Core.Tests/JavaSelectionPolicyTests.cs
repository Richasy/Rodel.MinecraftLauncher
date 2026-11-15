using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects executable ranking and the launcher's declared advisory boundaries, not actual game/JDK compatibility.</summary>
[TestClass]
public sealed class JavaSelectionPolicyTests
{
    /// <summary>An absent preference must not become a preference for candidates whose version probe was inconclusive.</summary>
    [TestMethod]
    public void Choose_NoPreferredMajor_RanksKnownVersionWithoutFavoringNullMajor()
    {
        var known = Candidate("known-java", 25, "25.0.1");
        var unknown = Candidate("unknown-java", null, null);
        AssertChoice(known, known, unknown, null);
    }

    /// <summary>Only probe failure excludes an executable; an unknown version remains a usable fallback rather than an implicit rejection.</summary>
    [TestMethod]
    public void Choose_ProbeFailureAndUnknownVersion_FiltersOnlyUnusableCandidates()
    {
        var failed = Candidate("failed-java", 25, "25.0.1") with { IsUsable = false };
        var unknown = Candidate("unknown-java", null, null);
        AssertChoice(unknown, failed, unknown, 25);
        Assert.IsNull(JavaSelection.Choose([failed], 25));
    }

    /// <summary>A recommended major outranks bitness and newer releases; a synthetic future major prevents turning component mappings into a whitelist.</summary>
    [TestMethod]
    [DataRow(17)]
    [DataRow(99)]
    public void Choose_RecommendedMajor_PrecedesBitnessAndOtherVersions(int major)
    {
        var preferred = Candidate("preferred-java", major, $"{major}.0.1+1", 32);
        var other = Candidate("other-java", 25, "25.0.9+9");
        AssertChoice(preferred, other, preferred, major);
    }

    /// <summary>Within the same major and advice tier, a 64-bit JVM is preferred even over a newer 32-bit update.</summary>
    [TestMethod]
    public void Choose_SameMajor_Prefers64BitBeforePatchVersion()
    {
        var wide = Candidate("wide-java", 21, "21.0.1+1");
        var narrow = Candidate("narrow-java", 21, "21.0.9+9", 32);
        AssertChoice(wide, narrow, wide, 21);
    }

    /// <summary>Full build versions outrank path ordering, while equal versions have a stable path tie-break independent of enumeration order.</summary>
    [TestMethod]
    public void Choose_EqualPreference_UsesFullVersionThenStablePath()
    {
        var older = Candidate("a-older-java", 21, "21.0.4+7");
        var newer = Candidate("z-newer-java", 21, "21.0.4+8");
        AssertChoice(newer, older, newer, 21);
        var tied = newer with { Path = "b-newer-java" };
        AssertChoice(tied, newer, tied, 21);
    }

    /// <summary>A known advisory can prefer an older update, but cannot remove the only usable candidate from selection.</summary>
    [TestMethod]
    public void Choose_CompatibilityAdvice_PrefersUnflaggedUpdateWithoutHardRejectingFallback()
    {
        var metadata = Profile("1.16.5", "forge", "36.2.23");
        var older = Candidate("older-java", 8, "1.8.0_320");
        var flagged = Candidate("newer-java", 8, "1.8.0_321");
        Assert.IsNull(JavaSelection.CompatibilityNote(metadata, older));
        Assert.IsNotNull(JavaSelection.CompatibilityNote(metadata, flagged));
        AssertChoice(older, flagged, older, 8, metadata);
        Assert.AreEqual(flagged.Path, JavaSelection.Choose([flagged], 8, metadata)?.Path);
    }

    /// <summary>Preserves the existing Java 8 update-51 advisory boundary without applying it to earlier Minecraft versions.</summary>
    [TestMethod]
    [DataRow("1.12.2", "1.8.0_50", false)]
    [DataRow("1.13", "1.8.0_50", true)]
    [DataRow("1.13", "1.8.0_51", false)]
    public void CompatibilityNote_Java8WorldGeneration_RespectsGameAndUpdateScope(string game, string version, bool expected)
    {
        var note = JavaSelection.CompatibilityNote(Profile(game), Candidate("java", 8, version));
        Assert.AreEqual(expected, note is not null);
    }

    /// <summary>Protects the declared inclusive Forge ranges and their exclusions; similar non-Forge metadata must not inherit the advisory.</summary>
    [TestMethod]
    [DataRow("1.16.3", "forge", "34.1.26", false)]
    [DataRow("1.16.3", "forge", "34.1.27", true)]
    [DataRow("1.16.4", "forge", "35.1.0", true)]
    [DataRow("1.16.5", "forge", "36.2.23", true)]
    [DataRow("1.16.5", "forge", "36.2.24", false)]
    [DataRow("1.17.1", "forge", "37.0.59", false)]
    [DataRow("1.17.1", "forge", "37.0.60", true)]
    [DataRow("1.17.1", "forge", "37.0.75", true)]
    [DataRow("1.17.1", "forge", "37.0.76", false)]
    [DataRow("1.16.5", "fabric", "0.16.0", false)]
    public void CompatibilityNote_LoaderRange_RespectsDeclaredBoundaries(string game, string loader, string version, bool expected)
    {
        var note = JavaSelection.CompatibilityNote(Profile(game, loader, version), Candidate("java", 17, "17.0.2"));
        Assert.AreEqual(expected, note is not null);
    }

    /// <summary>Separate before/at cases protect the existing per-JDK update thresholds, not wording or a claim that unflagged combinations are universally compatible.</summary>
    [TestMethod]
    [DataRow(8, "1.8.0_320", false)]
    [DataRow(8, "1.8.0_321", true)]
    [DataRow(11, "11.0.13", false)]
    [DataRow(11, "11.0.14", true)]
    [DataRow(15, "15.0.5", false)]
    [DataRow(15, "15.0.6", true)]
    [DataRow(17, "17.0.1", false)]
    [DataRow(17, "17.0.2", true)]
    public void CompatibilityNote_JavaUpdate_RespectsDeclaredThresholds(int major, string version, bool expected)
    {
        var note = JavaSelection.CompatibilityNote(Profile("1.16.5", "forge", "36.2.23"), Candidate("java", major, version));
        Assert.AreEqual(expected, note is not null);
    }

    /// <summary>The real planner must retain explicit Java even when automatic policy would advise a different runtime.</summary>
    [TestMethod]
    [TestCategory("Integration")]
    public async Task Planner_ExplicitJavaWithKnownAdvisory_RetainsUserSelection()
    {
        using var fixture = new JavaProbeFixture();
        var minecraft = Path.Combine(fixture.Root, "minecraft");
        var metadata = Profile("1.16.5", "forge", "36.2.23");
        metadata["id"] = "1.16.5";
        metadata["mainClass"] = "Synthetic.Main";
        metadata["arguments"] = new JsonObject { ["jvm"] = new JsonArray(), ["game"] = new JsonArray() };
        await JsonFiles.WriteAsync(Path.Combine(minecraft, "versions", "1.16.5", "1.16.5.json"), metadata, TestContext.CancellationToken);
        var plan = await new LaunchPlanner(fixture.Paths).BuildAsync(minecraft, Path.Combine(fixture.Root, "game"), "1.16.5",
            javaPath: fixture.Executable, ct: TestContext.CancellationToken);
        Assert.AreEqual(fixture.Executable, plan.JavaPath);
        Assert.AreEqual(JavaSelectionSource.Explicit, plan.JavaSelection!.Source);
        Assert.AreEqual(8, plan.JavaSelection.PreferredMajor);
        Assert.AreEqual(25, plan.JavaSelection.SelectedMajor);
        Assert.IsNotNull(plan.JavaSelection.CompatibilityNote);
        Assert.IsTrue(plan.JavaSelection.Advisory);
    }

    private static JavaInstallation Candidate(string path, int? major, string? version, int bitness = 64) =>
        new(path, major, string.Empty, true, bitness == 32 ? "x86" : "x64", bitness) { FullVersion = version };

    private static JsonObject Profile(string game, string? loader = null, string? version = null)
    {
        var libraries = new JsonArray();
        if (loader == "forge")
            libraries.Add((JsonNode)new JsonObject { ["name"] = $"net.minecraftforge:forge:{game}-{version}" });
        else if (loader == "fabric")
            libraries.Add((JsonNode)new JsonObject { ["name"] = $"net.fabricmc:fabric-loader:{version}" });
        return new JsonObject { ["id"] = "custom-profile", ["_minecraftVersion"] = game, ["libraries"] = libraries };
    }

    private static void AssertChoice(JavaInstallation expected, JavaInstallation first, JavaInstallation second, int? preferred, JsonObject? metadata = null)
    {
        Assert.AreEqual(expected.Path, JavaSelection.Choose([first, second], preferred, metadata)?.Path);
        Assert.AreEqual(expected.Path, JavaSelection.Choose([second, first], preferred, metadata)?.Path,
            "Enumeration order must not override an established ranking tier.");
    }

    /// <summary>Gets or sets cancellation for the explicit-selection planner integration.</summary>
    public TestContext TestContext { get; set; }
}