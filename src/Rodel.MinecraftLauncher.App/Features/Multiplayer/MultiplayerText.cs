// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.App.Features.Multiplayer;

/// <summary>Maps durable protocol states to localized player-facing descriptions.</summary>
internal static class MultiplayerText
{
    internal static string State(string? state) => state switch
    {
        "waiting-world" => Resources.MultiplayerWaiting,
        "opening" => Resources.MultiplayerOpening,
        "manual" => Resources.MultiplayerManual,
        "discovering" => Resources.MultiplayerLanDiscovering,
        "connecting" => Resources.MultiplayerConnecting,
        "connected" => Resources.MultiplayerConnected,
        "disconnected" => Resources.MultiplayerDisconnected,
        "retrying" => Resources.MultiplayerRetrying,
        "closed" => Resources.MultiplayerClosed,
        "stopped" => Resources.MultiplayerStopped,
        "ending" => Resources.MultiplayerEnding,
        "exit-pending" => Resources.MultiplayerCheckExit,
        "starting-game" => Resources.LaunchPreparing,
        "failed" or "interrupted" => Resources.MultiplayerFailed,
        _ => Resources.MultiplayerPreparing,
    };

    internal static string Recovery(string? code) => code switch
    {
        null or "" => string.Empty,
        "MULTIPLAYER_PORT_MISMATCH" or "MULTIPLAYER_MULTIPLE_PORTS" => Resources.MultiplayerPortMismatch,
        "MULTIPLAYER_AUTOMATION_UNAVAILABLE" or "GAME_LAN_UNSUPPORTED" or "GAME_LAN_OPEN_FAILED" => Resources.MultiplayerManualRecovery,
        "GAME_OPERATION_OUTCOME_UNKNOWN" => Resources.MultiplayerManualRecovery,
        "MULTIPLAYER_DISCOVERY_TIMEOUT" => Resources.MultiplayerDiscoveryRecovery,
        "MULTIPLAYER_LAN_DISCOVERY_TIMEOUT" => Resources.MultiplayerLanDiscoveryRecovery,
        "MULTIPLAYER_CONNECTION_0" or "MULTIPLAYER_CONNECTION_1" => Resources.MultiplayerHostRecovery,
        "MULTIPLAYER_COMPONENT_UNAVAILABLE" or "MULTIPLAYER_CONNECTION_2" or "MULTIPLAYER_CONNECTION_3" => Resources.MultiplayerComponentRecovery,
        "MULTIPLAYER_CONNECTION_5" or "MULTIPLAYER_COMPONENT_OWNERSHIP" => Resources.MultiplayerProtocolRecovery,
        "MULTIPLAYER_CONNECTION_4" or "MULTIPLAYER_WORLD_CHANGED" or "MULTIPLAYER_GAME_PORT_LOST" => Resources.MultiplayerClosed,
        "MULTIPLAYER_FALLBACK_UNAVAILABLE" => Resources.MultiplayerFallbackRecovery,
        "MULTIPLAYER_FALLBACK_STATUS_UNKNOWN" => Resources.MultiplayerFallbackStatusUnknown,
        "MULTIPLAYER_GAME_RESTART_REQUIRED" => Resources.MultiplayerRestartRecovery,
        "MULTIPLAYER_GAME_EXIT_PENDING" or "MULTIPLAYER_GAME_EXIT_UNAVAILABLE" or "MULTIPLAYER_GAME_TARGET_CHANGED" => Resources.MultiplayerExitPending,
        "MULTIPLAYER_GAME_LAUNCH_FAILED" => Resources.LaunchFailed,
        _ => string.Format(Resources.MultiplayerFailureDetail, "\u2068" + code + "\u2069"),
    };
}