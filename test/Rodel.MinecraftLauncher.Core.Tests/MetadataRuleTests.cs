using Rodel.MinecraftLauncher.Core.Launch;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects dynamic rule compatibility and selection semantics rather than individual reader accessors.</summary>
[TestClass]
public sealed class MetadataRuleTests
{
    /// <summary>
    /// Missing/non-array rules are unrestricted, but an explicit empty or unrecognized-entry array denies inclusion.
    /// Returning an empty sequence for every input would silently change which libraries are installed or launched.
    /// </summary>
    [TestMethod]
    [DataRow(null, true)]
    [DataRow("{}", true)]
    [DataRow("[]", false)]
    [DataRow("[null,17,\"future-entry\"]", false)]
    public void Allows_RuleSetShape_PreservesExistingDefaults(string? json, bool expected)
    {
        Assert.AreEqual(expected, MetadataRules.Allows(json is null ? null : JsonNode.Parse(json)));
    }

    /// <summary>
    /// Last matching action, Java architecture aliases and open feature names must agree without rewriting extensions.
    /// A nonmatching rule's numeric action also catches eager DTO-style parsing of fields that were never needed.
    /// </summary>
    [TestMethod]
    public void Allows_OrderedConditions_RetainsDynamicFieldsAndLazyAction()
    {
        var rules = JsonNode.Parse("""
            [
              {"action":"allow","vendor":{"future":[1,null]}},
              {"action":"disallow","os":{"name":"windows","arch":"amd64","vendor":"retained"},
               "features":{"future_feature":true,"missing_feature":false}},
              {"action":17,"os":{"name":"linux"}}
            ]
            """)!;
        var before = rules.ToJsonString();
        var enabled = new Dictionary<string, bool> { ["future_feature"] = true };
        Assert.IsFalse(MetadataRules.Allows(rules, enabled, "x86_64"));
        Assert.IsTrue(MetadataRules.Allows(rules, new Dictionary<string, bool>(), "x64"));
        Assert.IsTrue(MetadataRules.Allows(rules, enabled, "arm64"));
        var overrideRules = rules.DeepClone().AsArray();
        overrideRules.Add(new JsonObject { ["action"] = "allow" });
        Assert.IsTrue(MetadataRules.Allows(overrideRules, enabled, "x64"));
        Assert.AreEqual(before, rules.ToJsonString());
    }

    /// <summary>
    /// The existing OS regex and inclusive bounds still restrict selection; unparseable optional range strings remain advisory.
    /// This distinguishes a faithful field view from a strict Version model or an accessor that silently drops one bound.
    /// </summary>
    [TestMethod]
    public void Allows_VersionConditions_PreservesPatternsAndOptionalRange()
    {
        var current = Environment.OSVersion.Version.ToString();
        var range = new JsonObject { ["min"] = current, ["max"] = current };
        var os = new JsonObject { ["name"] = "windows", ["version"] = "^" + Regex.Escape(current) + "$", ["versionRange"] = range };
        var rules = new JsonArray(new JsonObject { ["action"] = "allow", ["os"] = os });
        Assert.IsTrue(MetadataRules.Allows(rules));
        range["max"] = "0.0";
        Assert.IsFalse(MetadataRules.Allows(rules));
        range["min"] = "future-range";
        range["max"] = "future-range";
        Assert.IsTrue(MetadataRules.Allows(rules));
        os["version"] = "$^";
        Assert.IsFalse(MetadataRules.Allows(rules));
    }

    /// <summary>
    /// Preparation must retain libraries for a metadata-declared architecture outside the built-in names.
    /// The shared reader must ignore the same non-object entries as evaluation rather than failing during preflight.
    /// </summary>
    [TestMethod]
    public void InstallationRules_UnknownArchitecture_RemainsAnAvailableCandidate()
    {
        var rules = JsonNode.Parse("""[17,null,{"action":"allow","os":{"name":"windows","arch":"future64"}}]""");
        Assert.IsFalse(MetadataRules.Allows(rules, javaArchitecture: "x64"));
        Assert.IsTrue(MetadataRules.AllowsAnyJavaArchitecture(rules));
    }
}