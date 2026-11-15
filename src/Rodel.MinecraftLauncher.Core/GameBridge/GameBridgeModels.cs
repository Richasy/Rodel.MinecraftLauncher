// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.GameBridge;

/// <summary>Describes the bridge owned by one actual game session, independently of window readiness.</summary>
public sealed record GameBridgeStatus
{
    /// <summary>Gets the launcher session identifier.</summary>
    public required string SessionId { get; init; }
    /// <summary>Gets disabled, starting, connected, disconnected, or unavailable.</summary>
    public required string State { get; init; }
    /// <summary>Gets the identity of this Java process incarnation, when connected.</summary>
    public string? GameIncarnation { get; init; }
    /// <summary>Gets the last bridge error code without game data or credentials.</summary>
    public string? ErrorCode { get; init; }
    /// <summary>Gets the supported operations; write availability still depends on the current world.</summary>
    public string[] Capabilities { get; init; } = [];
}

/// <summary>Contains a fresh observation sampled on the actual Minecraft client thread.</summary>
public sealed record GameSnapshot
{
    /// <summary>Gets the owning launcher session.</summary>
    public required string SessionId { get; init; }
    /// <summary>Gets the Java process identity established by the authenticated connection.</summary>
    public required string GameIncarnation { get; init; }
    /// <summary>Gets a monotonically increasing observation sequence, encoded without integer precision loss.</summary>
    public required string Sequence { get; init; }
    /// <summary>Gets the UTC sampling instant.</summary>
    public required string SampledAt { get; init; }
    /// <summary>Gets whether a player and world were present at sampling.</summary>
    public bool Ready { get; init; }
    /// <summary>Gets local_owned, remote_read_only, or unavailable, determined inside Java.</summary>
    public required string Mode { get; init; }
    /// <summary>Gets the world object generation; exiting and reentering revokes an older generation.</summary>
    public string? WorldEpoch { get; init; }
    /// <summary>Gets the owned integrated-server generation, stable across dimensions and respawns but revoked on world replacement.</summary>
    public string? LocalWorldEpoch { get; init; }
    /// <summary>Gets the dimension registry identifier.</summary>
    public string? Dimension { get; init; }
    /// <summary>Gets the client-synchronized player UUID.</summary>
    public string? PlayerId { get; init; }
    /// <summary>Gets the client-synchronized player X coordinate in blocks.</summary>
    public double? X { get; init; }
    /// <summary>Gets the client-synchronized player Y coordinate in blocks.</summary>
    public double? Y { get; init; }
    /// <summary>Gets the client-synchronized player Z coordinate in blocks.</summary>
    public double? Z { get; init; }
    /// <summary>Gets the observed horizontal player rotation, when the runtime exposes the native binding.</summary>
    public float? Yaw { get; init; }
    /// <summary>Gets client-synchronized health, not the entire remote server state.</summary>
    public float? Health { get; init; }
    /// <summary>Gets the observed world day time in ticks.</summary>
    public string? DayTime { get; init; }
    /// <summary>Gets the actual client sampling thread name for diagnostics.</summary>
    public string? ClientThread { get; init; }
    /// <summary>Gets a capability or readiness failure code without substituting an older observation.</summary>
    public string? ErrorCode { get; init; }
}

/// <summary>Freezes one bounded local edit against an explicitly observed game and world.</summary>
public sealed record GameEditRequest
{
    /// <summary>Gets the Java incarnation from a fresh snapshot.</summary>
    public required string GameIncarnation { get; init; }
    /// <summary>Gets the world generation from that snapshot.</summary>
    public required string WorldEpoch { get; init; }
    /// <summary>Gets the dimension registry ID from that snapshot.</summary>
    public required string Dimension { get; init; }
    /// <summary>Gets a caller-generated GUID in N format, reused only for querying this exact edit.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets a fixed edit, native command, player control, inventory click, screen close, or respawn operation.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets the requested time for world.setTime, between zero and Int64.MaxValue.</summary>
    public string? DayTime { get; init; }
    /// <summary>Gets the requested health for player.setHealth, from 1 through 20.</summary>
    public float? Health { get; init; }
    /// <summary>Gets one native command for command.execute; only an owned world with cheats enabled may execute it.</summary>
    public string? Command { get; init; }
    /// <summary>Gets bounded native input for player.control.</summary>
    public GameControlRequest? Control { get; init; }
    /// <summary>Gets an observed native menu action for inventory.click.</summary>
    public GameInventoryClick? Click { get; init; }
    /// <summary>Gets a native menu action payload for inventory.button.</summary>
    public GameInventoryButton? Button { get; init; }
}

/// <summary>Reports a bounded edit's actual in-memory outcome; applied does not imply disk persistence.</summary>
public sealed record GameOperation
{
    /// <summary>Gets the operation identity.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets queued, applying, applied, rejected, canceled, or unknown.</summary>
    public required string State { get; init; }
    /// <summary>Gets a precise failure code when execution was refused or its outcome is unknown.</summary>
    public string? ErrorCode { get; init; }
    /// <summary>Gets authoritative server-thread readback after a successful time edit.</summary>
    public string? DayTime { get; init; }
    /// <summary>Gets authoritative server-thread readback after a successful health edit.</summary>
    public float? Health { get; init; }
    /// <summary>Gets the actual game executor thread; native input and container dispatch use the client thread.</summary>
    public string? ServerThread { get; init; }
    /// <summary>Gets native command feedback or control completion details; applied alone does not prove a gameplay objective.</summary>
    public JsonElement? Result { get; init; }
}

/// <summary>Requests a bounded client-visible region around the player without loading new chunks.</summary>
/// <param name="GameIncarnation">The observed Java process identity.</param>
/// <param name="WorldEpoch">The observed world generation.</param>
/// <param name="Dimension">The observed dimension ID.</param>
/// <param name="X">The region's minimum X coordinate, within 64 blocks of the player.</param>
/// <param name="Y">The region's minimum Y coordinate, within 64 blocks of the player.</param>
/// <param name="Z">The region's minimum Z coordinate, within 64 blocks of the player.</param>
/// <param name="SizeX">The X extent, from 1 through 16.</param>
/// <param name="SizeY">The Y extent, from 1 through 16.</param>
/// <param name="SizeZ">The Z extent, from 1 through 16.</param>
public sealed record GameRegionRequest(string GameIncarnation, string WorldEpoch, string Dimension, int X, int Y, int Z, int SizeX = 1, int SizeY = 1, int SizeZ = 1);

/// <summary>Contains an explicitly partial, synchronized client observation; empty lists do not imply an empty server.</summary>
public sealed record GameRegion
{
    /// <summary>Gets the owning game session.</summary>
    public required string SessionId { get; init; }
    /// <summary>Gets the sampled Java process identity.</summary>
    public required string GameIncarnation { get; init; }
    /// <summary>Gets the sampled world generation.</summary>
    public required string WorldEpoch { get; init; }
    /// <summary>Gets the sampled dimension ID.</summary>
    public required string Dimension { get; init; }
    /// <summary>Gets the UTC client sampling time.</summary>
    public required string SampledAt { get; init; }
    /// <summary>Gets client_synchronized, identifying the visibility boundary of this data.</summary>
    public required string Source { get; init; }
    /// <summary>Gets whether the time or count budget omitted requested blocks, entities or inventory slots.</summary>
    public bool Truncated { get; init; }
    /// <summary>Gets sampled cells, distinguishing loaded cells from not_loaded.</summary>
    public GameBlock[] Blocks { get; init; } = [];
    /// <summary>Gets at most 128 visible entities within 32 blocks of the player, with a bounded scan.</summary>
    public GameEntity[] Entities { get; init; } = [];
    /// <summary>Gets nonempty client player inventory slots, with a maximum of 128 scanned slots.</summary>
    public GameInventorySlot[] Inventory { get; init; } = [];
}

/// <summary>Describes one observed region cell without NBT or forced chunk generation.</summary>
/// <param name="X">The block X coordinate.</param>
/// <param name="Y">The block Y coordinate.</param>
/// <param name="Z">The block Z coordinate.</param>
/// <param name="State">loaded or not_loaded.</param>
/// <param name="BlockId">The block registry identifier when loaded.</param>
public sealed record GameBlock(int X, int Y, int Z, string State, string? BlockId = null);
/// <summary>Describes a client-visible entity without custom text or private NBT.</summary>
/// <param name="Id">The entity UUID.</param>
/// <param name="TypeId">The entity type registry ID.</param>
/// <param name="X">The observed X coordinate.</param>
/// <param name="Y">The observed Y coordinate.</param>
/// <param name="Z">The observed Z coordinate.</param>
public sealed record GameEntity(string Id, string TypeId, double X, double Y, double Z);
/// <summary>Describes a nonempty client player inventory slot without item components or private NBT.</summary>
/// <param name="Slot">The native player inventory container index.</param>
/// <param name="ItemId">The item registry ID.</param>
/// <param name="Count">The synchronized stack count.</param>
public sealed record GameInventorySlot(int Slot, string ItemId, int Count);

internal sealed record BridgeRpc
{
    /// <summary>Gets the JSON-RPC protocol version.</summary>
    [JsonRequired]
    public string Jsonrpc { get; init; } = "2.0";
    /// <summary>Gets the bounded string request identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the fixed operation name for a request.</summary>
    public string? Method { get; init; }
    /// <summary>Gets typed request parameters serialized by their source-generated metadata.</summary>
    public JsonElement? Params { get; init; }
    /// <summary>Gets the typed result payload.</summary>
    public JsonElement? Result { get; init; }
    /// <summary>Gets a JSON-RPC failure.</summary>
    public BridgeRpcError? Error { get; init; }
}

internal sealed record BridgeRpcError
{
    /// <summary>Gets the JSON-RPC error category.</summary>
    public int Code { get; init; }
    /// <summary>Gets the stable bridge error code.</summary>
    public required string Message { get; init; }
}

internal sealed record BridgeHandshake
{
    /// <summary>Gets the protocol version.</summary>
    [JsonRequired]
    public int Version { get; init; } = 1;
    /// <summary>Gets the owning session.</summary>
    public required string SessionId { get; init; }
    /// <summary>Gets the fresh peer challenge.</summary>
    public required string Challenge { get; init; }
    /// <summary>Gets a challenge response HMAC, when present.</summary>
    public string? Proof { get; init; }
}

internal sealed record BridgeOperationTarget
{
    /// <summary>Gets the exact edit identifier to observe or cancel.</summary>
    public required string OperationId { get; init; }
}

internal sealed record BridgeCapabilities
{
    /// <summary>Gets the methods declared by this exact bridge binding.</summary>
    public required string[] Capabilities { get; init; }
}

internal sealed record BridgeJournalEntry
{
    /// <summary>Gets the immutable request fingerprint used to reject identity reuse.</summary>
    public required string Fingerprint { get; init; }
    /// <summary>Gets the original target and edit parameters.</summary>
    public required GameEditRequest Request { get; init; }
    /// <summary>Gets the most recent observed receipt, including unknown after interrupted submission.</summary>
    public required GameOperation Operation { get; init; }
}

/// <summary>Provides reflection-free metadata for public game bridge requests and results.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, MaxDepth = 32, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(GameBridgeStatus))]
[JsonSerializable(typeof(GameSnapshot))]
[JsonSerializable(typeof(GameEditRequest))]
[JsonSerializable(typeof(GameOperation))]
[JsonSerializable(typeof(GameRegionRequest))]
[JsonSerializable(typeof(GameRegion))]
[JsonSerializable(typeof(GameQueryRequest))]
[JsonSerializable(typeof(GameQueryResult))]
public partial class GameBridgeJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, MaxDepth = 32, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(BridgeRpc))]
[JsonSerializable(typeof(BridgeHandshake))]
[JsonSerializable(typeof(BridgeOperationTarget))]
[JsonSerializable(typeof(BridgeJournalEntry))]
[JsonSerializable(typeof(BridgeCapabilities))]
internal partial class BridgeWireJsonContext : JsonSerializerContext;