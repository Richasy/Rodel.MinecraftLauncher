using Rodel.MinecraftLauncher.Core.Application.AI;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Exercises the real cancellation timer used by both approval and ask_user waits.</summary>
[TestClass]
public sealed class TurnDeadlineTests
{
    /// <summary>Waiting longer than the active execution limit must remain answerable; resuming must restore a finite budget.</summary>
    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public async Task UserDecisionWait_ExcludesHumanTime_AndResumesTheDeadline()
    {
        using var deadline = new AiTurnDeadline(TimeSpan.FromMilliseconds(200), TestContext.CancellationToken);
        deadline.PauseForUser("approval");
        await Task.Delay(300, TestContext.CancellationToken);
        Assert.IsFalse(deadline.IsCancellationRequested);
        deadline.ResumeAfterUser("approval");
        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token).WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationToken));
    }

    /// <summary>Pausing the model's time budget must never suppress a user's explicit stop or host-lifetime cancellation.</summary>
    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public async Task CallerCancellation_StillStopsAnApprovalWait()
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        using var deadline = new AiTurnDeadline(TimeSpan.FromSeconds(5), caller.Token);
        deadline.PauseForUser("approval");
        await caller.CancelAsync();
        Assert.IsTrue(deadline.IsCancellationRequested);
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token));
    }

    /// <summary>Gets or sets the cooperative token for actual timer waits.</summary>
    public TestContext TestContext { get; set; }
}