// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Content;
using Sprout.Reactive;

namespace Rodel.MinecraftLauncher.Rendering.Tests;

/// <summary>Protects the UI read scope, not Core world discovery or native control wiring.</summary>
[TestClass]
public sealed class WorldListStoreTests
{
    /// <summary>Gets or sets cancellation for the controlled read lifetimes.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// A slow A read may ignore cancellation while B completes. The production Store must not show A's same-named
    /// world, recovery count or error under B; controlled completions reach the race without timers or Core mocks.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SwitchingInstance_DropsLateSuccessAndFailure(bool failOld)
    {
        var pending = new PendingReads();
        using var store = new WorldListStore(pending.Read);
        var first = store.SelectAsync("a");
        var second = store.SelectAsync("b");
        Assert.IsTrue(pending.Calls[0].Token.IsCancellationRequested);
        Assert.HasCount(0, store.Rows);
        pending.Calls[1].Complete(new("b", [World("b")], 2, "b recovery warning"));
        await second;
        Assert.AreEqual("b", store.Rows.Single().InstanceId);
        Assert.AreNotEqual(World("a").Key, store.Rows.Single().Key);
        if (failOld) pending.Calls[0].Fail(new IOException("old read failed"));
        else pending.Calls[0].Complete(new("a", [World("a")], 9, "old warning"));
        await first;
        Assert.AreEqual("b", store.InstanceId);
        Assert.AreEqual("b", store.Rows.Single().InstanceId);
        Assert.AreEqual(2, store.RecoveryCount);
        Assert.AreEqual("b recovery warning", store.Error);
        Assert.IsFalse(store.Loading);
    }

    /// <summary>
    /// Refreshing an existing instance must retain the real rows on failure and pass them to the next read for
    /// thumbnail reuse. Switching targets must clear them immediately, including while the new read is pending.
    /// </summary>
    [TestMethod]
    public async Task Refresh_PreservesOwnRowsAndRecovery_WithoutLeakingToAnotherInstance()
    {
        var pending = new PendingReads();
        using var store = new WorldListStore(pending.Read);
        var initial = store.SelectAsync("a");
        var world = World("a");
        pending.Calls[0].Complete(new("a", [world], 3, string.Empty));
        await initial;
        var refresh = store.SelectAsync("a", force: true);
        Assert.AreSame(world, store.Rows.Single());
        Assert.AreSame(world, pending.Calls[1].Request.Previous.Single());
        pending.Calls[1].Fail(new IOException("refresh failed"));
        await refresh;
        Assert.AreSame(world, store.Rows.Single());
        Assert.AreEqual(3, store.RecoveryCount);
        Assert.AreEqual("refresh failed", store.Error);
        var next = store.SelectAsync("b");
        Assert.HasCount(0, store.Rows);
        Assert.AreEqual(0, store.RecoveryCount);
        Assert.HasCount(0, pending.Calls[2].Request.Previous);
        pending.Calls[2].Complete(new("b", [], 0, string.Empty));
        await next;
        Assert.AreEqual(string.Empty, store.Error);
    }

    /// <summary>
    /// Disposal must request cancellation without relying on the reader to cooperate. An observer left alive
    /// across that boundary witnesses any late state write, unlike a test that merely checks a canceled token.
    /// </summary>
    [TestMethod]
    public async Task Dispose_NonCooperatingReadCannotPublishIntoReleasedScope()
    {
        var pending = new PendingReads();
        using var store = new WorldListStore(pending.Read);
        var notifications = 0;
        using var effect = new Effect(() => { _ = store.Rows; _ = store.Loading; _ = store.Error; notifications++; });
        var read = store.SelectAsync("a");
        store.Dispose();
        var before = notifications;
        Assert.IsTrue(pending.Calls[0].Token.IsCancellationRequested);
        pending.Calls[0].Complete(new("a", [World("a")], 1, "late warning"));
        await read;
        Assert.HasCount(0, store.Rows);
        Assert.AreEqual(before, notifications);
    }

    private static WorldItem World(string instance) => new(instance, "same-folder", "World", "26.3", 0, false, null, null, [1, 2]);

    /// <summary>
    /// A dispatcher queues Loading, so changing only the selected ID can expose the previous operation's error.
    /// Inspect every reactive observation before draining B, then prove B's own first-load failure is not hidden.
    /// </summary>
    [TestMethod]
    public async Task SwitchingAfterFailure_NeverPublishesPreviousErrorUnderNewInstance()
    {
        var pending = new PendingReads();
        var scheduler = new QueueScheduler();
        using var store = new WorldListStore(pending.Read, scheduler);
        var seen = new List<(string Instance, int Rows, int Recovery, string Error)>();
        using var effect = new Effect(() => seen.Add((store.InstanceId, store.Rows.Count, store.RecoveryCount, store.Error)));
        var first = store.SelectAsync("a");
        scheduler.Drain();
        pending.Calls[0].Fail(new IOException("a failed"));
        scheduler.Drain();
        await first;
        var start = seen.Count;
        var second = store.SelectAsync("b");
        try
        {
            Assert.AreEqual(string.Empty, store.Error);
            Assert.IsTrue(seen.Skip(start).All(item => item.Instance != "b" || (item.Rows == 0 && item.Recovery == 0 && item.Error.Length == 0)));
            scheduler.Drain();
            pending.Calls[1].Fail(new IOException("b failed"));
            scheduler.Drain();
            await second;
            Assert.AreEqual("b failed", store.Error);
        }
        finally
        {
            pending.Calls[1].Completion.TrySetCanceled(TestContext.CancellationToken);
            scheduler.Drain();
            await second;
        }
    }

    /// <summary>
    /// A→B→A is a new selection scope, not an A cache hit. Complete the old B after returning and inspect every
    /// reactive combination, so terminal-only assertions cannot miss briefly or persistently resurrected A rows.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReturningToPreviousInstance_DoesNotResurrectItsOldSnapshot(bool failOld)
    {
        var pending = new PendingReads();
        var scheduler = new QueueScheduler();
        using var store = new WorldListStore(pending.Read, scheduler);
        var first = store.SelectAsync("a");
        scheduler.Drain();
        pending.Calls[0].Complete(new("a", [World("a")], 3, "old a warning"));
        scheduler.Drain();
        await first;
        var observations = new List<(string Instance, int Rows, int Recovery, string Error)>();
        using var effect = new Effect(() => observations.Add((store.InstanceId, store.Rows.Count, store.RecoveryCount, store.Error)));
        var second = store.SelectAsync("b");
        scheduler.Drain();
        var start = observations.Count;
        var returned = store.SelectAsync("a");
        try
        {
            Assert.HasCount(0, store.Rows);
            Assert.IsTrue(observations.Skip(start).All(item => item.Instance != "a" || (item.Rows == 0 && item.Recovery == 0 && item.Error.Length == 0)));
            scheduler.Drain();
            if (failOld) pending.Calls[1].Fail(new IOException("late b failure"));
            else pending.Calls[1].Complete(new("b", [World("b")], 8, "late b warning"));
            scheduler.Drain();
            await second;
            Assert.HasCount(0, store.Rows);
            Assert.HasCount(0, pending.Calls[2].Request.Previous);
            pending.Calls[2].Complete(new("a", [], 0, "fresh a warning"));
            scheduler.Drain();
            await returned;
            Assert.HasCount(0, store.Rows);
            Assert.AreEqual("fresh a warning", store.Error);
        }
        finally
        {
            pending.Calls[1].Completion.TrySetCanceled(TestContext.CancellationToken);
            pending.Calls[2].Completion.TrySetCanceled(TestContext.CancellationToken);
            scheduler.Drain();
            await Task.WhenAll(second, returned);
        }
    }

    private sealed class QueueScheduler : IScheduler
    {
        private readonly Queue<Action> _callbacks = [];
        /// <inheritdoc/>
        public DateTimeOffset Now => DateTimeOffset.UnixEpoch;
        /// <inheritdoc/>
        public void Post(Action action) => _callbacks.Enqueue(action);
        /// <inheritdoc/>
        public IDisposable Schedule(TimeSpan delay, Action callback) => throw new NotSupportedException("This read fixture only schedules Post callbacks.");
        internal void Drain() { while (_callbacks.TryDequeue(out var action)) action(); }
    }

    private sealed class PendingReads
    {
        internal List<PendingRead> Calls { get; } = [];
        internal Task<WorldListSnapshot> Read(WorldListRead request, CancellationToken token)
        {
            var call = new PendingRead(request, token);
            Calls.Add(call);
            return call.Completion.Task;
        }
    }

    private sealed class PendingRead(WorldListRead request, CancellationToken token)
    {
        internal WorldListRead Request { get; } = request;
        internal CancellationToken Token { get; } = token;
        internal TaskCompletionSource<WorldListSnapshot> Completion { get; } = new();
        internal void Complete(WorldListSnapshot value) => Completion.SetResult(value);
        internal void Fail(Exception error) => Completion.SetException(error);
    }
}