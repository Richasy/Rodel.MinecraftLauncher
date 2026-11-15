// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Instances;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects list reuse and authoritative removal while instance detail reads are still pending.</summary>
[TestClass]
public sealed class InstanceListLifetimeTests
{
    /// <summary>
    /// Review: unlike the removal cases, navigation does not change inventory. Reenter the same revision
    /// while hydration is held, then after it completes: neither request may restart the read or replace
    /// the settled row snapshot. Initial rows must be usable while their detail producer is held.
    /// </summary>
    [TestMethod]
    public void SameInventory_ReentrySharesPendingReadAndKeepsSettledRows() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        var instance = await scope.Client.Instances.CreateAsync("Retained list", "26.3", TestContext.CancellationToken);
        InstanceSummary[] summaries = [new(instance.Id, instance.Name, "26.3", false)];
        var release = new TaskCompletionSource<IReadOnlyList<ManagedInstance>>();
        var reads = 0;
        using var store = new InstanceListStore((_, progress, _) =>
        {
            reads++;
            progress.Report([new(instance, null, null)]);
            return release.Task;
        });
        var first = store.EnsureAsync(1, summaries);
        var reentry = store.EnsureAsync(1, [.. summaries]);
        try
        {
            Assert.IsTrue(store.Loading, "A pending inventory read must not be presented as a settled empty list.");
            Assert.AreEqual(1, reads, "Changing the visible tab must not restart pending hydration.");
            await Task.Yield();
            Assert.HasCount(1, store.Rows, "Registered rows must not wait for their content details.");
            Assert.IsFalse(first.IsCompleted, "The detail producer is still held at the checkpoint.");
        }
        finally
        {
            release.TrySetResult([new(instance, null, null)]);
            await Task.WhenAll(first, reentry);
        }

        var settled = store.Rows;
        Assert.IsFalse(store.Loading);
        await store.EnsureAsync(1, [.. summaries]);
        Assert.AreEqual(1, reads);
        Assert.AreSame(settled, store.Rows, "Returning to a loaded tab must retain the existing rows.");
        Assert.AreEqual(instance.Id, store.Rows[0].Id);
    });

    /// <summary>A removed registration must leave the actionable list before slow survivor details finish; an older read cannot resurrect it.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Removal_InvalidatesRowsBeforeDetailsComplete(bool obsoleteReadFails) => StateTestScope.Run(async () =>
    {
        // Existing home/screenshot cases do not cover the separate instance-list hydration.
        // Gates hold two actual refresh calls after entry: assertions run before either read completes,
        // then release the obsolete result last to distinguish membership removal from eventual refresh.
        using var scope = new StateTestScope();
        var first = await scope.Client.Instances.CreateAsync("First", "26.3", TestContext.CancellationToken);
        var second = await scope.Client.Instances.CreateAsync("Second", "26.3", TestContext.CancellationToken);
        ManagedInstance[] initial = [new(first, null, null), new(second, null, null)];
        InstanceSummary[] summaries = [new(first.Id, first.Name, "26.3", false), new(second.Id, second.Name, "26.3", false)];
        var oldRead = new TaskCompletionSource<IReadOnlyList<ManagedInstance>>();
        var newRead = new TaskCompletionSource<IReadOnlyList<ManagedInstance>>();
        IProgress<IReadOnlyList<ManagedInstance>>? obsoleteProgress = null;
        var reads = 0;
        using var store = new InstanceListStore((_, progress, _) =>
        {
            reads++;
            if (reads == 2)
                obsoleteProgress = progress;
            return reads switch
            {
                1 => Task.FromResult<IReadOnlyList<ManagedInstance>>(initial),
                2 => oldRead.Task,
                _ => newRead.Task,
            };
        });
        await store.RefreshAsync(summaries);
        var obsolete = store.RefreshAsync(summaries);
        await scope.Client.Instances.RemoveAsync(first.Id, cancellationToken: TestContext.CancellationToken);
        var current = store.RefreshAsync([summaries[1]]);
        try
        {
            Assert.AreEqual(3, reads, "Both pending reads must have entered before checking membership.");
            Assert.HasCount(1, store.Rows, "Deletion must invalidate the row synchronously, before asynchronous detail refresh.");
            Assert.AreEqual(second.Id, store.Rows[0].Id);
            obsoleteProgress!.Report(initial);
            await Task.Yield();
            Assert.HasCount(1, store.Rows, "Queued progress from a superseded read must not restore a deleted row.");
        }
        finally
        {
            newRead.SetResult([initial[1]]);
            await current;
            if (obsoleteReadFails)
                oldRead.SetException(new IOException("Obsolete inventory read failed."));
            else
                oldRead.SetResult(initial);
            await obsolete;
        }

        Assert.HasCount(1, store.Rows);
        Assert.AreEqual(second.Id, store.Rows[0].Id);
        Assert.AreEqual(string.Empty, store.Error, "A superseded read must not replace the current error state.");
        Assert.AreEqual(second.Name, scope.Client.Instances.Get(second.Id).Name);
    });

    /// <summary>Supplies the test runner's cancellation token for isolated Core operations.</summary>
    public TestContext TestContext { get; set; }
}