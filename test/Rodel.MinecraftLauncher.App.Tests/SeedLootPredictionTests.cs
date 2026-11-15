// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Features.SeedExplorer;
using Rodel.MinecraftLauncher.Core.Worlds.Maps;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects manual prediction ownership and recovery independently of native loot-table correctness.</summary>
[TestClass]
public sealed class SeedLootPredictionTests
{
    /// <summary>A container switch must reject the old completion without clearing the newer request's busy state.</summary>
    [TestMethod]
    public async Task SwitchContainer_LateCompletionCannotReplaceNewerRequest()
    {
        // Review: real controller + held API completions reach the supersession window without timing sleeps.
        // Existing map/pan tests do not own inventory requests. Missing request-ownership guards could publish A,
        // or A's finally would clear B's busy state while B remains pending.
        var calls = new List<Pending>();
        using var controller = Create(calls);
        var snapshot = Snapshot();
        controller.SetContext(snapshot, "overworld", Recorded(10));
        var old = controller.StartAsync();
        Assert.HasCount(1, calls);
        controller.SetContext(snapshot, "overworld", Recorded(20));
        var current = controller.StartAsync();
        Assert.HasCount(2, calls);
        Assert.IsTrue(calls[0].Token.IsCancellationRequested);

        calls[0].Completion.SetResult(Predicted(snapshot, calls[0].Recorded));
        await old;
        Assert.IsTrue(controller.Current.Busy);
        Assert.IsNull(controller.Current.Result);
        calls[1].Completion.SetResult(Predicted(snapshot, calls[1].Recorded));
        await current;
        Assert.IsFalse(controller.Current.Busy);
        Assert.AreEqual(20, controller.Current.Result?.Container.X);
    }

    /// <summary>Canceling or failing an active request leaves the saved inventory untouched and allows a successful manual retry.</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task InterruptedPrediction_SavedItemsSurviveAndRetryCanPublish(bool cancel)
    {
        // Review: the API is paused after entry; cancellation then returns a deliberately late successful
        // response. The failure variant tests the same recovery contract with an actual failed Task.
        // Assertions distinguish canceled-result publication, stuck busy state, and overwriting saved Items.
        var calls = new List<Pending>();
        using var controller = Create(calls);
        var snapshot = Snapshot();
        var recorded = Recorded(30);
        var savedItems = recorded.Items;
        controller.SetContext(snapshot, "overworld", recorded);
        var interrupted = controller.StartAsync();
        Assert.HasCount(1, calls);
        Assert.IsTrue(controller.Current.Busy);
        if (cancel)
        {
            controller.Cancel();
            Assert.IsTrue(controller.Current.Canceling);
            Assert.IsTrue(calls[0].Token.IsCancellationRequested);
            calls[0].Completion.SetResult(Predicted(snapshot, calls[0].Recorded));
        }
        else
        {
            calls[0].Completion.SetException(new IOException("native prediction failed"));
        }

        await interrupted;
        Assert.IsFalse(controller.Current.Busy);
        Assert.IsFalse(controller.Current.Canceling);
        Assert.IsNull(controller.Current.Result);
        Assert.AreEqual(cancel ? string.Empty : "native prediction failed", controller.Current.Error);

        var retry = controller.StartAsync();
        Assert.HasCount(2, calls);
        calls[1].Completion.SetResult(Predicted(snapshot, calls[1].Recorded));
        await retry;
        Assert.AreEqual("minecraft:diamond", controller.Current.Result?.Container.Items.Single().Id);
        Assert.AreEqual(string.Empty, controller.Current.Error);
        Assert.AreSame(savedItems, recorded.Items);
        Assert.IsEmpty(recorded.Items);
        Assert.AreEqual("unevaluated", recorded.State);
    }

    private static SeedLootPredictionController Create(List<Pending> calls) => new((_, _, recorded, token) =>
    {
        var pending = new Pending(recorded, token);
        calls.Add(pending);
        return pending.Completion.Task;
    }, (_, _, _) => true, CancellationToken.None);

    private static WorldMapSnapshot Snapshot() => new()
    {
        Selection = new("test-instance", "test-world"),
        Name = "test-world",
        Version = "installed",
        Profile = new(),
        Seed = "123",
        Revision = "recorded-revision"
    };

    private static SeedContainer Recorded(int x) => new()
    {
        Id = "minecraft:chest",
        X = x,
        Y = 64,
        Z = 0,
        State = "unevaluated",
        Slots = 27,
        LootTable = "minecraft:chests/simple_dungeon",
        LootSeed = "456"
    };

    private static WorldContainerPrediction Predicted(WorldMapSnapshot snapshot, SeedContainer recorded) => new()
    {
        Container = recorded with { State = "generated", Items = [new() { Id = "minecraft:diamond", Slot = 4, Count = 2 }] },
        SourceRevision = snapshot.Revision,
        RuntimeFingerprint = "test-runtime",
        Deterministic = true
    };

    private sealed record Pending(SeedContainer Recorded, CancellationToken Token)
    {
        internal TaskCompletionSource<WorldContainerPrediction> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}