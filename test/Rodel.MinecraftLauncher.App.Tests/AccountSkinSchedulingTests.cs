// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Accounts;
using Rodel.MinecraftLauncher.Core.Application;
using System.Globalization;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects the real window-owned refresh scheduler; controlled query completions and time replace network and waiting, not scheduling logic.</summary>
[TestClass]
public sealed class AccountSkinSchedulingTests
{
    /// <summary>Gets runner cancellation for controlled completion barriers.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Review: startup, menu and activation can overlap. Hold an entered query to prove coalescing, then advance time to prove fresh data is reused. The same UUID on a second provider must still get its own query.</summary>
    [TestMethod]
    public void OverlappingTriggers_CoalesceUntilCooldownAndKeepSourcesSeparate() => StateTestScope.Run(async () =>
    {
        var clock = new Clock();
        var account = Account();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        var updated = new List<IdentityRecord>();
        using var refresh = new AccountSkinRefresh(async (id, ct) =>
        {
            Interlocked.Increment(ref requests);
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return (id == account.Id ? account : account with { Id = id, AuthServer = "https://second.test/api/" }) with { SkinUrl = "https://skin.test/new.png" };
        }, clock);
        refresh.Updated += updated.Add;
        var first = refresh.RefreshAsync(account);
        try
        {
            await entered.Task.WaitAsync(TestContext.CancellationToken);
            var duplicate = refresh.RefreshAsync(account);
            Assert.AreSame(first, duplicate);
            Assert.AreEqual(1, requests);
        }
        finally
        {
            release.TrySetResult();
            await first;
        }

        Assert.HasCount(1, updated);
        await refresh.RefreshAsync(account);
        Assert.AreEqual(1, requests);
        await refresh.RefreshAsync(account with { Id = "second", AuthServer = "https://second.test/api/" });
        Assert.AreEqual(2, requests);
        Assert.HasCount(2, updated);
        clock.Advance(TimeSpan.FromMinutes(5));
        await refresh.RefreshAsync(account);
        Assert.AreEqual(3, requests);
    });

    /// <summary>Review: unavailable skin services must not produce visible replacement events or get hammered by focus changes. Two failures distinguish exponential backoff from a constant retry delay; a later success restores normal freshness.</summary>
    [TestMethod]
    public void Failure_BacksOffSilentlyAndSuccessfulRetryRestoresFreshness() => StateTestScope.Run(async () =>
    {
        var clock = new Clock();
        var account = Account();
        var requests = 0;
        var updates = 0;
        using var refresh = new AccountSkinRefresh((_, _) => Interlocked.Increment(ref requests) <= 2
            ? Task.FromException<IdentityRecord>(new HttpRequestException("Synthetic failure"))
            : Task.FromResult(account with { SkinUrl = "https://skin.test/recovered.png" }), clock);
        refresh.Updated += _ => updates++;
        await refresh.RefreshAsync(account);
        clock.Advance(TimeSpan.FromMinutes(5));
        await refresh.RefreshAsync(account);
        Assert.AreEqual(1, requests);
        Assert.AreEqual(0, updates);
        clock.Advance(TimeSpan.FromMinutes(5));
        await refresh.RefreshAsync(account);
        Assert.AreEqual(2, requests);
        clock.Advance(TimeSpan.FromMinutes(19));
        await refresh.RefreshAsync(account);
        Assert.AreEqual(2, requests);
        Assert.AreEqual(0, updates);
        clock.Advance(TimeSpan.FromMinutes(1));
        await refresh.RefreshAsync(account);
        Assert.AreEqual(3, requests);
        Assert.AreEqual(1, updates);
        clock.Advance(TimeSpan.FromMinutes(5));
        await refresh.RefreshAsync(account);
        Assert.AreEqual(4, requests);
    });

    /// <summary>Review: after a successful login the returned profile is already fresh. A delayed older query must not overwrite it; disposal similarly suppresses a non-cooperating query's completion. The response barrier establishes the actual pending work.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PendingQuery_NewerLoginOrDisposalSuppressesLateNotification(bool dispose) => StateTestScope.Run(async () =>
    {
        var clock = new Clock();
        var account = Account();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        var updates = 0;
        using var refresh = new AccountSkinRefresh(async (_, _) =>
        {
            Interlocked.Increment(ref requests);
            entered.TrySetResult();
            await release.Task.WaitAsync(TestContext.CancellationToken);
            return account with { SkinUrl = "https://skin.test/late.png" };
        }, clock);
        refresh.Updated += _ => updates++;
        var pending = refresh.RefreshAsync(account);
        var loggedIn = account with { VerifiedAt = clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture), SkinUrl = "https://skin.test/login.png" };
        try
        {
            await entered.Task.WaitAsync(TestContext.CancellationToken);
            if (dispose) refresh.Dispose();
            else Assert.AreSame(pending, refresh.RefreshAsync(loggedIn));
        }
        finally
        {
            release.TrySetResult();
            await pending;
        }

        await refresh.RefreshAsync(loggedIn);
        Assert.AreEqual(1, requests);
        Assert.AreEqual(0, updates);
    });

    private static IdentityRecord Account() => new()
    {
        Id = "first",
        Type = "external",
        Uuid = "0123456789abcdef0123456789abcdef",
        AuthServer = "https://first.test/api/",
        SkinUrl = "https://skin.test/old.png"
    };

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan duration) => _now += duration;
    }
}