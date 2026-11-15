// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Multiplayer;

/// <summary>Observes one explicitly authorized normal game exit without replaying accepted signals or terminating a process tree.</summary>
internal static class MultiplayerGameExit
{
    internal static async Task<MultiplayerRoom> AdvanceAsync(LauncherClient client, MultiplayerRoom room, bool requestClose)
    {
        if (room.SessionId is null) return Complete(room);
        SessionStatus game;
        try { game = client.Sessions.Get(room.SessionId); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { return Pending(room, "MULTIPLAYER_GAME_TARGET_CHANGED"); }

        if (game.InstanceId != room.InstanceId || game.ProcessId != room.GameProcessId || game.ProcessStartUtc != room.GameStartedAt)
            return Pending(room, "MULTIPLAYER_GAME_TARGET_CHANGED");
        if (game.ActivityUnknown == true || game.ProcessId == Environment.ProcessId)
            return Pending(room, "MULTIPLAYER_GAME_EXIT_UNAVAILABLE");
        if (!MultiplayerService.OwnerMatchesGame(game))
        {
            return game.WindowObservationWarning is not null
            ? Pending(room, "MULTIPLAYER_GAME_EXIT_UNAVAILABLE") : Complete(room);
        }

        if (game.Status != "running") return Pending(room, "MULTIPLAYER_GAME_EXIT_UNAVAILABLE");
        if (room.GameCloseRequestedAt is { } requested)
        {
            return DateTimeOffset.UtcNow - requested >= TimeSpan.FromSeconds(30)
                ? Pending(room, "MULTIPLAYER_GAME_EXIT_PENDING") : room;
        }

        if (!requestClose) return room;
        if (game.Readiness != "window_visible") return Pending(room, "MULTIPLAYER_GAME_EXIT_UNAVAILABLE");
        if (room.World is { Epoch.Length: > 0 } world)
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var snapshot = await client.GameBridge.SnapshotAsync(room.SessionId, deadline.Token).ConfigureAwait(false);
                if (!snapshot.Ready || snapshot.LocalWorldEpoch != world.Epoch) return Pending(room, "MULTIPLAYER_GAME_TARGET_CHANGED");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException or LauncherOperationException)
            { return Pending(room, "MULTIPLAYER_GAME_TARGET_CHANGED"); }
        }
        // Persist dispatch intent first. A crash between posting and recording the result must not replay a close.
        room = room with { State = "ending", GameCloseRequestedAt = DateTimeOffset.UtcNow, ErrorCode = null };
        await JsonFiles.WriteAsync(Path.Combine(client.Paths.Root, "multiplayer", "room.json"),
            JsonSerializer.SerializeToNode(room, MultiplayerJsonContext.Default.MultiplayerRoom)!, CancellationToken.None).ConfigureAwait(false);
        try
        {
            var result = client.Sessions.Stop(room.SessionId);
            return result.StopRequested
                ? room
                : Pending(room with { GameCloseRequestedAt = null }, "MULTIPLAYER_GAME_EXIT_UNAVAILABLE");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            // A request with uncertain effect is not automatically sent a second time.
            return Pending(room, "MULTIPLAYER_GAME_EXIT_UNAVAILABLE");
        }
    }

    private static MultiplayerRoom Complete(MultiplayerRoom room) => room with { State = "stopped", ErrorCode = null, Members = [], Invitation = null, Address = null };
    private static MultiplayerRoom Pending(MultiplayerRoom room, string code) => room with { State = "exit-pending", ErrorCode = code };
}