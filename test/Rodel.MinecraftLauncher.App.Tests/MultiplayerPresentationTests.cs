// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Multiplayer;
using Rodel.MinecraftLauncher.Core.Multiplayer;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects room-status and compact-window lifecycle decisions; actual window behavior is verified separately.</summary>
[TestClass]
public sealed class MultiplayerPresentationTests
{
    /// <summary>
    /// Review: a dismissed window must not reopen on every room observation, and a dismissal must not leak into
    /// a new room or a new minimize cycle. Existing cancellation and home-observation cases do not cover this
    /// lifecycle. This crosses the actual stateful policy without a timer, native window, or network substitute.
    /// </summary>
    [TestMethod]
    public void Dismissal_SurvivesRoomUpdates_UntilMainRestoresOrRoomChanges()
    {
        var policy = new MultiplayerMiniVisibility();
        var room = Room();
        Assert.IsFalse(policy.Update(room, enabled: true, mainHidden: false));
        Assert.IsTrue(policy.Update(room, enabled: true, mainHidden: true));
        policy.Dismiss();
        Assert.IsFalse(policy.Update(room with { State = "opening" }, enabled: true, mainHidden: true));
        Assert.IsFalse(policy.Update(room with { UpdatedAt = DateTimeOffset.UtcNow }, enabled: true, mainHidden: true));

        Assert.IsFalse(policy.Update(room, enabled: true, mainHidden: false));
        Assert.IsTrue(policy.Update(room, enabled: true, mainHidden: true));
        policy.Dismiss();
        Assert.IsTrue(policy.Update(room with { Id = "other-room" }, enabled: true, mainHidden: true));
        Assert.IsFalse(policy.Update(room with { Role = "guest" }, enabled: true, mainHidden: true));
        Assert.IsFalse(policy.Update(room with { Active = false }, enabled: true, mainHidden: true));
        Assert.IsFalse(policy.Update(room, enabled: false, mainHidden: true));
    }

    /// <summary>
    /// Review: an observed world or member list must not imply joinability when opening failed. Unknown membership
    /// must not become a fabricated zero. The same projection also suppresses timestamp-only UI wakeups while real
    /// participant changes remain visible. No existing Core transport test establishes this presentation contract.
    /// </summary>
    [TestMethod]
    public void Availability_RequiresUsableEndpoint_AndKeepsMembershipIndependent()
    {
        var room = Room() with
        {
            State = "manual",
            ErrorCode = "GAME_OPERATION_OUTCOME_UNKNOWN",
            World = new("Current world", "1", "unknown")
        };
        Assert.IsFalse(MultiplayerPresentation.Ready(room));
        Assert.IsNull(MultiplayerPresentation.OnlineCount(room));
        room = room with { State = "connected", GamePort = 25565 };
        Assert.IsFalse(MultiplayerPresentation.Ready(room));
        room = room with { Invitation = "U/4K3M-9V6A-2R8D-5XQ4" };
        Assert.IsTrue(MultiplayerPresentation.Ready(room));
        Assert.IsNull(MultiplayerPresentation.OnlineCount(room));

        room = room with { Members = [new("host", "Host", "local")] };
        Assert.AreEqual(1, MultiplayerPresentation.OnlineCount(room));
        Assert.IsTrue(MultiplayerPresentation.WaitingForFriends(room));
        Assert.IsTrue(MultiplayerPresentation.Equivalent(room, room with { UpdatedAt = DateTimeOffset.UtcNow }));
        var joined = room with { Members = [.. room.Members, new("friend", "Friend", "guest")] };
        Assert.IsFalse(MultiplayerPresentation.Equivalent(room, joined));
        Assert.IsFalse(MultiplayerPresentation.WaitingForFriends(joined));
        Assert.AreEqual(2, MultiplayerPresentation.OnlineCount(joined));
        Assert.IsNull(MultiplayerPresentation.OnlineCount(joined with { State = "disconnected" }));
        Assert.IsFalse(MultiplayerPresentation.Ready(joined with { Active = false }));
        // An explicit fallback endpoint does not require the unavailable automatic LAN port observation.
        Assert.IsTrue(MultiplayerPresentation.Ready(room with { Path = "fallback", GamePort = null, Invitation = null, Address = "127.0.0.1:25565" }));
    }

    private static MultiplayerRoom Room() => new()
    {
        Id = "room",
        Name = "Room",
        InstanceId = "instance",
        Role = "host",
        Active = true,
        State = "waiting-world"
    };

    /// <summary>
    /// Review: a game entry must return to an existing process, never replace an active host world because its
    /// observation is delayed, and never launch a guest before its endpoint is ready. Readiness-only cases do not
    /// cover these task-routing guards. This exercises the production decision; native task dispatch is separate evidence.
    /// </summary>
    [TestMethod]
    public void GameEntry_ReturnsToLiveGame_AndDoesNotRestartAnActiveWorld()
    {
        var host = Room();
        Assert.AreEqual(MultiplayerGameAction.ReturnToGame, MultiplayerPresentation.GameAction(host, true, false));
        Assert.AreEqual(MultiplayerGameAction.Unavailable, MultiplayerPresentation.GameAction(host, false, false));
        Assert.AreEqual(MultiplayerGameAction.Unavailable, MultiplayerPresentation.GameAction(host, true, true));
        Assert.AreEqual(MultiplayerGameAction.Unavailable, MultiplayerPresentation.GameAction(host with { State = "closed" }, false, false));
        Assert.AreEqual(MultiplayerGameAction.Unavailable, MultiplayerPresentation.GameAction(host with { State = "closed", Active = false }, false, false));
        Assert.AreEqual(MultiplayerGameAction.Unavailable, MultiplayerPresentation.GameAction(host with { Active = false }, false, false));
        Assert.AreEqual(MultiplayerGameAction.Unavailable, MultiplayerPresentation.GameAction(host with { State = "exit-pending" }, false, false));
        Assert.AreEqual(MultiplayerGameAction.ReturnToGame, MultiplayerPresentation.GameAction(host with { State = "ending" }, true, false));
        Assert.IsFalse(MultiplayerPresentation.Equivalent(host, host with { EndOperationId = "exit" }));
        var guest = host with { Role = "guest", State = "connecting" };
        Assert.AreEqual(MultiplayerGameAction.Unavailable, MultiplayerPresentation.GameAction(guest, false, false));
        Assert.AreEqual(MultiplayerGameAction.LaunchGuest, MultiplayerPresentation.GameAction(guest with { State = "connected", Address = "127.0.0.1:25565" }, false, false));
    }
}