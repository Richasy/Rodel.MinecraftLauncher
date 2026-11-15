using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects frozen pack update agreement and compensation after content publication, not initial import or game compatibility.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class PackUpdateTests
{
    /// <summary>
    /// Direct updates and frozen apply must restore content and runtime selection when real installation fails.
    /// The local version profile deliberately has no client JAR or URL, reaching a deterministic installer fault
    /// without network. Completion tests retain new imports; this instead protects an existing working baseline.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Update_RuntimeFailure_RestoresPreviousContentAndSelection(bool frozen)
    {
        using var cli = new CliSandbox();
        var (client, id, incoming) = await CreateUpdateAsync(cli);
        var instance = client.Instances.Get(id);
        var metadata = JsonFiles.ReadObject(instance.MetadataPath);
        metadata["settings"] = new JsonObject { ["versionId"] = "previous-runtime", ["loader"] = "previous-loader", ["memoryMb"] = 1536 };
        await JsonFiles.WriteAsync(instance.MetadataPath, metadata, TestContext.CancellationToken);
        var lockFile = Path.Combine(instance.GameDirectory, ".rodel", "pack-lock.json");
        var previousPack = await File.ReadAllTextAsync(lockFile, TestContext.CancellationToken);
        var arguments = new[] { "pack", "update", id, incoming };
        if (frozen)
        {
            var review = (await cli.RunAsync("pack", "review", id, incoming, "--offline")).ReadEnvelope(0)["data"]!;
            arguments = ["pack", "apply", review["operationId"]!.GetValue<string>()];
        }

        var failed = (await cli.RunAsync(arguments)).ReadEnvelope(1);

        Assert.AreEqual("PACK_RUNTIME_FAILED", failed["error"]?["code"]?.ToString(), failed.ToJsonString());
        Assert.AreEqual("rolledBack", failed["data"]?["phase"]?.ToString());
        Assert.AreEqual("runtime", failed["data"]?["preparationFailure"]?["stage"]?.ToString());
        Assert.IsNull(failed["data"]?["consistency"], "Agreement for the rolled-back release must not describe current content.");
        Assert.AreEqual(previousPack, await File.ReadAllTextAsync(lockFile, TestContext.CancellationToken));
        Assert.AreEqual("original", await File.ReadAllTextAsync(Path.Combine(instance.GameDirectory, "config", "changed.txt"), TestContext.CancellationToken));
        var restored = JsonFiles.ReadObject(instance.MetadataPath);
        Assert.AreEqual("previous-runtime", restored["settings"]?["versionId"]?.ToString());
        Assert.AreEqual("previous-loader", restored["settings"]?["loader"]?.ToString());
        Assert.AreEqual(1536, restored["settings"]?["memoryMb"]?.GetValue<int>());
        var operation = failed["data"]!["operationId"]!.GetValue<string>();
        Assert.AreEqual("rolledBack", JsonFiles.ReadObject(Path.Combine(cli.Root, "packs", "updates", operation, "review.json"))["phase"]?.ToString());
    }

    /// <summary>
    /// Cancel at the published-content/runtime boundary, not before dispatch. Compensation must expose its
    /// durable result and preserve an edit made after publication instead of blindly restoring the old bytes.
    /// This differs from a runtime error and from preparation cancellation, which has no update to undo.
    /// </summary>
    [TestMethod]
    public async Task Update_CancelAfterPublication_RollsBackAndRetainsLaterPlayerEdit()
    {
        using var cli = new CliSandbox();
        var (client, id, incoming) = await CreateUpdateAsync(cli);
        var game = client.Instances.Get(id).GameDirectory;
        var changed = Path.Combine(game, "config", "changed.txt");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var reachedBoundary = false;
        var observed = new LauncherClient(new LauncherPaths(cli.Root), progress: new UpdateProgress(value =>
        {
            if (value.Stage != "pack-runtime") return;
            Assert.AreEqual("replacement", File.ReadAllText(changed));
            var operation = value.OperationId!;
            Assert.AreEqual("applied", JsonFiles.ReadObject(Path.Combine(cli.Root, "packs", "updates", operation, "review.json"))["phase"]?.ToString());
            reachedBoundary = true;
            File.WriteAllText(changed, "later player edit");
            cancellation.Cancel();
        }));

        var canceled = await Assert.ThrowsAsync<LauncherOperationCanceledException>(
            () => observed.UpdatePackAsync(id, incoming, cancellationToken: cancellation.Token));

        Assert.IsTrue(reachedBoundary, "The token must be canceled only after the actual content commit.");
        Assert.AreEqual("rolledBack", canceled.Details["phase"]?.ToString());
        Assert.AreEqual("later player edit", await File.ReadAllTextAsync(changed, TestContext.CancellationToken));
        var conflict = canceled.Details["rollback"]!["rollbackConflicts"]!.AsArray().Single()!;
        Assert.AreEqual("config/changed.txt", conflict["path"]?.ToString());
        Assert.AreEqual("original", await File.ReadAllTextAsync(conflict["previousFile"]!.GetValue<string>(), TestContext.CancellationToken));
        Assert.AreEqual("original", await File.ReadAllTextAsync(Path.Combine(game, "config", "removed.txt"), TestContext.CancellationToken));
    }

    /// <summary>
    /// Available downloads do not prove release agreement: unchanged upstream files can preserve local edits,
    /// and changed upstream files can conflict. Both differences must be visible without deleting player data
    /// or turning unrelated extra files into mismatches. Existing completion cases do not exercise this contract.
    /// </summary>
    [TestMethod]
    public async Task Apply_PreservesPlayerChanges_SeparatesAvailabilityFromReleaseAgreement()
    {
        using var cli = new CliSandbox();
        var (client, id, incoming) = await CreateUpdateAsync(cli);
        var game = client.Instances.Get(id).GameDirectory;
        await File.WriteAllTextAsync(Path.Combine(game, "config", "changed.txt"), "player conflict", TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(game, "config", "unchanged.txt"), "player preference", TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(game, "extra.txt"), "unmanaged", TestContext.CancellationToken);
        var review = (await cli.RunAsync("pack", "review", id, incoming, "--offline")).ReadEnvelope(0)["data"]!;

        var applied = (await cli.RunAsync("pack", "apply", review["operationId"]!.GetValue<string>(), "--content-only")).ReadEnvelope(0)["data"]!;

        Assert.IsTrue(applied["complete"]!.GetValue<bool>());
        Assert.IsTrue(applied["consistency"]!["filesAvailable"]!.GetValue<bool>());
        Assert.IsFalse(applied["consistency"]!["matchesRelease"]!.GetValue<bool>());
        CollectionAssert.AreEquivalent(ExpectedDifferences,
            applied["consistency"]!["differences"]!.AsArray().Select(item => item!["path"]!.GetValue<string>()).ToArray());
        Assert.HasCount(1, applied["conflicts"]!.AsArray(), "An upstream-unchanged player preference is a difference, not an apply conflict.");
        Assert.AreEqual("player conflict", await File.ReadAllTextAsync(Path.Combine(game, "config", "changed.txt"), TestContext.CancellationToken));
        Assert.AreEqual("player preference", await File.ReadAllTextAsync(Path.Combine(game, "config", "unchanged.txt"), TestContext.CancellationToken));
        Assert.AreEqual("unmanaged", await File.ReadAllTextAsync(Path.Combine(game, "extra.txt"), TestContext.CancellationToken));
        Assert.IsFalse(File.Exists(Path.Combine(game, "config", "removed.txt")));
        var persisted = client.Packs.GetState(id).Consistency!;
        Assert.AreEqual(applied["consistency"]!["filesAvailable"]!.GetValue<bool>(), persisted.FilesAvailable);
        Assert.AreEqual(applied["consistency"]!["matchesRelease"]!.GetValue<bool>(), persisted.MatchesRelease);
        CollectionAssert.AreEquivalent(ExpectedDifferences, persisted.Differences.Select(item => item.Path).ToArray());
    }

    private async Task<(LauncherClient Client, string Id, string Incoming)> CreateUpdateAsync(CliSandbox cli)
    {
        Directory.CreateDirectory(cli.Root);
        var original = WriteArchive(cli.Root, "original.mrpack", false);
        var incoming = WriteArchive(cli.Root, "incoming.mrpack", true);
        var client = new LauncherClient(new LauncherPaths(cli.Root));
        var imported = await client.Packs.ImportAsync(original, download: false, cancellationToken: TestContext.CancellationToken);
        var id = imported.Instance!.Id;
        var instance = client.Instances.Get(id);
        var version = Path.Combine(instance.MinecraftDirectory, "versions", "update-fixture");
        Directory.CreateDirectory(version);
        await File.WriteAllTextAsync(Path.Combine(version, "update-fixture.json"),
            """{"id":"update-fixture","mainClass":"fixture.Main","libraries":[],"arguments":{"game":[],"jvm":[]},"downloads":{"client":{}}}""",
            TestContext.CancellationToken);
        return (client, id, incoming);
    }

    /// <summary>
    /// An older undo must not restore stale pack metadata over a later transaction. A second content-only
    /// application deliberately reuses the same release: even a no-op update has a durable owner that must
    /// be unwound first. Existing player-edit rollback tests do not protect this transaction ordering.
    /// </summary>
    [TestMethod]
    public async Task Rollback_OlderThanCurrentOperation_RejectsWithoutChangingClient()
    {
        using var cli = new CliSandbox();
        var (client, id, incoming) = await CreateUpdateAsync(cli);
        var first = (await cli.RunAsync("pack", "update", id, incoming, "--content-only")).ReadEnvelope(0)["data"]!;
        var second = (await cli.RunAsync("pack", "update", id, incoming, "--content-only")).ReadEnvelope(0)["data"]!;
        var lockFile = Path.Combine(client.Instances.Get(id).GameDirectory, ".rodel", "pack-lock.json");
        var before = await File.ReadAllTextAsync(lockFile, TestContext.CancellationToken);

        (await cli.RunAsync("pack", "rollback", first["operationId"]!.GetValue<string>())).ReadEnvelope(1);

        Assert.AreEqual(before, await File.ReadAllTextAsync(lockFile, TestContext.CancellationToken));
        var file = Path.Combine(client.Instances.Get(id).GameDirectory, "config", "changed.txt");
        Assert.AreEqual("replacement", await File.ReadAllTextAsync(file, TestContext.CancellationToken));
        (await cli.RunAsync("pack", "rollback", second["operationId"]!.GetValue<string>())).ReadEnvelope(0);
        (await cli.RunAsync("pack", "rollback", first["operationId"]!.GetValue<string>())).ReadEnvelope(0);
        Assert.AreEqual("original", await File.ReadAllTextAsync(file, TestContext.CancellationToken));
    }

    private static string WriteArchive(string root, string name, bool updated)
    {
        var path = Path.Combine(root, name);
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        WriteEntry(archive, "modrinth.index.json", new JsonObject
        {
            ["formatVersion"] = 1,
            ["game"] = "minecraft",
            ["name"] = "Update fixture",
            ["versionId"] = updated ? "two" : "one",
            ["dependencies"] = new JsonObject { ["minecraft"] = "update-fixture" },
            ["files"] = new JsonArray()
        }.ToJsonString());
        WriteEntry(archive, "overrides/config/changed.txt", updated ? "replacement" : "original");
        WriteEntry(archive, "overrides/config/unchanged.txt", "original preference");
        if (!updated)
            WriteEntry(archive, "overrides/config/removed.txt", "original");
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(text);
    }

    private sealed class UpdateProgress(Action<LauncherProgress> observe) : IProgress<LauncherProgress>
    {
        /// <summary>Observes the actual post-publication preparation boundary synchronously.</summary>
        public void Report(LauncherProgress value) => observe(value);
    }

    /// <summary>Provides the runner-owned cancellation deadline to each isolated update scenario.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static readonly string[] ExpectedDifferences = ["config/changed.txt", "config/unchanged.txt"];
}