// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Multiplayer;

/// <summary>Defines one host-owned room lifetime; game and content preparation use the existing Core workflows.</summary>
public sealed record MultiplayerRequest
{
    /// <summary>Gets a new 32-digit room identity, retained by its task and controls.</summary>
    public required string RoomId { get; init; }
    /// <summary>Gets host or guest.</summary>
    public required string Role { get; init; }
    /// <summary>Gets the registered local game environment used by this participant.</summary>
    public required string InstanceId { get; init; }
    /// <summary>Gets the room's local display name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the display name sent to the standard network profile.</summary>
    public string PlayerName { get; init; } = string.Empty;
    /// <summary>Gets an existing monitored game session; null starts the selected instance for a host.</summary>
    public string? SessionId { get; init; }
    /// <summary>Gets whether a host may publish the first observed local world in this room.</summary>
    public bool AutoOpen { get; init; } = true;
    /// <summary>Gets whether a guest starts its selected game automatically after the network entry is ready.</summary>
    public bool StartGame { get; init; } = true;
    /// <summary>Gets explicit permission to normally close the selected running session before joining another target.</summary>
    public bool RestartGame { get; init; }
    /// <summary>Gets the ordinary Core launch overrides, including an explicitly selected world or identity.</summary>
    public LaunchOptions Launch { get; init; } = new();
    /// <summary>Gets a guest invitation supplied in memory; task admission moves it into protected storage before serialization.</summary>
    [JsonIgnore]
    public string? Invitation { get; init; }
}

/// <summary>Reports a participant from the network protocol; it does not claim that the player entered Minecraft.</summary>
/// <param name="Id">An opaque local identifier derived from the remote machine ID.</param>
/// <param name="Name">The participant's display name.</param>
/// <param name="Role">host, local, or guest.</param>
public sealed record MultiplayerMember(string Id, string Name, string Role);

/// <summary>Reports a game-bound world observation without exporting its seed, directory or player position.</summary>
/// <param name="Name">The observed local world's display name.</param>
/// <param name="Epoch">The local world lifetime frozen by this room.</param>
/// <param name="State">loaded, opening, open, or unknown.</param>
public sealed record MultiplayerWorld(string Name, string Epoch, string State);

/// <summary>Contains the current room state, with independent game, network and process identity evidence.</summary>
public sealed record MultiplayerRoom
{
    /// <summary>Gets the task-bound room identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets host or guest.</summary>
    public required string Role { get; init; }
    /// <summary>Gets the local room title.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the selected registered instance.</summary>
    public required string InstanceId { get; init; }
    /// <summary>Gets preparing, waiting-world, opening, manual, discovering, connecting, starting-game, connected, disconnected, retrying, ending, exit-pending, closed, stopped, failed, or interrupted.</summary>
    public string State { get; init; } = "preparing";
    /// <summary>Gets whether a live task still owns this room's control loop.</summary>
    public bool Active { get; init; }
    /// <summary>Gets a bounded stable error code rather than raw component logs or credentials.</summary>
    public string? ErrorCode { get; init; }
    /// <summary>Gets the invitation only after a verified room becomes usable; it is omitted from the durable public snapshot.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Invitation { get; init; }
    /// <summary>Gets a guest's verified loopback entry or a caller-supplied fallback address.</summary>
    public string? Address { get; init; }
    /// <summary>Gets terracotta or fallback.</summary>
    public string Path { get; init; } = "terracotta";
    /// <summary>Gets the game monitor selected or started for this room.</summary>
    public string? SessionId { get; init; }
    /// <summary>Gets the exact game process admitted to this room, preventing a changed session record from closing another game.</summary>
    public int? GameProcessId { get; init; }
    /// <summary>Gets the admitted game process UTC start identity.</summary>
    public string? GameStartedAt { get; init; }
    /// <summary>Gets the first accepted explicit end transaction, retained while its game exit is observed.</summary>
    public string? EndOperationId { get; init; }
    /// <summary>Gets the durable normal-exit dispatch intent time; accepted or uncertain requests are observed without automatic replay.</summary>
    public DateTimeOffset? GameCloseRequestedAt { get; init; }
    /// <summary>Gets the verified host game listener, independently of component readiness.</summary>
    public int? GamePort { get; init; }
    /// <summary>Gets the current or last local-world observation.</summary>
    public MultiplayerWorld? World { get; init; }
    /// <summary>Gets network members, independently of in-game player counts.</summary>
    public MultiplayerMember[] Members { get; init; } = [];
    /// <summary>Gets the number of bounded automatic reconnect attempts in the current recovery sequence.</summary>
    public int RetryCount { get; init; }
    /// <summary>Gets the room owner's process identity.</summary>
    public int OwnerProcessId { get; init; }
    /// <summary>Gets the owner process UTC start time, preventing PID reuse.</summary>
    public string OwnerStartedAt { get; init; } = string.Empty;
    /// <summary>Gets the private component HTTP port for Core ownership checks.</summary>
    public int? ComponentPort { get; init; }
    /// <summary>Gets the last observed native component phase, independently of the game's LAN publication receipt.</summary>
    public string? ComponentState { get; init; }
    /// <summary>Gets the verified isolated component worker, independently of its short-lived bootstrap.</summary>
    public int? ComponentProcessId { get; init; }
    /// <summary>Gets that component worker's UTC start identity.</summary>
    public string? ComponentStartedAt { get; init; }
    /// <summary>Gets the latest update instant.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
    /// <summary>Gets the last consumed control identity, preventing duplicate application.</summary>
    public string? ControlId { get; init; }
}

/// <summary>Persists one exact room control without carrying invitation credentials.</summary>
/// <param name="Id">The unique control identity consumed once by the owner.</param>
/// <param name="RoomId">The room this control may affect.</param>
/// <param name="Action">The typed service-validated action discriminator.</param>
/// <param name="Address">An optional explicitly supplied fallback endpoint.</param>
internal sealed record RoomControl(string Id, string RoomId, string Action, string? Address = null);

/// <summary>Distinguishes ending only the network from explicitly requesting normal exit of this room's game.</summary>
public enum MultiplayerEndMode
{
    /// <summary>Stops the room without requesting game exit.</summary>
    KeepGame,
    /// <summary>Stops the room and requests normal exit of its exact admitted game, without forced termination.</summary>
    ExitGame
}
/// <summary>Stores sanitized state transitions for bounded diagnostic export.</summary>
/// <param name="At">The observed UTC instant.</param>
/// <param name="State">The local room state.</param>
/// <param name="Code">A stable error code without raw network data.</param>
/// <param name="ComponentState">The last observed native phase without invitation or address fields.</param>
/// <param name="GamePortObserved">Whether a game listener was verified, without exporting its port number.</param>
/// <param name="WorldState">The current publication observation, independently of network readiness.</param>
internal sealed record RoomEvent(DateTimeOffset At, string State, string? Code, string? ComponentState = null, bool GamePortObserved = false, string? WorldState = null);

/// <summary>Provides reflection-free room request and snapshot metadata to product hosts.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MultiplayerRequest))]
[JsonSerializable(typeof(MultiplayerRoom))]
public partial class MultiplayerJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RoomControl))]
[JsonSerializable(typeof(RoomEvent[]))]
[JsonSerializable(typeof(TerracottaState))]
[JsonSerializable(typeof(TerracottaMeta))]
[JsonSerializable(typeof(TerracottaBootstrap))]
internal partial class TerracottaJsonContext : JsonSerializerContext;

/// <summary>Reads the private bootstrap rendezvous.</summary>
/// <param name="Port">The worker HTTP listener to verify against the native process table.</param>
internal sealed record TerracottaBootstrap(int Port);
/// <summary>Describes the pinned native protocol version and local inspection channel.</summary>
/// <param name="Version">The native component version.</param>
/// <param name="ScaffoldingPort">The owned loopback command port used to verify the selected Minecraft listener.</param>
internal sealed record TerracottaMeta(string Version, [property: JsonPropertyName("yggdrasil_port")] int ScaffoldingPort);
/// <summary>Reads network identity without claiming a game account or avatar.</summary>
/// <param name="MachineId">The opaque remote network identity, hashed before public snapshots.</param>
/// <param name="Name">The participant-provided display name.</param>
/// <param name="Kind">The native HOST, LOCAL or GUEST role.</param>
internal sealed record TerracottaProfile([property: JsonPropertyName("machine_id")] string MachineId, string Name, string Kind);
internal sealed record TerracottaState
{
    /// <summary>Creates a response whose unknown protocol fields are populated independently of constructor binding.</summary>
    [JsonConstructor]
    public TerracottaState() { }
    /// <summary>Gets the component's native state discriminator.</summary>
    public string State { get; init; } = string.Empty;
    /// <summary>Gets the monotonically increasing component revision.</summary>
    public long Index { get; init; }
    /// <summary>Gets an operational native invitation, never copied into diagnostic output.</summary>
    public string? Room { get; init; }
    /// <summary>Gets the component's loopback game entry.</summary>
    public string? Url { get; init; }
    /// <summary>Gets the native exception category.</summary>
    public int? Type { get; init; }
    /// <summary>Gets network participant profiles, independently of in-game players.</summary>
    public TerracottaProfile[] Profiles { get; init; } = [];
    /// <summary>Retains unfamiliar protocol values without narrowing the upstream response.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}