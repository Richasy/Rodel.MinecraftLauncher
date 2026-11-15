// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Shell;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects the automatic return intent across complete multi-instance observation sequences.</summary>
[TestClass]
public sealed class GameWindowReturnTests
{
    /// <summary>Review: the selected game's exit is insufficient while another instance runs. Feed the real home publisher and start notification into the actual return policy; a borrowed second game also blocks return. Only launch snapshots replace game processes; native tray effects are validated separately.</summary>
    [TestMethod]
    public void MultipleGames_ReturnsOnceAfterTheLastInstanceStops()
    {
        using var scope = new StateTestScope();
        using var home = new HomeController(scope.Client, "first");
        var returnState = new GameWindowReturn();
        var returns = 0;
        home.StateChanged += state => { if (returnState.Observe(state, home.HasPreparingLaunch)) returns++; };
        home.GameStarted += _ => returnState.Arm();
        home.PublishLaunches(new Dictionary<string, HomeLaunchState>
        {
            ["first"] = new("running", SessionId: "first-session", SessionOwned: true, GameWindowVisible: true),
            ["second"] = new("running", SessionId: "second-session", GameWindowVisible: true)
        });
        home.PublishLaunches(new Dictionary<string, HomeLaunchState>
        {
            ["first"] = new("exited", ExitCode: 0),
            ["second"] = new("running", SessionId: "second-session", GameWindowVisible: true)
        });
        Assert.AreEqual(0, returns);
        home.PublishLaunches(new Dictionary<string, HomeLaunchState>
        {
            ["first"] = new("exited", ExitCode: 0),
            ["second"] = new("exited", ExitCode: 0)
        });
        Assert.AreEqual(1, returns);
        home.PublishLaunches(new Dictionary<string, HomeLaunchState>());
        Assert.AreEqual(1, returns);
    }

    /// <summary>Review: initial/failed reads and a not-yet-published launch must not look like the last game exiting. Once activity is known to have failed, return is allowed; manual restoration/hiding consumes the old intent so later observations cannot steal focus.</summary>
    [TestMethod]
    public void UncertainActivityAndPreflight_WaitUntilKnownIdle_AndManualReturnCancels()
    {
        var returnState = new GameWindowReturn();
        var idle = new HomePageState { LaunchObserved = true };
        Assert.IsFalse(returnState.Observe(idle, false));
        returnState.Arm();
        Assert.IsFalse(returnState.Observe(new(), false));
        Assert.IsFalse(returnState.Observe(idle with { LaunchReadError = "synthetic read failure" }, false));
        Assert.IsFalse(returnState.Observe(idle with { LaunchStates = new Dictionary<string, HomeLaunchState> { ["game"] = new("unobserved") } }, false));
        Assert.IsFalse(returnState.Observe(idle, true));
        Assert.IsFalse(returnState.Observe(idle with { LaunchStates = new Dictionary<string, HomeLaunchState> { ["next"] = new("queued", TaskActive: true) } }, false));
        Assert.IsTrue(returnState.Observe(idle with { LaunchStates = new Dictionary<string, HomeLaunchState> { ["next"] = new("failed", ExitCode: 1) } }, false));
        Assert.IsFalse(returnState.Observe(idle, false));
        returnState.Arm();
        returnState.Cancel();
        Assert.IsFalse(returnState.Observe(idle, false));
    }
}