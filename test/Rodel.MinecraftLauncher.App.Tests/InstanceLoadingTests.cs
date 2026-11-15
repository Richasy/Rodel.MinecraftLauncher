// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects responsive inventory publication and shared detail reads without a packaged window.</summary>
[TestClass]
public sealed class InstanceLoadingTests
{
    /// <summary>
    /// Review: home, management and server surfaces must share an expensive read. Hold the producer after entry,
    /// cancel one actual waiter, and verify that the other waiter and a later cache hit retain that same result.
    /// The producer is substituted; existing memory cases separately cover the real Core reader.
    /// </summary>
    [TestMethod]
    public async Task SharedDetails_OneCallerLeaves_OtherReadersKeepTheSameRead()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new HomeInstanceDetails("26.3", "Fabric", 2, null, false, 2048);
        var reads = 0;
        CancellationToken producerToken = default;
        using var cache = new InstanceDetailsCache(async (_, token) =>
        {
            Interlocked.Increment(ref reads);
            producerToken = token;
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return expected;
        });
        using var leaving = new CancellationTokenSource();
        var first = cache.ReadAsync("instance", leaving.Token);
        await entered.Task.WaitAsync(TestContext.CancellationToken);
        var second = cache.ReadAsync("instance", TestContext.CancellationToken);
        try
        {
            leaving.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => first);
            Assert.IsFalse(producerToken.IsCancellationRequested, "Leaving one surface must not cancel another surface's read.");
            Assert.AreEqual(1, reads);
        }
        finally
        {
            release.TrySetResult();
            await second;
        }

        Assert.AreSame(expected, await second);
        Assert.AreSame(expected, await cache.ReadAsync("instance", TestContext.CancellationToken));
        Assert.AreEqual(1, reads, "Returning to the same instance must not scan its content again.");
    }

    /// <summary>
    /// Review: an operation can invalidate details while the old read is still in flight. Complete the newer
    /// read first and the obsolete read last; the obsolete result must not repopulate the current cache.
    /// </summary>
    [TestMethod]
    public async Task SharedDetails_InvalidatedDuringRead_LateResultCannotReplaceNewDetails()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HomeInstanceDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new HomeInstanceDetails("26.3", "Fabric", 2, null, false, 2048);
        var updated = original with { MemoryMb = 4096 };
        var reads = 0;
        using var cache = new InstanceDetailsCache((_, _) =>
        {
            if (Interlocked.Increment(ref reads) != 1)
                return Task.FromResult(updated);
            entered.SetResult();
            return release.Task;
        });
        var obsolete = cache.ReadAsync("instance", TestContext.CancellationToken);
        await entered.Task.WaitAsync(TestContext.CancellationToken);
        try
        {
            cache.Invalidate();
            Assert.AreSame(updated, await cache.ReadAsync("instance", TestContext.CancellationToken));
        }
        finally
        {
            release.TrySetResult(original);
            await obsolete;
        }

        Assert.AreSame(updated, await cache.ReadAsync("instance", TestContext.CancellationToken));
        Assert.AreEqual(2, reads);
    }

    /// <summary>A transient read error must remain retryable, rather than pinning a failed task for the rest of the App lifetime.</summary>
    [TestMethod]
    public async Task SharedDetails_ReadFails_NextRequestCanRecover()
    {
        var expected = new HomeInstanceDetails("26.3", "Fabric", 2, null, false, 2048);
        var reads = 0;
        using var cache = new InstanceDetailsCache((_, _) => Interlocked.Increment(ref reads) == 1
            ? Task.FromException<HomeInstanceDetails>(new IOException("Temporarily unreadable configuration."))
            : Task.FromResult(expected));
        await Assert.ThrowsExactlyAsync<IOException>(() => cache.ReadAsync("instance", TestContext.CancellationToken));
        Assert.AreSame(expected, await cache.ReadAsync("instance", TestContext.CancellationToken));
        Assert.AreEqual(2, reads);
    }

    /// <summary>
    /// Review: the first usable inventory must not wait for inherited loader metadata. Observe real home
    /// publications against two tiny original-format profiles and a real interrupted installation:
    /// registration first, resolved subtitle and recovery later. The initial publication is an exact
    /// checkpoint before enrichment, so putting history back on the critical path fails this case.
    /// </summary>
    [TestMethod]
    public void Inventory_PublishesRegistrationBeforeEnrichingInheritedMetadata() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        var instance = await scope.Client.Instances.CreateAsync("Inherited profile", "fabric-test", TestContext.CancellationToken);
        foreach (var (id, metadata) in new[]
        {
            ("fabric-test", """{"id":"fabric-test","inheritsFrom":"26.3","libraries":[{"name":"net.fabricmc:fabric-loader:0.18.0"}]}"""),
            ("26.3", """{"id":"26.3","mainClass":"net.minecraft.client.main.Main","libraries":[]}""")
        })
        {
            var directory = Path.Combine(instance.MinecraftDirectory, "versions", id);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, id + ".json"), metadata, TestContext.CancellationToken);
        }

        var queued = await scope.Client.Tasks.QueueAsync(new InstallInstanceTask(instance.Id, new()), cancellationToken: TestContext.CancellationToken);
        var jobPath = Path.Combine(scope.Client.Paths.JobsDirectory, queued.Id + ".json");
        var job = JsonFiles.ReadObject(jobPath);
        job["status"] = "canceled";
        await JsonFiles.WriteAsync(jobPath, job, TestContext.CancellationToken);
        using var home = new HomeController(scope.Client);
        var inventories = new List<IReadOnlyList<InstanceSummary>>();
        HomePageState? firstInventory = null;
        home.StateChanged += state =>
        {
            if (state.InstancesLoaded && (inventories.Count == 0 || !ReferenceEquals(inventories[^1], state.Instances)))
            {
                firstInventory ??= state;
                inventories.Add(state.Instances);
            }
        };
        Assert.IsFalse(home.State.InstancesLoaded);
        await home.RefreshAsync();
        Assert.HasCount(1, inventories[0]);
        Assert.AreEqual(instance.Id, inventories[0][0].Id);
        Assert.AreEqual("fabric-test", inventories[0][0].Version, "Registration must be visible before parsing inherited profiles.");
        Assert.IsNull(inventories[0][0].Loader);
        Assert.IsEmpty(firstInventory!.InstallationRecoveries, "The inventory must be published before inspecting installation history.");
        Assert.AreEqual(queued.Id, home.State.InstallationRecoveries.Single().JobId, "Deferred recovery must still find the interrupted installation.");
        Assert.AreEqual("26.3", home.State.Instances[0].Version);
        Assert.AreEqual("Fabric", home.State.Instances[0].Loader);
        Assert.AreEqual(instance.Id, home.State.SelectedId);
    });

    /// <summary>
    /// Review: isolated cache tests cannot prove that real settings operations invalidate the shared App snapshot.
    /// Change Core's persisted heap limit, refresh through the actual home owner, and read the shared result again.
    /// </summary>
    [TestMethod]
    public void Inventory_RefreshAfterSettingsChange_InvalidatesSharedDetails() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        var instance = await scope.Client.Instances.CreateAsync("Refreshed details", "26.3", TestContext.CancellationToken);
        await scope.Client.Settings.UpdateAsync(new JsonObject { ["memoryMode"] = "manual", ["maxMemoryMb"] = 2048 },
            instance: instance, cancellationToken: TestContext.CancellationToken);
        using var home = new HomeController(scope.Client);
        await home.RefreshAsync();
        var original = await home.ReadInstanceDetailsAsync(instance.Id, TestContext.CancellationToken);
        Assert.AreEqual(2048, original.MemoryMb);
        Assert.AreSame(original, await home.ReadInstanceDetailsAsync(instance.Id, TestContext.CancellationToken));

        await scope.Client.Settings.SetAsync("maxMemoryMb", JsonValue.Create(4096), instance, TestContext.CancellationToken);
        await home.RefreshAsync();
        var updated = await home.ReadInstanceDetailsAsync(instance.Id, TestContext.CancellationToken);
        Assert.AreEqual(4096, updated.MemoryMb);
        Assert.AreNotSame(original, updated);
    });

    /// <summary>Supplies cancellation for controlled reads and isolated Core storage.</summary>
    public TestContext TestContext { get; set; }
}