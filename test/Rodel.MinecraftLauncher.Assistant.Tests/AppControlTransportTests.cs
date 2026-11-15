using Rodel.MinecraftLauncher.Core.Application;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Exercises real current-user pipes, discovery and cancellation with a fake App callback, not a real Sprout window.</summary>
[TestClass]
public sealed class AppControlTransportTests
{
    /// <summary>
    /// App discovery must remain visible to the CLI at the shared data root while its authentication record stays excluded
    /// from model file snapshots, even when the whole data root was selected for ordinary file access.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task SharedDiscoveryRecord_IsUsableForControl_ButProtectedFromBroadFileSnapshots()
    {
        using var sandbox = new AiSandbox();
        await using var host = await AppControlHost.StartAsync(sandbox.Paths,
            (_, _) => Task.FromResult(new JsonObject { ["state"] = "real-pipe" }), ct: TestContext.CancellationToken);
        var listed = new AppControlService(sandbox.Paths).List().Items;
        Assert.HasCount(1, listed);
        var record = Path.Combine("app", "hosts", listed[0].Id + ".json");
        Assert.IsTrue(File.Exists(Path.Combine(sandbox.Root, record)));
        var snapshot = new Core.Application.AI.AiFileSnapshot(sandbox.Paths, Path.Combine(sandbox.Root, "snapshot"), null);
        var failure = Assert.Throws<LauncherOperationException>(() => snapshot.Target(sandbox.Root, record));
        Assert.AreEqual("AI_FILE_PROTECTED", failure.Code);
        var state = await new AppControlService(sandbox.Paths).InvokeAsync(listed[0].Id,
            new AppControlRequest { Action = AppControlAction.State }, TestContext.CancellationToken);
        Assert.AreEqual("real-pipe", state["state"]!.ToString());
    }

    /// <summary>
    /// A client must bind to the selected root/process/nonce, not another live-looking App record.
    /// Rejected identity and wrong-root requests must never reach the supplied mutation callback.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task DiscoveredHost_ValidatesRootAndNonce_AndRemovesOwnedRegistrationOnStop()
    {
        using var sandbox = new AiSandbox();
        var registry = Path.Combine(sandbox.Root, "registry");
        var calls = 0;
        await using (var host = await AppControlHost.StartAsync(sandbox.Paths,
            (request, ct) =>
            {
                calls++;
                return Task.FromResult(new JsonObject { ["observed"] = "actual-pipe-callback" });
            }, null, registry, TestContext.CancellationToken))
        {
            var client = new AppControlService(sandbox.Paths, registry);
            var item = client.List().Items.Single();
            var id = item.Id;
            var output = JsonSerializer.SerializeToNode(client.List(), AppControlJsonContext.Default.AppControlList)!;
            Assert.IsNull(output["items"]![0]!["nonce"], "Discovery output must not expose the private endpoint token.");
            var observed = await client.InvokeAsync(id, new AppControlRequest { Action = AppControlAction.State }, TestContext.CancellationToken);
            Assert.AreEqual("actual-pipe-callback", observed["observed"]!.ToString());
            using var other = new AiSandbox();
            var wrongRoot = new AppControlService(other.Paths, registry);
            Assert.HasCount(0, wrongRoot.List().Items);
            var missing = await Assert.ThrowsAsync<LauncherOperationException>(async () =>
                await wrongRoot.InvokeAsync(id, new AppControlRequest { Action = AppControlAction.PreferencesUpdate }, TestContext.CancellationToken));
            Assert.AreEqual("APP_NOT_RUNNING", missing.Code);

            var file = Path.Combine(registry, id + ".json");
            var registration = JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.CancellationToken))!;
            var actualPackage = registration["packageIdentity"]!.GetValue<string>();
            registration["packageIdentity"] = "Forged.Package.Identity";
            await File.WriteAllTextAsync(file, registration.ToJsonString(), TestContext.CancellationToken);
            Assert.HasCount(0, client.List().Items);
            var forged = await Assert.ThrowsAsync<LauncherOperationException>(async () =>
                await client.InvokeAsync(id, new AppControlRequest { Action = AppControlAction.State }, TestContext.CancellationToken));
            Assert.AreEqual("APP_NOT_RUNNING", forged.Code);
            registration["packageIdentity"] = actualPackage;
            registration["nonce"] = new string('0', 64);
            await File.WriteAllTextAsync(file, registration.ToJsonString(), TestContext.CancellationToken);
            var rejected = await Assert.ThrowsAsync<LauncherOperationException>(async () =>
                await client.InvokeAsync(id, new AppControlRequest { Action = AppControlAction.PreferencesUpdate }, TestContext.CancellationToken));
            Assert.AreEqual("APP_CONTROL_IDENTITY_MISMATCH", rejected.Code);
            Assert.AreEqual(1, calls);
        }

        Assert.HasCount(0, Directory.GetFiles(registry, "*.json"));
    }

    /// <summary>
    /// Cancel after the actual server callback starts: disconnect must reach its token before any deferred write,
    /// and a later connection must still work. Starting two tasks alone would not exercise this failure window.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task ClientCancellation_StopsActiveCallbackWithoutReplay_AndListenerAcceptsNextRead()
    {
        using var sandbox = new AiSandbox();
        var registry = Path.Combine(sandbox.Root, "registry");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutationCalls = 0;
        await using var host = await AppControlHost.StartAsync(sandbox.Paths, async (request, ct) =>
        {
            if (request.Action == AppControlAction.State) return new JsonObject { ["state"] = "unchanged" };
            Interlocked.Increment(ref mutationCalls);
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { canceled.SetResult(); throw; }

            throw new AssertFailedException("A canceled deferred mutation cannot finish.");
        }, null, registry, TestContext.CancellationToken);
        var client = new AppControlService(sandbox.Paths, registry);
        var id = client.List().Items[0].Id;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var pending = client.InvokeAsync(id, new AppControlRequest { Action = AppControlAction.PreferencesUpdate }, cancellation.Token);
        await entered.Task.WaitAsync(TestContext.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
        await canceled.Task.WaitAsync(TestContext.CancellationToken);
        var next = await client.InvokeAsync(id, new AppControlRequest { Action = AppControlAction.State }, TestContext.CancellationToken);
        Assert.AreEqual("unchanged", next["state"]!.ToString());
        Assert.AreEqual(1, mutationCalls);
    }

    /// <summary>Gets or sets the bounded cancellation context for actual pipe operations.</summary>
    public TestContext TestContext { get; set; }
}