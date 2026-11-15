// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Reports;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects operation completion and cancellation closure independently of task transport and native event routing.</summary>
[TestClass]
public sealed class PopupPreparationCancellationTests
{
    /// <summary>Review: a successful cancellation request is not task completion. A controlled owner remains active after acknowledgment; repeated close events must neither hide it nor issue duplicate requests. Existing selection tests do not cover this lifetime boundary.</summary>
    [TestMethod]
    public void CloseDuringPreparation_WaitsForTerminalOwnerNotification()
    {
        var active = true;
        var closes = 0;
        using var policy = new PopupPreparationClose(() => closes++, () => active, () => !active, () => false);
        Assert.IsTrue(policy.Request());
        policy.Observe(); // Cancellation acknowledged; worker has not stopped.
        Assert.AreEqual(0, closes);
        Assert.IsFalse(policy.Request());
        active = false; // Only this owner notification establishes task completion.
        policy.Observe();
        Assert.AreEqual(1, closes);
        policy.Observe(succeeded: true); // A racing success notification must not request closure again.
        Assert.AreEqual(1, closes);
    }

    /// <summary>Review: failed cancellation must retain the active popup and allow another close attempt; hiding on an error or permanently latching would leave preparation uncontrollable.</summary>
    [TestMethod]
    public void CancellationFailure_RetainsPopupAndAllowsRetry()
    {
        var active = true;
        var failed = false;
        var closes = 0;
        using var policy = new PopupPreparationClose(() => closes++, () => active, () => !active, () => failed);
        Assert.IsTrue(policy.Request());
        failed = true;
        policy.Observe();
        Assert.AreEqual(0, closes);
        failed = false;
        Assert.IsTrue(policy.Request());
        active = false;
        policy.Observe();
        Assert.AreEqual(1, closes);
    }

    /// <summary>Review: a picker/report can temporarily prohibit closure without owning cancellable work. Once accepted, idle dismissal must not be repeated by a queued success notification.</summary>
    [TestMethod]
    public void IdleDismissal_RespectsGuardAndClosesOnlyOnce()
    {
        var active = false;
        var blocked = true;
        var closes = 0;
        var policy = new PopupPreparationClose(() => closes++, () => active, () => !active && !blocked, () => false);
        Assert.IsFalse(policy.Request());
        blocked = false;
        Assert.IsFalse(policy.Request());
        Assert.AreEqual(1, closes);
        Assert.IsFalse(policy.Request());
        Assert.IsFalse(policy.Observe(succeeded: true));
        policy.Dispose();
    }

    /// <summary>Review: a successful operation must dismiss at its terminal notification, before asynchronous list refreshes. Active or failed work stays available; repeated success cannot close twice. Existing cancellation cases exercise only explicit dismissal.</summary>
    [TestMethod]
    public void SuccessfulOperation_ClosesAtTerminalNotificationOnlyOnce()
    {
        var active = true;
        var closes = 0;
        using var policy = new PopupPreparationClose(() => closes++, () => active, () => !active, () => false);
        Assert.IsFalse(policy.Observe());
        Assert.AreEqual(0, closes);
        active = false;
        Assert.IsFalse(policy.Observe()); // Failure or an ordinary refresh is not successful submission.
        Assert.AreEqual(0, closes);
        Assert.IsTrue(policy.Observe(succeeded: true)); // The refresh continuation has not run yet.
        Assert.IsTrue(policy.IsClosing);
        Assert.AreEqual(1, closes);
        Assert.IsFalse(policy.Observe(succeeded: true));
        Assert.IsFalse(policy.Request());
        Assert.AreEqual(1, closes);
    }

    /// <summary>Review: a completed task can still be blocked by an owned picker/report. Success must wait for that guard, and disposal must suppress a late notification rather than affect another popup.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SuccessWhileBlocked_ClosesWhenReleasedUnlessDisposed(bool disposed)
    {
        var blocked = true;
        var closes = 0;
        using var policy = new PopupPreparationClose(() => closes++, () => false, () => !blocked, () => false);
        Assert.IsFalse(policy.Observe(succeeded: true));
        Assert.IsFalse(policy.IsClosing);
        blocked = false;
        if (disposed)
            policy.Dispose();
        Assert.AreEqual(!disposed, policy.Observe());
        Assert.IsFalse(policy.Request());
        Assert.AreEqual(disposed ? 0 : 1, closes);
    }
}