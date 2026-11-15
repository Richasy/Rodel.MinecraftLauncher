// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Multiplayer;

namespace Rodel.MinecraftLauncher.App.Features.Multiplayer;

/// <summary>Derives availability from verified room evidence, independently of game startup or member counts.</summary>
internal static class MultiplayerPresentation
{
    internal static bool Ended(MultiplayerRoom? room) => room is { Active: false, State: "closed" or "stopped" };

    internal static MultiplayerGameAction GameAction(MultiplayerRoom? room, bool gameRunning, bool preparing)
    {
        if (room is null || preparing) return MultiplayerGameAction.Unavailable;
        if (gameRunning) return MultiplayerGameAction.ReturnToGame;
        if (room.State is "ending" or "exit-pending") return MultiplayerGameAction.Unavailable;
        if (room.Role == "guest") return Ready(room) ? MultiplayerGameAction.LaunchGuest : MultiplayerGameAction.Unavailable;
        return MultiplayerGameAction.Unavailable;
    }

    internal static bool Ready(MultiplayerRoom? room) => room is { Active: true, State: "connected" }
        && (room.Path == "fallback" || room.Role == "guest"
            ? !string.IsNullOrWhiteSpace(room.Address)
            : room.GamePort is > 0 && !string.IsNullOrWhiteSpace(room.Invitation));

    internal static int? OnlineCount(MultiplayerRoom? room) => Ready(room) && room!.Members.Length > 0 ? room.Members.Length : null;

    internal static bool WaitingForFriends(MultiplayerRoom? room) => Ready(room) && room!.Role == "host"
        && !room.Members.Any(member => member.Role is not ("host" or "local"));

    internal static string Status(MultiplayerRoom? room) => Ready(room) ? room?.Role == "guest" ? Resources.MultiplayerConnected : Resources.MultiplayerJoinable : room?.State switch
    {
        "ending" => Resources.MultiplayerEnding,
        "exit-pending" => Resources.MultiplayerCheckExit,
        "starting-game" => Resources.LaunchPreparing,
        "waiting-world" => Resources.MultiplayerWaiting,
        "manual" => Resources.MultiplayerManual,
        "discovering" => Resources.MultiplayerLanDiscovering,
        "opening" or "connecting" or "preparing" => Resources.MultiplayerOpening,
        "retrying" => Resources.MultiplayerRetrying,
        "disconnected" or "failed" or "interrupted" => Resources.MultiplayerDisconnected,
        "closed" or "stopped" => Resources.MultiplayerStopped,
        _ => Resources.MultiplayerUnavailable,
    };

    internal static bool Equivalent(MultiplayerRoom? left, MultiplayerRoom? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return (left.Id == right.Id)
            && (left.InstanceId == right.InstanceId)
            && (left.Name == right.Name)
            && (left.Role == right.Role)
            && (left.Active == right.Active)
            && (left.State == right.State)
            && (left.ErrorCode == right.ErrorCode)
            && (left.ComponentState == right.ComponentState)
            && (left.RetryCount == right.RetryCount)
            && (left.Invitation == right.Invitation)
            && (left.Address == right.Address)
            && (left.Path == right.Path)
            && (left.GamePort == right.GamePort)
            && (left.SessionId == right.SessionId)
            && (left.GameProcessId == right.GameProcessId)
            && (left.GameStartedAt == right.GameStartedAt)
            && (left.EndOperationId == right.EndOperationId)
            && (left.GameCloseRequestedAt == right.GameCloseRequestedAt)
            && Equals(left.World, right.World) && left.Members.SequenceEqual(right.Members);
    }

}

/// <summary>Distinguishes returning to the bound game from explicitly starting a connected guest.</summary>
internal enum MultiplayerGameAction { Unavailable, ReturnToGame, LaunchGuest }

/// <summary>Retains dismissal for one room and one main-window hide cycle without changing the room or persistent preference.</summary>
internal sealed class MultiplayerMiniVisibility
{
    private string? _room;
    private bool _dismissed;

    internal bool Update(MultiplayerRoom? room, bool enabled, bool mainHidden)
    {
        if (!mainHidden || _room != room?.Id)
            _dismissed = false;
        _room = room?.Id;
        return enabled && mainHidden && !_dismissed && room is { Active: true, Role: "host" };
    }

    internal void Dismiss() => _dismissed = true;
}