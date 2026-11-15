// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Multiplayer;

/// <summary>Owns one data-root room, its protected invitation, component and durable controls independently of any UI.</summary>
public sealed class MultiplayerService(LauncherClient client)
{
    private string DirectoryPath => Path.Combine(client.Paths.Root, "multiplayer");
    private string StateFile => Path.Combine(DirectoryPath, "room.json");
    private string ControlFile => Path.Combine(DirectoryPath, "control.json");
    private string SecretFile(string id) => Path.Combine(DirectoryPath, id + ".secret.json");
    private string EventsFile(string id) => Path.Combine(DirectoryPath, id + ".events.json");

    /// <summary>Reads the active or last room, verifying the owner identity and revealing an invitation only for a live usable room.</summary>
    public MultiplayerRoom? Get()
    {
        MultiplayerRoom room;
        try
        {
            room = JsonSerializer.Deserialize(JsonFiles.ReadObject(StateFile), MultiplayerJsonContext.Default.MultiplayerRoom)
                ?? throw new InvalidDataException("The room record is empty.");
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return null; }

        ValidateId(room.Id);
        if (room.Active && !OwnerAlive(room))
        {
            return room.EndOperationId is not null && room.State is "ending" or "exit-pending"
                ? room with { State = "exit-pending", Active = false, Invitation = null, Address = null, ErrorCode = "MULTIPLAYER_GAME_EXIT_PENDING" }
                : room with { State = "interrupted", Active = false, Invitation = null, ErrorCode = "MULTIPLAYER_OWNER_EXITED" };
        }

        if (room.Active && room.State == "connected" && room.Path == "terracotta" && room.Role == "host")
            room = room with { Invitation = ReadInvitation(room.Id) };
        return room;
    }

    /// <summary>Moves a guest invitation into current-user protected storage before a task request is persisted.</summary>
    public async Task<MultiplayerRequest> PrepareAsync(MultiplayerRequest request, CancellationToken ct = default)
    {
        Validate(request);
        _ = client.Instances.Get(request.InstanceId);
        if (request.Role == "guest" && request.Invitation is { } text)
        {
            var code = RoomCode.Parse(text) ?? throw new LauncherOperationException("MULTIPLAYER_CODE_INVALID", "The room code did not pass native validation.");
            await ProtectedStore.WriteAsync(SecretFile(request.RoomId), new JsonObject { ["invitation"] = code }, ct).ConfigureAwait(false);
        }

        if (request.Role == "guest" && ReadInvitation(request.RoomId) is null)
            throw new LauncherOperationException("MULTIPLAYER_CODE_REQUIRED", "Provide a native room code.");
        return request with { Invitation = null };
    }

    /// <summary>Requests retry, stop, fallback or guest launch for exactly the observed room; a stop cannot be overwritten by another action.</summary>
    public async Task<MultiplayerRoom> ControlAsync(string roomId, string action, string? address = null, CancellationToken ct = default)
        => await WriteControlAsync(roomId, action, address, null, ct).ConfigureAwait(false);

    /// <summary>Ends exactly one room, explicitly distinguishing network-only cleanup from normal game exit; accepted exit signals are only observed on repeated requests.</summary>
    public async Task<MultiplayerRoom> EndAsync(string roomId, MultiplayerEndMode mode, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var room = Get() ?? throw new LauncherOperationException("MULTIPLAYER_NO_ROOM", "There is no room to end.");
        if (room.Id != roomId) throw new LauncherOperationException("MULTIPLAYER_ROOM_CHANGED", "The active room has changed.");
        if (mode == MultiplayerEndMode.KeepGame) return await ControlAsync(roomId, "stop", ct: ct).ConfigureAwait(false);
        if (room.Active) return await WriteControlAsync(roomId, "end", null, null, ct).ConfigureAwait(false);
        if (room.EndOperationId is null || room.State != "exit-pending")
            throw new LauncherOperationException("MULTIPLAYER_GAME_TARGET_EXPIRED", "Only an active room or its unfinished explicit exit can close a game.");
        using var owner = AcquireOwner();
        if (Get()?.Id != roomId) throw new LauncherOperationException("MULTIPLAYER_ROOM_CHANGED", "The active room has changed.");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        try
        {
            do
            {
                room = await MultiplayerGameExit.AdvanceAsync(client, room, requestClose: room.GameCloseRequestedAt is null).ConfigureAwait(false);
                room = room with { UpdatedAt = DateTimeOffset.UtcNow };
                await JsonFiles.WriteAsync(StateFile, JsonSerializer.SerializeToNode(room, MultiplayerJsonContext.Default.MultiplayerRoom)!, CancellationToken.None).ConfigureAwait(false);
                if (room.State != "ending") return room;
                await Task.Delay(200, ct).ConfigureAwait(false);
            } while (DateTimeOffset.UtcNow < deadline);
            return room;
        }
        finally
        {
            if (room.State == "ending") room = room with { State = "exit-pending", ErrorCode = "MULTIPLAYER_GAME_EXIT_PENDING" };
            room = room with { UpdatedAt = DateTimeOffset.UtcNow };
            await JsonFiles.WriteAsync(StateFile, JsonSerializer.SerializeToNode(room, MultiplayerJsonContext.Default.MultiplayerRoom)!, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Replaces a guest invitation through protected storage, preserving its prepared instance and game.</summary>
    public async Task<MultiplayerRoom> ChangeInvitationAsync(string roomId, string invitation, CancellationToken ct = default)
    {
        var code = RoomCode.Parse(invitation) ?? throw new LauncherOperationException("MULTIPLAYER_CODE_INVALID", "The room code did not pass native validation.");
        return await WriteControlAsync(roomId, "invitation", null, code, ct).ConfigureAwait(false);
    }

    private async Task<MultiplayerRoom> WriteControlAsync(string roomId, string action, string? address, string? invitation, CancellationToken ct)
    {
        var room = Get() ?? throw new LauncherOperationException("MULTIPLAYER_NO_ROOM", "There is no room to control.");
        if (room.Id != roomId) throw new LauncherOperationException("MULTIPLAYER_ROOM_CHANGED", "The active room has changed.");
        if (action is not ("stop" or "end" or "retry" or "fallback" or "launch" or "invitation")) throw new ArgumentException("Unknown room action.");
        if (room.State is "ending" or "exit-pending" && action is not ("stop" or "end"))
            throw new LauncherOperationException("MULTIPLAYER_ROOM_ENDING", "The room is ending.");
        if (action == "invitation" && (room.Role != "guest" || invitation is null)) throw new ArgumentException("A guest replacement invitation is required.");
        if (action == "fallback") _ = Servers.ServerService.ParseAddress(address ?? throw new ArgumentException("A host-provided server address is required."));
        if (!room.Active || !OwnerAlive(room))
        {
            if (action != "stop")
                throw new LauncherOperationException("MULTIPLAYER_OWNER_EXITED", "The room owner exited; end this room before starting another. The game is retained.");
            using var owner = AcquireOwner();
            if (Get()?.Id != roomId)
                throw new LauncherOperationException("MULTIPLAYER_ROOM_CHANGED", "The active room has changed.");
            if (room.ComponentPort is { } port && room.ComponentProcessId is { } worker && room.ComponentStartedAt is { } started)
                await TerracottaClient.StopRecordedAsync(port, worker, started, ct).ConfigureAwait(false);
            room = room with { State = "stopped", Active = false, Invitation = null, UpdatedAt = DateTimeOffset.UtcNow };
            await JsonFiles.WriteAsync(StateFile, JsonSerializer.SerializeToNode(room, MultiplayerJsonContext.Default.MultiplayerRoom)!, ct).ConfigureAwait(false);
            return room;
        }

        using var lease = await ResourceLease.AcquireAsync(ControlFile, ct).ConfigureAwait(false);
        if (File.Exists(ControlFile))
        {
            var pending = JsonSerializer.Deserialize(JsonFiles.ReadObject(ControlFile), TerracottaJsonContext.Default.RoomControl);
            if (pending?.RoomId == room.Id && pending.Id != room.ControlId && pending.Action is "stop" or "end")
                return room;
        }

        if (action == "end" && room.State == "ending") return room;
        var control = new RoomControl(Guid.NewGuid().ToString("N"), room.Id, action, address);
        if (invitation is not null)
            await ProtectedStore.WriteAsync(Path.Combine(DirectoryPath, room.Id + ".pending.secret.json"), new JsonObject { ["invitation"] = invitation }, ct).ConfigureAwait(false);
        await JsonFiles.WriteAsync(ControlFile, JsonSerializer.SerializeToNode(control, TerracottaJsonContext.Default.RoomControl)!, ct).ConfigureAwait(false);
        return room;
    }

    /// <summary>Exports only this room's bounded state/error history, excluding invitations, addresses and player data.</summary>
    public async Task ExportLogAsync(string roomId, string destination, CancellationToken ct = default)
    {
        ValidateId(roomId);
        var file = EventsFile(roomId);
        var events = File.Exists(file) ? JsonSerializer.Deserialize(await File.ReadAllTextAsync(file, ct).ConfigureAwait(false), TerracottaJsonContext.Default.RoomEventArray) ?? [] : [];
        var contents = "陶瓦联机连接记录\n" + string.Join('\n', events.Select(entry => $"{entry.At:O}\t{entry.State}\t{entry.Code}"));
        var bytes = Encoding.UTF8.GetBytes(contents);
        using var stream = new MemoryStream(bytes);
        var full = Path.GetFullPath(destination);
        using var lease = await ResourceLease.AcquireAsync(full, ct).ConfigureAwait(false);
        await AtomicFile.CopyAsync(stream, full, 8192, FileOptions.Asynchronous, ct).ConfigureAwait(false);
    }

    /// <summary>Runs one owned room until world exit, explicit ending or cancellation, preserving prepared instances and using normal game exit only when explicitly requested.</summary>
    public async Task<MultiplayerRoom> RunAsync(MultiplayerRequest request, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Multiplayer integration requires Windows.");
        request = await PrepareAsync(request, ct).ConfigureAwait(false);
        Directory.CreateDirectory(DirectoryPath);
        using var owner = AcquireOwner();
        var previous = Get();
        if (previous is { EndOperationId: not null, State: "exit-pending" })
            throw new LauncherOperationException("MULTIPLAYER_ROOM_ENDING", "Resolve the unfinished game exit before starting another room.");
        return await new MultiplayerRoomSession(client, request, DirectoryPath, previous).RunAsync(ct).ConfigureAwait(false);
    }
    private FileStream AcquireOwner()
    {
        try { return new FileStream(Path.Combine(DirectoryPath, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) when ((error.HResult & 65535) is 32 or 33) { throw new LauncherOperationException("MULTIPLAYER_ROOM_ACTIVE", "End the active room before creating or joining another."); }
    }

    private string? ReadInvitation(string id) => ProtectedStore.Read(SecretFile(id))?["invitation"]?.GetValue<string>();
    private static bool OwnerAlive(MultiplayerRoom room)
    {
        try { using var owner = Process.GetProcessById(room.OwnerProcessId); return !owner.HasExited && owner.StartTime.ToUniversalTime().ToString("O") == room.OwnerStartedAt; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    internal static bool OwnerMatchesGame(SessionStatus game)
    {
        try { using var process = Process.GetProcessById(game.ProcessId ?? -1); return !process.HasExited && process.StartTime.ToUniversalTime().ToString("O") == game.ProcessStartUtc; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    private static void Validate(MultiplayerRequest request)
    {
        ValidateId(request.RoomId);
        if (request.Role is not ("host" or "guest") || request.Name.Length > 60 || request.PlayerName.Length > 256)
            throw new ArgumentException("A bounded host or guest room is required.");
    }
    private static void ValidateId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("A 32-digit room identity is required.");
    }
}