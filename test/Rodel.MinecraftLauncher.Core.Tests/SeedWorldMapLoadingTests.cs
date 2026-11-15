// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Features.SeedExplorer;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects progressive saved-map publication across a held original-preview request.</summary>
[TestClass]
public sealed class SeedWorldMapLoadingTests
{
    private static readonly int[] ExpectedSteps = [16, 16];

    /// <summary>
    /// Review: serial saved/preview pairs leave neighboring saved terrain invisible behind a slow generator.
    /// Unlike merge/provenance tests, this reaches the actual viewport scheduler's preview-await boundary.
    /// Controlled completion also proves that cancellation rejects a late successful preview without starting
    /// fine work. Only tile producers are substituted; no wall-clock timing or native rendering is claimed.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SlowPreview_PublishesSavedCoverageFirstAndRejectsCanceledCompletion(bool cancel)
    {
        using var source = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SeedMapQuery[] queries = [new() { Step = 16, Side = 2 }, new() { MinX = 32, Step = 16, Side = 2 }, new() { Step = 1, Side = 2 }];
        var published = new List<(int X, int Step, string Source, bool Completed)>();
        var reads = new List<int>();
        var loading = SeedWorldMapLoading.ReadAsync(queries, (query, _) =>
        {
            reads.Add(query.Step);
            return Task.FromResult(new SeedMapTile { MinX = query.MinX, Step = query.Step, Source = "saved" });
        }, async (_, saved, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return saved with { Source = "mixed" };
        }, (query, tile, completed) =>
        {
            published.Add((tile.MinX, query.Step, tile.Source, completed));
            return Task.CompletedTask;
        }, source.Token);

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
            CollectionAssert.AreEqual(new[] { (0, 16, "saved", false), (32, 16, "saved", false) }, published.ToArray(),
                "Both saved neighbors must already be displayed while the first preview is blocked.");
            CollectionAssert.AreEqual(ExpectedSteps, reads.ToArray(), "Fine reads must wait for overview coverage.");
            if (cancel) source.Cancel();
        }
        finally { release.TrySetResult(); }

        if (cancel)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => loading);
            Assert.HasCount(2, published, "A preview returning after cancellation cannot publish or begin finer work.");
            Assert.HasCount(2, reads);
        }
        else
        {
            await loading;
            CollectionAssert.AreEqual(new[] { (0, 16, "mixed", true), (32, 16, "mixed", true) }, published.Skip(2).Take(2).ToArray());
            Assert.AreEqual((0, 1, "mixed", true), published[^1]);
        }
    }

    /// <summary>Gets or sets the runner's cancellation scope for the held-preview boundary.</summary>
    public TestContext TestContext { get; set; }
}