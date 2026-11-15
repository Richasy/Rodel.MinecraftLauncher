// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects the home observation boundary that wakes reactive UI; actual idle GPU use is measured separately.</summary>
[TestClass]
public sealed class HomeObservationTests
{
    /// <summary>Only a game exit during this App process should reach the home error card, even when the game started before the App and its crash remains in Core history.</summary>
    [TestMethod]
    public async Task CrashHistory_OnlyShowsExitsDuringThisAppRun()
    {
        using var scope = new StateTestScope();
        using var process = Process.GetCurrentProcess();
        var started = process.StartTime.ToUniversalTime();
        var ct = TestContext.CancellationToken;
        scope.Client.Paths.EnsureDirectories();
        var instance = await scope.Client.Instances.CreateAsync("Crash history fixture", "1.20.1", ct);
        var sessionId = Guid.NewGuid().ToString("N");
        var file = Path.Combine(scope.Client.Paths.SessionsDirectory, sessionId + ".json");
        var session = new JsonObject
        {
            ["id"] = sessionId,
            ["instanceId"] = instance.Id,
            ["gameDirectory"] = instance.GameDirectory,
            ["status"] = "exited",
            ["exitCode"] = 1,
            ["createdAt"] = started.AddMinutes(-2).ToString("O"),
            ["exitedAt"] = started.AddMinutes(-1).ToString("O"),
        };
        await File.WriteAllTextAsync(file, session.ToJsonString(), ct);

        var observer = new LaunchProblemObserver(scope.Client);
        var states = await observer.EnrichAsync(HomeLaunchReader.Read(scope.Client), [instance.Id], ct);
        var home = new HomePageState { SelectedId = instance.Id, LaunchStates = states };
        Assert.IsFalse(home.ShowLaunchStatus);
        Assert.IsNull(home.CurrentLaunch.SessionId);
        Assert.AreEqual(1, scope.Client.Sessions.Get(sessionId).ExitCode);

        session["exitedAt"] = DateTimeOffset.UtcNow.ToString("O");
        await File.WriteAllTextAsync(file, session.ToJsonString(), ct);
        states = await observer.EnrichAsync(HomeLaunchReader.Read(scope.Client), [instance.Id], ct);
        home = home with { LaunchStates = states };
        Assert.IsTrue(home.HasLaunchError);
        Assert.AreEqual(sessionId, home.CurrentLaunch.SessionId);
        Assert.AreEqual(1, home.CurrentLaunch.ExitCode);
    }

    /// <summary>A still active session and a newer failed preparation can coexist for one instance. Reading real Core records must keep the selected session's own task, so opening details cannot mix two attempts. Incomplete process identity is intentional and must stay unknown rather than require a real game.</summary>
    [TestMethod]
    public async Task ActiveSession_DoesNotBorrowTheLatestFailedTask()
    {
        using var scope = new StateTestScope();
        scope.Client.Paths.EnsureDirectories();
        var instance = await scope.Client.Instances.CreateAsync("Attempt fixture", "1.20.1", TestContext.CancellationToken);
        var sessionId = Guid.NewGuid().ToString("N");
        var sessionJob = Guid.NewGuid().ToString("N");
        var latestJob = Guid.NewGuid().ToString("N");
        foreach (var (id, created) in new[] { (sessionJob, "2026-10-30T00:00:00Z"), (latestJob, "2026-10-31T00:00:00Z") })
        {
            var record = new JsonObject
            {
                ["id"] = id,
                ["status"] = "failed",
                ["createdAt"] = created,
                ["request"] = new JsonObject { ["operation"] = "launcher-task", ["parameters"] = new JsonObject { ["instanceId"] = instance.Id, ["kind"] = "launch" } }
            };
            await File.WriteAllTextAsync(Path.Combine(scope.Client.Paths.JobsDirectory, id + ".json"), record.ToJsonString(), TestContext.CancellationToken);
        }

        var session = new JsonObject { ["id"] = sessionId, ["instanceId"] = instance.Id, ["gameDirectory"] = instance.GameDirectory, ["jobId"] = sessionJob, ["status"] = "running" };
        await File.WriteAllTextAsync(Path.Combine(scope.Client.Paths.SessionsDirectory, sessionId + ".json"), session.ToJsonString(), TestContext.CancellationToken);
        var launch = HomeLaunchReader.Read(scope.Client)[instance.Id];
        Assert.AreEqual(sessionId, launch.SessionId);
        Assert.AreEqual(sessionJob, launch.JobId);
        Assert.AreEqual("unobserved", launch.Status);
    }

    /// <summary>
    /// Empty and equivalent newly allocated reads must settle after the initial observation. Otherwise idle polling
    /// invalidates the UI forever. No existing state-owner test covers this producer; no window or timer is substituted.
    /// </summary>
    [TestMethod]
    public void EquivalentReads_PublishInitialObservationThenRemainQuiet()
    {
        using var scope = new StateTestScope();
        using var controller = new HomeController(scope.Client);
        var changes = new List<HomePageState>();
        controller.StateChanged += changes.Add;

        controller.PublishLaunches(HomeLaunchReader.Read(scope.Client));
        Assert.HasCount(1, changes);
        Assert.IsTrue(changes[0].LaunchObserved);
        controller.PublishLaunches(HomeLaunchReader.Read(scope.Client));
        Assert.HasCount(1, changes);

        controller.PublishLaunches(new Dictionary<string, HomeLaunchState>
        {
            ["a"] = new("idle"),
            ["b"] = new("exited", ExitCode: 0)
        });
        var settled = controller.State;
        changes.Clear();
        controller.PublishLaunches(new Dictionary<string, HomeLaunchState>
        {
            ["b"] = new("exited", ExitCode: 0),
            ["a"] = new("idle")
        });
        Assert.IsEmpty(changes);
        Assert.AreSame(settled, controller.State);

        controller.PublishLaunches(new Dictionary<string, HomeLaunchState> { ["b"] = new("exited", ExitCode: 0) });
        Assert.HasCount(1, changes);
        Assert.IsFalse(controller.State.LaunchStates.ContainsKey("a"));
    }

    /// <summary>Deduplication must preserve byte progress and cancellation ownership changes even without a status change.</summary>
    [TestMethod]
    public void ProgressAndOwnershipChanges_StillPublishCompleteSnapshots()
    {
        using var scope = new StateTestScope();
        using var controller = new HomeController(scope.Client);
        var changes = new List<HomePageState>();
        controller.StateChanged += changes.Add;
        var launch = new HomeLaunchState("preparing", JobId: "job", Stage: "download", Bytes: 10,
            TotalBytes: 100, TaskActive: true, TaskOwned: true, Owned: true);
        controller.PublishLaunches(new Dictionary<string, HomeLaunchState> { ["a"] = launch });
        changes.Clear();

        var progress = launch with { Bytes = 20 };
        controller.PublishLaunches(new Dictionary<string, HomeLaunchState> { ["a"] = progress });
        Assert.HasCount(1, changes);
        Assert.AreEqual(20L, controller.State.LaunchStates["a"].Bytes);
        var ownership = progress with { TaskOwned = false, Owned = false };
        controller.PublishLaunches(new Dictionary<string, HomeLaunchState> { ["a"] = ownership });
        Assert.HasCount(2, changes);
        Assert.IsFalse(controller.State.LaunchStates["a"].TaskOwned);
        controller.PublishLaunches(new Dictionary<string, HomeLaunchState> { ["a"] = ownership with { } });
        Assert.HasCount(2, changes);
    }

    /// <summary>
    /// Clock-derived window-wait progress must keep updating until the real window is observed. Only that readiness
    /// triggers launch behavior once and fills the retained progress bar; subsequent equal reads settle
    /// without swallowing a later process error. Keeping the bar visible distinguishes completion from hiding it.
    /// </summary>
    [TestMethod]
    public void GameWindowReadiness_NotifiesOnceWithoutLosingLaterErrors()
    {
        using var scope = new StateTestScope();
        using var controller = new HomeController(scope.Client, "a");
        var started = new List<string>();
        var changes = new List<HomePageState>();
        controller.GameStarted += started.Add;
        controller.StateChanged += changes.Add;
        var waiting = new HomeLaunchState("running", SessionId: "session", SessionOwned: true, Owned: true);
        controller.PublishLaunches(new Dictionary<string, HomeLaunchState> { ["a"] = waiting });
        controller.PublishLaunches(new Dictionary<string, HomeLaunchState> { ["a"] = waiting with { } });
        Assert.IsEmpty(started);
        Assert.HasCount(2, changes);
        Assert.IsTrue(controller.State.ShowLaunchProgress);
        Assert.AreNotEqual(100d, controller.State.LaunchPercent);

        var ready = waiting with { GameWindowVisible = true };
        controller.PublishLaunches(new Dictionary<string, HomeLaunchState> { ["a"] = ready });
        controller.PublishLaunches(new Dictionary<string, HomeLaunchState> { ["a"] = ready with { } });
        Assert.HasCount(1, started);
        Assert.AreEqual("a", started[0]);
        Assert.HasCount(3, changes);
        Assert.IsTrue(controller.State.ShowLaunchProgress);
        Assert.AreEqual(100d, controller.State.LaunchPercent);
        controller.PublishLaunches(new Dictionary<string, HomeLaunchState>
        {
            ["a"] = ready with { Status = "failed", ExitCode = 1, Error = "game process failed" }
        });
        Assert.AreEqual("game process failed", controller.State.LaunchStates["a"].Error);
        Assert.HasCount(1, started);
        Assert.HasCount(4, changes);
        Assert.IsFalse(controller.State.ShowLaunchProgress);
    }

    public TestContext TestContext { get; set; }
}