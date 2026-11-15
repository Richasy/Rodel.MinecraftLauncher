using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// Verifies local content mutations and recovery against real files, without remote catalogs or runnable game mods.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ContentWorkflowTests
{
    /// <summary>
    /// A selected package may change state or move to recovery, but its source, unrelated files and extensible
    /// tracking must survive. Injected unknown/null tracking catches mutation receipts that narrow persisted records;
    /// this extends the same lifecycle rather than duplicating it in a serialization-only test.
    /// </summary>
    [TestMethod]
    public async Task AddDisableEnableRemove_LocalPackage_PreservesSourceAndRecoveryBytes()
    {
        using var cli = new CliSandbox();
        var fixture = await ContentFixture.CreateAsync(cli, TestContext.CancellationToken);
        var original = await File.ReadAllBytesAsync(fixture.Source, TestContext.CancellationToken);
        var active = Path.Combine(fixture.Mods, "selected.jar");

        (await cli.RunAsync("content", "add", fixture.InstanceId, fixture.Source)).ReadEnvelope(0);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(active, TestContext.CancellationToken));
        var lockPath = Path.Combine(Path.GetDirectoryName(fixture.Mods)!, ".rodel", "content-lock.json");
        var tracking = JsonNode.Parse(await File.ReadAllTextAsync(lockPath, TestContext.CancellationToken))!;
        tracking["futureLock"] = new JsonObject { ["nullable"] = null };
        tracking["files"]![0]!["futureEntry"] = new JsonArray("retained", null, 17);
        await File.WriteAllTextAsync(lockPath, tracking.ToJsonString(), TestContext.CancellationToken);
        var disabled = (await cli.RunAsync("content", "disable", fixture.InstanceId, "selected.jar")).ReadEnvelope(0);
        Assert.IsFalse(disabled["data"]?["enabled"]?.GetValue<bool>());
        Assert.IsTrue(JsonNode.DeepEquals(tracking["files"]![0]!["futureEntry"], disabled["data"]?["futureEntry"]));
        Assert.IsFalse(File.Exists(active));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(active + ".disabled", TestContext.CancellationToken));

        var policy = (await cli.RunAsync("content", "policy", fixture.InstanceId, "selected.jar.disabled", "--channel", "beta")).ReadEnvelope(0);
        Assert.AreEqual("beta", policy["data"]?["channel"]?.GetValue<string>());
        Assert.IsFalse(policy["data"]?["enabled"]?.GetValue<bool>(), "A policy update must not enable a disabled package.");
        var enabled = (await cli.RunAsync("content", "enable", fixture.InstanceId, "selected.jar.disabled")).ReadEnvelope(0);
        Assert.IsTrue(JsonNode.DeepEquals(tracking["files"]![0]!["futureEntry"], enabled["data"]?["futureEntry"]));
        var retained = JsonNode.Parse(await File.ReadAllTextAsync(lockPath, TestContext.CancellationToken))!;
        Assert.IsTrue(retained["futureLock"]!.AsObject().ContainsKey("nullable"));
        Assert.IsNull(retained["futureLock"]!["nullable"]);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(active, TestContext.CancellationToken));
        Assert.IsFalse(File.Exists(active + ".disabled"));
        var removed = (await cli.RunAsync("content", "remove", fixture.InstanceId, "selected.jar")).ReadEnvelope(0);

        // Deletion is recoverable, not a loss of either the original source or the installed bytes.
        Assert.IsFalse(File.Exists(active));
        var recovery = removed["data"]!["recoveryPath"]!.GetValue<string>();
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(recovery, TestContext.CancellationToken));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(fixture.Source, TestContext.CancellationToken));
        Assert.AreEqual("unselected", await File.ReadAllTextAsync(Path.Combine(fixture.Mods, "notes.txt"), TestContext.CancellationToken));
    }

    /// <summary>
    /// Importing a same-named but different package must not overwrite or silently reactivate a disabled file.
    /// </summary>
    [TestMethod]
    public async Task Add_ConflictingDisabledPackage_RejectsWithoutReplacingEitherFile()
    {
        using var cli = new CliSandbox();
        var fixture = await ContentFixture.CreateAsync(cli, TestContext.CancellationToken);
        var disabled = Path.Combine(fixture.Mods, "selected.jar.disabled");
        ContentFixture.WritePackage(disabled, "existing disabled package");
        var existing = await File.ReadAllBytesAsync(disabled, TestContext.CancellationToken);
        var source = await File.ReadAllBytesAsync(fixture.Source, TestContext.CancellationToken);

        var failure = (await cli.RunAsync("content", "add", fixture.InstanceId, fixture.Source)).ReadEnvelope(1);

        Assert.AreEqual("CONTENT_IMPORT_CONFLICT", failure["error"]?["code"]?.GetValue<string>());
        CollectionAssert.AreEqual(existing, await File.ReadAllBytesAsync(disabled, TestContext.CancellationToken));
        CollectionAssert.AreEqual(source, await File.ReadAllBytesAsync(fixture.Source, TestContext.CancellationToken));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Mods, "selected.jar")));
    }

    /// <summary>
    /// A partial batch must retain successful items, identify the failed source, and allow retrying only that source.
    /// </summary>
    [TestMethod]
    public async Task AddBatch_MissingSource_ReportsPartialEffectsAndAllowsTargetedRetry()
    {
        using var cli = new CliSandbox();
        var fixture = await ContentFixture.CreateAsync(cli, TestContext.CancellationToken);
        var missing = Path.Combine(Path.GetDirectoryName(fixture.Source)!, "retry.jar");
        var original = await File.ReadAllBytesAsync(fixture.Source, TestContext.CancellationToken);

        var failure = (await cli.RunAsync("content", "add", fixture.InstanceId, fixture.Source, missing)).ReadEnvelope(1);

        Assert.AreEqual("CONTENT_BATCH_PARTIAL", failure["error"]?["code"]?.GetValue<string>());
        Assert.AreEqual(1, failure["data"]?["succeededCount"]?.GetValue<int>());
        Assert.AreEqual(1, failure["data"]?["failedCount"]?.GetValue<int>());
        var failedItem = failure["data"]!["items"]!.AsArray().Single(item => item?["status"]?.GetValue<string>() == "failed");
        Assert.AreEqual(missing, failedItem?["item"]?.GetValue<string>());
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(Path.Combine(fixture.Mods, "selected.jar"), TestContext.CancellationToken));

        // Repair the failed input without replaying the already successful part of the batch.
        ContentFixture.WritePackage(missing, "repaired input");
        (await cli.RunAsync("content", "add", fixture.InstanceId, missing)).ReadEnvelope(0);
        CollectionAssert.AreEqual(await File.ReadAllBytesAsync(missing, TestContext.CancellationToken), await File.ReadAllBytesAsync(Path.Combine(fixture.Mods, "retry.jar"), TestContext.CancellationToken));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(Path.Combine(fixture.Mods, "selected.jar"), TestContext.CancellationToken));
    }

    /// <summary>Gets or sets the MSTest context supplying this test's cancellation token.</summary>
    public TestContext TestContext { get; set; }
}