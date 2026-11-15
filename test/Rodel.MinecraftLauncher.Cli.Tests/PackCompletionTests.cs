using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects retained pack completion and preparation failure boundaries, not online catalogs or actual game compatibility.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class PackCompletionTests
{
    /// <summary>
    /// A successful but incomplete import must remain recoverable in the same instance. Unlike local content tests,
    /// this crosses the persisted manifest, optional selection and typed background completion boundaries.
    /// Re-extracting overrides or treating completion as a fresh import would lose edits or install excluded files,
    /// including client-unsupported declarations also present in the archive overrides. Review: extending
    /// this existing recovery journey with single-file staging protects the UI boundary without another
    /// duplicate case; wrong bytes must fail completion, then the frozen corrected copy succeeds.
    /// </summary>
    [TestMethod]
    public async Task Mrpack_CompleteRetainedInstance_PreservesChoicesAndPlayerFiles()
    {
        using var cli = new CliSandbox();
        Directory.CreateDirectory(cli.Root);
        var bytes = Encoding.UTF8.GetBytes("synthetic required package");
        var manifest = new JsonObject
        {
            ["formatVersion"] = 1,
            ["game"] = "minecraft",
            ["versionId"] = "fixed-1",
            ["name"] = "Completion fixture",
            ["dependencies"] = new JsonObject { ["minecraft"] = "1.21.1" },
            ["files"] = new JsonArray(
                Declaration("mods/required.jar", bytes, "required"),
                Declaration("mods/optional.jar", bytes, "optional"),
                Declaration("mods/server.jar", bytes, "unsupported"))
        };
        var archive = WriteArchive(cli.Root, "source.mrpack", "modrinth.index.json", manifest);
        var imported = (await cli.RunAsync("pack", "import", archive, "--offline", "--exclude-optional", "mods/optional.jar")).ReadEnvelope(0)["data"]!;
        Assert.IsFalse(imported["complete"]!.GetValue<bool>());
        Assert.HasCount(1, imported["missing"]!.AsArray());
        var id = imported["instance"]!["id"]!.GetValue<string>();
        var client = new LauncherClient(new LauncherPaths(cli.Root));
        var game = client.Instances.Get(id).GameDirectory;
        var config = Path.Combine(game, "config", "player.txt");
        await File.WriteAllTextAsync(config, "player edit", TestContext.CancellationToken);
        var extra = Path.Combine(game, "mods", "extra.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(extra)!);
        await File.WriteAllTextAsync(extra, "player extra", TestContext.CancellationToken);
        var source = Path.Combine(cli.Root, "selected-name.jar");
        await File.WriteAllTextAsync(source, "wrong bytes", TestContext.CancellationToken);
        var staged = (await cli.RunAsync("pack", "stage-file", id, "mods/required.jar", source)).ReadEnvelope(0)["data"]!;
        var manual = staged["manualDirectory"]!.GetValue<string>();
        Assert.AreEqual("selected-name.jar", staged["fileName"]!.GetValue<string>());
        Assert.IsFalse(File.Exists(Path.Combine(game, "mods", "required.jar")), "Selecting a file must not install it.");
        (await cli.RunAsync("pack", "complete", id, "--offline", "--manual-directory", manual)).ReadEnvelope(1);
        Assert.IsFalse(client.Packs.GetState(id).Complete);
        Assert.AreEqual("player edit", await File.ReadAllTextAsync(config, TestContext.CancellationToken));
        await File.WriteAllBytesAsync(source, bytes, TestContext.CancellationToken);
        (await cli.RunAsync("pack", "stage-file", id, "mods/required.jar", source)).ReadEnvelope(0);
        File.Delete(source);

        await using var job = await JobHost.StartAsync(cli, "pack", "complete", id, "--offline", "--manual-directory", manual);
        var terminal = await job.WaitForTerminalAsync();

        Assert.AreEqual("succeeded", terminal["status"]?.GetValue<string>(), terminal.ToJsonString());
        Assert.AreEqual("launcher-task", terminal["request"]?["operation"]?.GetValue<string>());
        Assert.AreEqual("complete-pack", terminal["request"]?["parameters"]?["kind"]?.GetValue<string>());
        var state = client.Packs.GetState(id);
        Assert.IsTrue(state.Complete);
        Assert.HasCount(0, state.Missing);
        Assert.HasCount(1, client.Instances.List());
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(Path.Combine(game, "mods", "required.jar"), TestContext.CancellationToken));
        Assert.IsFalse(File.Exists(Path.Combine(game, "mods", "optional.jar")));
        Assert.IsFalse(File.Exists(Path.Combine(game, "mods", "server.jar")));
        Assert.AreEqual("player edit", await File.ReadAllTextAsync(config, TestContext.CancellationToken));
        Assert.AreEqual("player extra", await File.ReadAllTextAsync(extra, TestContext.CancellationToken));
        Assert.IsFalse(state.Pack!.OptionalSelection.Paths["mods/optional.jar"]);
    }

    /// <summary>
    /// Offline CF completion requires an explicit project:file mapping and must reject another release before
    /// mutation. Correcting that input must repair the original instance, without implying API identity verification.
    /// This differs from MRPACK hash-based recovery and needs no provider, credentials or network.
    /// </summary>
    [TestMethod]
    public async Task CurseForge_WrongReleaseThenMappedCompletion_RetainsInstanceAndUnverifiedIdentity()
    {
        using var cli = new CliSandbox();
        Directory.CreateDirectory(cli.Root);
        var manifest = new JsonObject
        {
            ["manifestType"] = "minecraftModpack",
            ["manifestVersion"] = 1,
            ["name"] = "Offline CF fixture",
            ["version"] = "one",
            ["minecraft"] = new JsonObject { ["version"] = "1.20.1", ["modLoaders"] = new JsonArray() },
            ["files"] = new JsonArray(new JsonObject { ["projectID"] = 123, ["fileID"] = 456, ["required"] = true }),
            ["overrides"] = "overrides"
        };
        var archive = WriteArchive(cli.Root, "original.zip", "manifest.json", manifest);
        var imported = (await cli.RunAsync("pack", "import", archive, "--offline")).ReadEnvelope(0)["data"]!;
        Assert.AreEqual("123:456", imported["missing"]![0]!["manualKey"]!.GetValue<string>());
        Assert.AreEqual("123-456.jar", imported["missing"]![0]!["manualFileName"]!.GetValue<string>());
        var id = imported["instance"]!["id"]!.GetValue<string>();
        var client = new LauncherClient(new LauncherPaths(cli.Root));
        var lockFile = Path.Combine(client.Instances.Get(id).GameDirectory, ".rodel", "pack-lock.json");
        var before = await File.ReadAllTextAsync(lockFile, TestContext.CancellationToken);
        manifest["version"] = "other release";
        var wrong = WriteArchive(cli.Root, "wrong.zip", "manifest.json", manifest);

        (await cli.RunAsync("pack", "complete", id, wrong, "--offline")).ReadEnvelope(1);
        Assert.AreEqual(before, await File.ReadAllTextAsync(lockFile, TestContext.CancellationToken), "A different release must not replace the retained baseline.");
        var manual = Path.Combine(cli.Root, "manual");
        Directory.CreateDirectory(manual);
        var source = Path.Combine(manual, "local-name.jar");
        ContentFixture.WritePackage(source, "explicitly mapped offline package");
        var bytes = await File.ReadAllBytesAsync(source, TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(manual, "manual-files.json"), """{"123:456":"local-name.jar"}""", TestContext.CancellationToken);

        var completed = (await cli.RunAsync("pack", "complete", id, "--offline", "--manual-directory", manual)).ReadEnvelope(0)["data"]!;

        Assert.IsTrue(completed["complete"]!.GetValue<bool>());
        Assert.HasCount(1, client.Instances.List());
        var installed = Path.Combine(client.Instances.Get(id).GameDirectory, "mods", "local-name.jar");
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(installed, TestContext.CancellationToken));
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(source, TestContext.CancellationToken));
        var record = client.Packs.GetState(id).Pack!.Files.Single(item => item.ProjectId == "123");
        Assert.IsFalse(record.IdentityVerified);
        Assert.IsFalse(record.ClassificationVerified);
    }

    /// <summary>
    /// Unlike content-only completion, one preparation request must advance automatically after repairing files.
    /// A local metadata fixture deliberately lacks its client JAR and any download URL, so runtime failure is
    /// deterministic and network-free. Reporting success, requiring a second request, or advancing Java would fail.
    /// </summary>
    [TestMethod]
    public async Task Preparation_MissingFilesThenRuntimeFailure_AdvancesAndRetainsCompletedSteps()
    {
        using var cli = new CliSandbox();
        var (archive, manual, bytes) = await CreatePreparationSourceAsync(cli);
        var blocked = (await cli.RunAsync("pack", "import", archive)).ReadEnvelope(1);
        Assert.AreEqual("PACK_FILES_MISSING", blocked["error"]?["code"]?.ToString());
        var id = blocked["data"]!["instance"]!["id"]!.GetValue<string>();
        Assert.AreEqual("blocked", blocked["data"]?["preparation"]?["steps"]?[0]?["status"]?.ToString());
        Assert.AreEqual("pending", blocked["data"]?["preparation"]?["steps"]?[1]?["status"]?.ToString());
        var client = new LauncherClient(new LauncherPaths(cli.Root));
        var instance = client.Instances.Get(id);
        var versionDirectory = Path.Combine(instance.MinecraftDirectory, "versions", "preparation-fixture");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "preparation-fixture.json"),
            """{"id":"preparation-fixture","mainClass":"fixture.Main","libraries":[],"arguments":{"game":[],"jvm":[]},"downloads":{"client":{}}}""",
            TestContext.CancellationToken);

        // The new host reopens durable typed input, completes files and reaches the controlled runtime fault.
        await using var job = await JobHost.StartAsync(cli, "pack", "prepare", id, "--manual-directory", manual);
        var terminal = await job.WaitForTerminalAsync();

        Assert.AreEqual("failed", terminal["status"]?.ToString(), terminal.ToJsonString());
        Assert.AreEqual("prepare-pack-workflow", terminal["request"]?["parameters"]?["kind"]?.ToString());
        var result = terminal["failure"]!["data"]!;
        Assert.AreEqual(id, result["instance"]?["id"]?.ToString());
        Assert.AreEqual("completed", result["preparation"]?["steps"]?[0]?["status"]?.ToString());
        Assert.AreEqual("failed", result["preparation"]?["steps"]?[1]?["status"]?.ToString());
        Assert.AreEqual("pending", result["preparation"]?["steps"]?[2]?["status"]?.ToString());
        Assert.HasCount(0, result["missing"]!.AsArray());
        Assert.HasCount(1, client.Instances.List());
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(Path.Combine(instance.GameDirectory, "mods", "required.jar"), TestContext.CancellationToken));
    }

    /// <summary>
    /// Cancel exactly when Core publishes the runtime-step boundary, after actual content completion.
    /// This adds post-content cancellation evidence that queued-job cancellation cannot provide; no sleeps,
    /// remote services, Java executable or runnable game are needed to establish the boundary.
    /// The retained callback must remain a running snapshot when the same workflow later becomes canceled;
    /// checking its nested step after cancellation detects a shallow progress copy at the real mutation boundary.
    /// </summary>
    [TestMethod]
    public async Task Preparation_CancelAtRuntimeBoundary_PreservesFilesAndCanceledRecoveryData()
    {
        using var cli = new CliSandbox();
        var (archive, manual, bytes) = await CreatePreparationSourceAsync(cli);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var reachedBoundary = false;
        LauncherProgress? checkpoint = null;
        var client = new LauncherClient(new LauncherPaths(cli.Root), progress: new PreparationProgress(value =>
        {
            if (value.Stage != "pack-runtime") return;
            reachedBoundary = true;
            checkpoint = value;
            Assert.AreEqual("completed", value.PackPreparation?.Steps[0].Status);
            cancellation.Cancel();
        }));
        var imported = await client.ImportPackAsync(archive, new() { Offline = true, ManualDirectory = manual }, TestContext.CancellationToken);
        var id = imported.Instance!.Id;

        var canceled = await Assert.ThrowsAsync<LauncherOperationCanceledException>(
            () => client.PreparePackAsync(id, cancellationToken: cancellation.Token));

        Assert.IsTrue(reachedBoundary, "Cancellation must occur at the actual runtime checkpoint, not before dispatch.");
        var snapshot = checkpoint?.PackPreparation;
        Assert.IsNotNull(snapshot);
        Assert.AreEqual("running", snapshot.Status);
        Assert.AreEqual("running", snapshot.Steps[1].Status, "A delivered callback must not share mutable steps with the canceled workflow.");
        Assert.AreEqual("canceled", canceled.Details["preparation"]?["status"]?.ToString());
        Assert.AreEqual("completed", canceled.Details["preparation"]?["steps"]?[0]?["status"]?.ToString());
        Assert.AreEqual("canceled", canceled.Details["preparation"]?["steps"]?[1]?["status"]?.ToString());
        Assert.AreEqual("pending", canceled.Details["preparation"]?["steps"]?[2]?["status"]?.ToString());
        Assert.HasCount(1, client.Instances.List());
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(Path.Combine(client.Instances.Get(id).GameDirectory, "mods", "required.jar"), TestContext.CancellationToken));
    }

    private async Task<(string Archive, string Manual, byte[] Bytes)> CreatePreparationSourceAsync(CliSandbox cli)
    {
        Directory.CreateDirectory(cli.Root);
        var bytes = Encoding.UTF8.GetBytes("preparation boundary package");
        var manifest = new JsonObject
        {
            ["formatVersion"] = 1,
            ["game"] = "minecraft",
            ["versionId"] = "one",
            ["name"] = "Preparation fixture",
            ["dependencies"] = new JsonObject { ["minecraft"] = "preparation-fixture" },
            ["files"] = new JsonArray(Declaration("mods/required.jar", bytes, "required"))
        };
        var archive = WriteArchive(cli.Root, "preparation.mrpack", "modrinth.index.json", manifest);
        var manual = Path.Combine(cli.Root, "manual");
        Directory.CreateDirectory(Path.Combine(manual, "mods"));
        await File.WriteAllBytesAsync(Path.Combine(manual, "mods", "required.jar"), bytes, TestContext.CancellationToken);
        return (archive, manual, bytes);
    }

    /// <summary>
    /// An unhandled declaration must not become an unqualified green runtime completion. The synthetic local
    /// profile and JAR let the real installer finish without network; where.exe deliberately replaces only
    /// Java probing as an unusable explicit executable, proving that advice does not gate the next step.
    /// </summary>
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task Preparation_UnknownDeclaration_PreservesNoticeAndAttemptsExplicitJava()
    {
        using var cli = new CliSandbox();
        var (archive, manual, _) = await CreatePreparationSourceAsync(cli);
        var client = new LauncherClient(new LauncherPaths(cli.Root));
        var imported = await client.ImportPackAsync(archive, new() { Offline = true, ManualDirectory = manual }, TestContext.CancellationToken);
        var id = imported.Instance!.Id;
        var instance = client.Instances.Get(id);
        var lockFile = Path.Combine(instance.GameDirectory, ".rodel", "pack-lock.json");
        var pack = JsonNode.Parse(await File.ReadAllTextAsync(lockFile, TestContext.CancellationToken))!;
        pack["dependencies"]!["unhandled-loader"] = "fixture";
        await File.WriteAllTextAsync(lockFile, pack.ToJsonString(), TestContext.CancellationToken);
        var versionDirectory = Path.Combine(instance.MinecraftDirectory, "versions", "preparation-fixture");
        Directory.CreateDirectory(versionDirectory);
        var clientBytes = Encoding.UTF8.GetBytes("synthetic client, never executed");
        var metadata = JsonNode.Parse("""{"id":"preparation-fixture","mainClass":"fixture.Main","libraries":[],"arguments":{"game":[],"jvm":[]},"downloads":{}}""")!;
        metadata["downloads"]!["client"] = new JsonObject
        {
            ["sha1"] = Convert.ToHexString(SHA1.HashData(clientBytes)).ToLowerInvariant(),
            ["size"] = clientBytes.Length
        };
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "preparation-fixture.json"), metadata.ToJsonString(), TestContext.CancellationToken);
        // New managed instances route client bytes through dependencies.json, not the legacy version directory.
        var referencesFile = Path.Combine(instance.MinecraftDirectory, "dependencies.json");
        var references = JsonNode.Parse(await File.ReadAllTextAsync(referencesFile, TestContext.CancellationToken))!;
        var slot = new string('a', 64);
        references["clients"]!["preparation-fixture"] = slot;
        await File.WriteAllTextAsync(referencesFile, references.ToJsonString(), TestContext.CancellationToken);
        var clientsDirectory = Path.Combine(cli.Root, "dependencies", "clients");
        Directory.CreateDirectory(clientsDirectory);
        await File.WriteAllBytesAsync(Path.Combine(clientsDirectory, slot + ".jar"), clientBytes, TestContext.CancellationToken);
        await client.Settings.SetAsync("javaPath", JsonValue.Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe")), instance, TestContext.CancellationToken);

        var failure = (await cli.RunAsync("pack", "prepare", id)).ReadEnvelope(1);

        Assert.AreEqual("JAVA_UNAVAILABLE", failure["error"]?["code"]?.ToString(), failure.ToJsonString());
        Assert.IsFalse(failure["data"]?["runtimePrepared"]?.GetValue<bool>());
        Assert.AreEqual("completed-with-notices", failure["data"]?["preparation"]?["steps"]?[1]?["status"]?.ToString());
        Assert.AreEqual("failed", failure["data"]?["preparation"]?["steps"]?[2]?["status"]?.ToString());
        Assert.Contains("unhandled-loader", failure["data"]!["runtime"]!["warnings"]!.ToJsonString());
        Assert.HasCount(1, client.Instances.List());
    }

    private sealed class PreparationProgress(Action<LauncherProgress> observe) : IProgress<LauncherProgress>
    {
        /// <summary>Runs the test's checkpoint observer synchronously on the actual preparation boundary.</summary>
        public void Report(LauncherProgress value) => observe(value);
    }

    private static JsonObject Declaration(string path, byte[] bytes, string client) => new()
    {
        ["path"] = path,
        ["hashes"] = new JsonObject { ["sha1"] = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant() },
        ["fileSize"] = bytes.Length,
        ["downloads"] = new JsonArray(),
        ["env"] = new JsonObject { ["client"] = client, ["server"] = "required" }
    };

    private static string WriteArchive(string directory, string name, string manifestName, JsonObject manifest)
    {
        var path = Path.Combine(directory, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry(manifestName).Open()))
            writer.Write(manifest.ToJsonString());
        using (var writer = new StreamWriter(archive.CreateEntry("overrides/config/player.txt").Open()))
            writer.Write("original override");
        if (manifestName == "modrinth.index.json")
        {
            using (var writer = new StreamWriter(archive.CreateEntry("overrides/mods/server.jar").Open()))
                writer.Write("server-only override must not reach client");
            using (var writer = new StreamWriter(archive.CreateEntry("overrides/mods/optional.jar").Open()))
                writer.Write("unselected optional override must not reach client");
        }

        return path;
    }

    /// <summary>Gets or sets the MSTest context supplying this test's cancellation token.</summary>
    public TestContext TestContext { get; set; }
}