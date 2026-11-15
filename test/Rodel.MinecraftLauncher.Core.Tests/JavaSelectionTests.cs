using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects typed selection snapshots, their launch consumers and the existing JSON contract, not model accessors.</summary>
[TestClass]
public sealed class JavaSelectionTests
{
    /// <summary>
    /// Game selections remain advisory, but their isolated query adapter must execute both classfile
    /// ABIs. Existing launch snapshot cases cannot catch an older explicit Java bypassing a newer
    /// adapter. Independent header-only JARs protect that minimum, the legacy Java 8 floor, and optional
    /// multi-release classes without pretending to initialize a real game or duplicating discovery.
    /// </summary>
    [TestMethod]
    [DataRow(61, 69, 17, 25)]
    [DataRow(45, 52, 6, 8)]
    [DataRow(65, 52, 25, 25)]
    public async Task OriginalHelperJava_RespectsGameAndAdapterBytecodeInsteadOfAdvisoryPreference(int gameClass, int adapterClass, int metadataMajor, int expectedMajor)
    {
        await ReportStorage.RunAsync(paths =>
        {
            var client = Path.Combine(paths.Root, "original.jar");
            File.WriteAllBytes(client, Jar(gameClass));
            var metadata = new JsonObject { ["javaVersion"] = new JsonObject { ["majorVersion"] = metadataMajor } };
            Assert.AreEqual(expectedMajor, SeedRuntimeJava.RequiredMajor(metadata, client, Jar(adapterClass)));
            return Task.CompletedTask;
        });

        static byte[] Jar(int major)
        {
            using var bytes = new MemoryStream();
            using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var main = archive.CreateEntry("original/Main.class").Open())
                    main.Write([0xca, 0xfe, 0xba, 0xbe, 0, 0, (byte)(major >> 8), (byte)major]);
                using var optional = archive.CreateEntry("META-INF/versions/36/Optional.class").Open();
                optional.Write([0xca, 0xfe, 0xba, 0xbe, 0, 0, 0, 80]);
            }

            return bytes.ToArray();
        }
    }

    /// <summary>Typed sources must retain their protocol identifiers and explicit nulls when a selection has no executable.</summary>
    [TestMethod]
    [DataRow("""{"id":"1.21.1"}""", true, JavaSelectionSource.Explicit, "explicit")]
    [DataRow("""{"id":"custom","javaVersion":{"majorVersion":25,"component":"custom-runtime"}}""", false, JavaSelectionSource.Metadata, "metadata")]
    [DataRow("""{"id":"1.21.1"}""", false, JavaSelectionSource.KnownVersionPreference, "known-version-preference")]
    [DataRow("""{"id":"unknown-profile"}""", false, JavaSelectionSource.AvailableFallback, "available-fallback")]
    [DataRow("""{"id":"1.21.1"}""", false, JavaSelectionSource.AutomaticRuntime, "automatic-runtime")]
    public void Describe_SourceAndUnselectedState_PreservesWireContract(string metadataJson, bool explicitPath, JavaSelectionSource source, string wireSource)
    {
        var report = JavaSelection.Describe([], null, JsonNode.Parse(metadataJson)!.AsObject(), explicitPath);
        // This represents the workflow's typed source update, not an actual automatic download.
        if (source == JavaSelectionSource.AutomaticRuntime)
            report = report with { Source = JavaSelectionSource.AutomaticRuntime };
        Assert.AreEqual(source, report.Source);
        var wire = JsonNode.Parse(Plan(report).ToJson())!["javaSelection"]!.AsObject();
        Assert.AreEqual(wireSource, wire["source"]!.GetValue<string>());
        foreach (var name in new[] { "selectedPath", "selectedMajor", "selectedVersion", "compatibilityNote" })
        {
            Assert.IsTrue(wire.ContainsKey(name), $"Null field was omitted: {name}");
            Assert.IsNull(wire[name]);
        }

        Assert.IsTrue(wire["advisory"]!.GetValue<bool>());
        Assert.IsEmpty(wire["candidates"]!.AsArray());
    }

    /// <summary>Reports snapshot a single enumeration, retain warnings from a newly prepared selection, and explain malformed optional hints without rejecting Java.</summary>
    [TestMethod]
    public void Describe_ChangingCandidatesAndInvalidComponent_CapturesAdvisorySnapshot()
    {
        string[] probeWarnings = ["probe warning"];
        var candidate = new JavaInstallation("candidate-java", 25, "raw probe output", true, "x64", 64)
        {
            FullVersion = "25.0.1",
            Warnings = probeWarnings
        };
        var selected = candidate with { Path = "prepared-java", Warnings = ["probe warning", "selected warning"] };
        var metadata = new JsonObject
        {
            ["id"] = "custom",
            ["javaVersion"] = new JsonObject { ["majorVersion"] = 25, ["component"] = new JsonObject { ["unexpected"] = true } },
            ["vendorExtension"] = new JsonObject { ["preserve"] = "unchanged" }
        };
        var original = metadata.DeepClone();
        var enumerations = 0;
        IEnumerable<JavaInstallation> Candidates()
        {
            if (++enumerations != 1)
                throw new InvalidOperationException("Candidate discovery must not run twice for one snapshot.");
            yield return candidate;
        }

        var report = JavaSelection.Describe(Candidates(), selected, metadata, true);
        Assert.AreEqual("prepared-java", report.SelectedPath);
        Assert.AreEqual("candidate-java", report.Candidates.Single().Path);
        Assert.AreEqual("java-runtime-epsilon", report.RuntimeComponent);
        Assert.IsTrue(report.Advisory);
        Assert.HasCount(3, report.Warnings, "Distinct probe warnings and the invalid-component advisory must all be retained.");
        CollectionAssert.AreEqual(expected, report.Warnings.Take(2).ToArray());
        Assert.IsTrue(JsonNode.DeepEquals(original, metadata), "Describing a selection must not rewrite third-party metadata.");
        probeWarnings[0] = "later mutation";
        Assert.AreEqual("probe warning", report.Warnings[0]);
    }

    /// <summary>Memory and encoding must use the selected typed candidate, not the first candidate or stale JSON field names.</summary>
    [TestMethod]
    [DataRow(false, "utf-16")]
    [DataRow(true, "us-ascii")]
    public void Configure_SelectedCandidate_UsesBitnessAndFileEncoding(bool compatEncoding, string expectedEncoding)
    {
        var other = new JavaInstallation("other-java", 25, string.Empty, true, "x64", 64)
        {
            FullVersion = "25.0.1",
            FileEncoding = "utf-8",
            NativeEncoding = "utf-8"
        };
        var selected = other with { Path = "selected-java", Architecture = "x86", Bitness = 32, FileEncoding = "utf-16", NativeEncoding = "us-ascii" };
        var report = JavaSelection.Describe([other, selected], selected, new JsonObject { ["id"] = "custom" }, true);
        var plan = Plan(report);
        if (compatEncoding)
            plan = plan with { Arguments = ["-Xmx2048M", "-Dfile.encoding=COMPAT", "Synthetic.Main"], MainClassArgumentIndex = 2 };
        var configured = MemorySelection.Configure(plan, new JsonObject { ["memoryMode"] = "auto" }, null, null);
        configured = LogEncoding.Configure(configured, new JsonObject(), null);
        Assert.IsTrue(configured.MemorySelection!.Java32Bit);
        Assert.AreEqual(expectedEncoding, configured.FileEncoding);
        Assert.AreEqual(JavaSelectionSource.Explicit, configured.JavaSelection!.Source);
    }

    /// <summary>A real planner and persisted session must emit the same stable selection shape without serializing raw probe output or changing candidate field names.</summary>
    [TestMethod]
    [TestCategory("Integration")]
    public async Task PlannerAndSession_TypedSelection_PreserveSelectionPayload()
    {
        using var fixture = new JavaProbeFixture();
        var minecraft = Path.Combine(fixture.Root, "minecraft");
        await JsonFiles.WriteAsync(Path.Combine(minecraft, "versions", "synthetic", "synthetic.json"),
            new JsonObject
            {
                ["id"] = "synthetic",
                ["mainClass"] = "Synthetic.Main",
                ["javaVersion"] = new JsonObject { ["majorVersion"] = 25, ["component"] = "java-runtime-epsilon" },
                ["libraries"] = new JsonArray(),
                ["arguments"] = new JsonObject { ["jvm"] = new JsonArray(), ["game"] = new JsonArray() }
            },
            TestContext.CancellationToken);
        var plan = await new LaunchPlanner(fixture.Paths).BuildAsync(minecraft, Path.Combine(fixture.Root, "game"), "synthetic",
            javaPath: fixture.Executable, ct: TestContext.CancellationToken);
        Assert.IsNotNull(plan.JavaSelection);
        Assert.AreEqual(fixture.Executable, plan.JavaSelection.SelectedPath);
        var expected = new JsonObject
        {
            ["preferredMajor"] = 25,
            ["runtimeComponent"] = "java-runtime-epsilon",
            ["source"] = "explicit",
            ["selectedPath"] = fixture.Executable,
            ["selectedMajor"] = 25,
            ["selectedVersion"] = "25.0.1",
            ["advisory"] = true,
            ["compatibilityNote"] = null,
            ["warnings"] = new JsonArray(),
            ["candidates"] = new JsonArray(new JsonObject
            {
                ["path"] = fixture.Executable,
                ["version"] = "25.0.1",
                ["major"] = 25,
                ["usable"] = true,
                ["architecture"] = "x64",
                ["bitness"] = 64,
                ["fileEncoding"] = "UTF-8",
                ["nativeEncoding"] = "UTF-8"
            })
        };
        var wire = JsonNode.Parse(plan.ToJson())!["javaSelection"];
        Assert.IsTrue(JsonNode.DeepEquals(expected, wire), wire?.ToJsonString());
        var sessions = new SessionService(fixture.Paths);
        var id = await sessions.CreateAsync(plan, ct: TestContext.CancellationToken);
        Assert.IsTrue(JsonNode.DeepEquals(expected, JsonNode.Parse(sessions.Get(id).AdditionalData["javaSelection"].GetRawText())));
    }

    private static LaunchPlan Plan(JavaSelectionReport report) =>
        new("synthetic", report.SelectedPath ?? "java", ["-Xmx2048M", "Synthetic.Main"], Path.GetTempPath(), Path.GetTempPath(),
            [], [], "Synthetic.Main", "00000000000000000000000000000000", 25, 1)
        { JavaSelection = report };

    /// <summary>Gets or sets cancellation for real planner probes and session writes.</summary>
    public TestContext TestContext { get; set; }

    private static readonly string[] expected = ["probe warning", "selected warning"];
}