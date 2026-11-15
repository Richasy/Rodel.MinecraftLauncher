// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Content;
using Rodel.MinecraftLauncher.Core.Content;
using Sprout.Reactive;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects the App's candidate/review binding; Core's planning and application are verified by their own workflows.</summary>
[TestClass]
public sealed class ContentInstallSelectionTests
{
    /// <summary>
    /// Selecting a new version and reloading must discard old confirmation immediately, including when a canceled
    /// same-version review eventually returns. Taking a review is one-shot and freezes the exact target/version/ID.
    /// </summary>
    [TestMethod]
    public void SelectionAndReload_ConsumeOnlyTheDisplayedFrozenReview() => StateTestScope.Run(async () =>
    {
        var reviews = new List<PendingReview>();
        Task<ContentReview> Review(ContentReviewSelection selection, CancellationToken token)
        {
            var pending = new PendingReview(selection, token);
            reviews.Add(pending);
            return pending.Completion.Task;
        }

        using var store = new ContentInstallStore("project", ContentKind.Mods, new("a", "A"),
            (_, _) => Task.FromResult(Catalog("v1", "v2")), Review);
        var load = store.LoadAsync();
        var choose = store.ChooseAsync(1);
        Task? reload = null;
        try
        {
            Assert.IsTrue(reviews[0].Token.IsCancellationRequested);
            Assert.IsNull(store.TakeReview());
            reviews[1].Completion.SetResult(Frozen(reviews[1].Selection, "review-v2"));
            await choose;
            var binding = store.TakeReview();
            Assert.IsNotNull(binding);
            Assert.AreEqual("a", binding.InstanceId);
            Assert.AreEqual("v2", binding.VersionId);
            Assert.AreEqual("review-v2", binding.ReviewId);
            Assert.AreEqual(ContentKind.Mods, binding.Kind);
            Assert.IsNull(store.TakeReview());
            reload = store.LoadAsync();
            Assert.IsNull(store.Review);
            Assert.IsFalse(store.Ready);
            reviews[0].Completion.SetResult(Frozen(reviews[0].Selection, "old-v1"));
            await load;
            Assert.IsNull(store.Review);
            reviews[2].Completion.SetResult(Frozen(reviews[2].Selection, "fresh-v1"));
            await reload;
            Assert.AreEqual("fresh-v1", store.TakeReview()?.ReviewId);
        }
        finally
        {
            store.Dispose();
            foreach (var review in reviews) review.Completion.TrySetCanceled(TestContext.CancellationToken);
            await Task.WhenAll(new[] { load, choose }.Concat(reload is null ? [] : new[] { reload }));
        }
    });

    /// <summary>
    /// A target switch must not combine B with A's catalog, errors or confirmation. Completing actual captured
    /// reads in reverse order reaches that window; observations also reject an enabled catalog before auto-selection.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TargetSwitch_DropsLateCatalogSuccessAndFailure(bool failOld) => StateTestScope.Run(async () =>
    {
        var catalogs = new List<TaskCompletionSource<ContentVersionCatalog>>();
        var reviews = new List<PendingReview>();
        Task<ContentVersionCatalog> Read(ContentInstallTarget target, CancellationToken token)
        {
            var completion = new TaskCompletionSource<ContentVersionCatalog>();
            catalogs.Add(completion);
            return completion.Task;
        }

        Task<ContentReview> Review(ContentReviewSelection selection, CancellationToken token)
        {
            var pending = new PendingReview(selection, token);
            reviews.Add(pending);
            return pending.Completion.Task;
        }

        using var store = new ContentInstallStore("project", ContentKind.Mods, new("a", "A"), Read, Review);
        var seen = new List<(string Target, bool Loading, int Selected, int Count)>();
        using var effect = new Effect(() => seen.Add((store.Target.Id, store.Loading, store.Selected, store.Versions.Count)));
        var first = store.LoadAsync();
        var second = store.LoadAsync(new("b", "B"));
        try
        {
            catalogs[1].SetResult(Catalog("b-version"));
            Assert.IsTrue(seen.All(item => item.Count == 0 || item.Loading || item.Selected >= 0));
            Assert.AreEqual("b", reviews.Single().Selection.InstanceId);
            Assert.AreEqual("b-version", reviews.Single().Selection.VersionId);
            reviews[0].Completion.SetResult(Frozen(reviews[0].Selection, "b-review"));
            await second;
            if (failOld) catalogs[0].SetException(new IOException("old a catalog"));
            else catalogs[0].SetResult(Catalog("a-version"));
            await first;
            Assert.AreEqual("b-version", store.Versions.Single().Id);
            Assert.AreEqual(string.Empty, store.LoadError);
            Assert.AreEqual("b-review", store.TakeReview()?.ReviewId);
        }
        finally
        {
            store.Dispose();
            foreach (var catalog in catalogs) catalog.TrySetCanceled(TestContext.CancellationToken);
            foreach (var review in reviews) review.Completion.TrySetCanceled(TestContext.CancellationToken);
            await Task.WhenAll(first, second);
        }
    });

    /// <summary>A mismatched planner response is not a confirmation for the current target even if its plan says CanApply.</summary>
    [TestMethod]
    public void WrongReviewOwner_IsVisibleFailureRatherThanApplicableEvidence() => StateTestScope.Run(async () =>
    {
        using var store = new ContentInstallStore("project", ContentKind.Mods, new("a", "A"),
            (_, _) => Task.FromResult(Catalog("v1")),
            (selection, _) => Task.FromResult(Frozen(selection with { InstanceId = "b" }, "wrong-owner")));
        await store.LoadAsync();
        Assert.IsFalse(store.Ready);
        Assert.IsNull(store.TakeReview());
        Assert.IsFalse(string.IsNullOrEmpty(store.PlanError));
        Assert.IsTrue(store.NeedsReview);
    });

    /// <summary>Closing a dialog stops UI observation; a planner that ignores the token cannot publish or restore confirmation later.</summary>
    [TestMethod]
    public void Dispose_DropsNonCooperatingReviewCompletion() => StateTestScope.Run(async () =>
    {
        PendingReview? pending = null;
        using var store = new ContentInstallStore("project", ContentKind.Mods, new("a", "A"),
            (_, _) => Task.FromResult(Catalog("v1")),
            (selection, token) => (pending = new(selection, token)).Completion.Task);
        var notifications = 0;
        using var effect = new Effect(() => { _ = store.Ready; _ = store.Review; _ = store.PlanError; notifications++; });
        var load = store.LoadAsync();
        Assert.IsNotNull(pending);
        store.Dispose();
        var before = notifications;
        pending.Completion.SetResult(Frozen(pending.Selection, "late"));
        await load;
        Assert.IsTrue(pending.Token.IsCancellationRequested);
        Assert.AreEqual(before, notifications);
        Assert.IsNull(store.Review);
        Assert.IsNull(store.TakeReview());
    });

    private static ContentVersionCatalog Catalog(params string[] versions)
        => new([.. versions.Select(version => new ContentVersion(version, version, ["26.3"], ["fabric"]))], "26.3", "fabric");
    private static ContentReview Frozen(ContentReviewSelection selection, string id) => new()
    {
        Id = id,
        InstanceId = selection.InstanceId,
        Mode = "online",
        GameDirectory = "synthetic",
        Context = new() { Source = "fixture" },
        Fingerprint = "frozen",
        Plan = new()
        {
            Platform = "modrinth",
            PrimaryProject = selection.ProjectId,
            GameDirectory = "synthetic",
            LockFingerprint = "lock",
            Nodes = [],
            Edges = [],
            Conflicts = [],
            Warnings = [],
            CanApply = true
        }
    };
    private sealed class PendingReview(ContentReviewSelection selection, CancellationToken token)
    {
        internal ContentReviewSelection Selection { get; } = selection;
        internal CancellationToken Token { get; } = token;
        internal TaskCompletionSource<ContentReview> Completion { get; } = new();
    }

    public TestContext TestContext { get; set; }
}