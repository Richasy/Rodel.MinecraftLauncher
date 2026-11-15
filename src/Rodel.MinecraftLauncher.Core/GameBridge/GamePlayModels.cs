// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.GameBridge;

/// <summary>Requests one semantic game observation bound to the currently observed world.</summary>
public sealed record GameQueryRequest
{
    /// <summary>Creates a query with explicit paging defaults for source-generated deserialization.</summary>
    /// <param name="offset">The first list entry.</param>
    /// <param name="limit">The maximum entries returned.</param>
    [System.Text.Json.Serialization.JsonConstructor]
    public GameQueryRequest(int offset = 0, int limit = 50) { Offset = offset; Limit = limit; }
    /// <summary>Gets the authenticated Java incarnation from a current snapshot.</summary>
    public required string GameIncarnation { get; init; }
    /// <summary>Gets the current client world generation, never an assumed earlier dimension.</summary>
    public required string WorldEpoch { get; init; }
    /// <summary>Gets the current dimension registry identifier.</summary>
    public required string Dimension { get; init; }
    /// <summary>Gets player, inventory, screen, block, entity, world, registry, recipes, advancements, quests, or books.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets the target entity UUID; omission selects the current player for entity observations.</summary>
    public string? EntityId { get; init; }
    /// <summary>Gets an optional exact registry or recipe identifier, or an advancement prefix.</summary>
    public string? Resource { get; init; }
    /// <summary>Gets an optional case-insensitive text filter for recipe outputs, quest text, or book entries; it is never executable code.</summary>
    public string? Search { get; init; }
    /// <summary>Gets an optional native Minecraft NBT path; it is never a Java expression.</summary>
    public string? NbtPath { get; init; }
    /// <summary>Gets the block X coordinate for a block observation.</summary>
    public int? X { get; init; }
    /// <summary>Gets the block Y coordinate for a block observation.</summary>
    public int? Y { get; init; }
    /// <summary>Gets the block Z coordinate for a block observation.</summary>
    public int? Z { get; init; }
    /// <summary>Gets the zero-based page offset for a registry, recipe, or advancement observation.</summary>
    public int Offset { get; init; }
    /// <summary>Gets the maximum page size, from 1 through 100.</summary>
    public int Limit { get; init; } = 50;
}

/// <summary>Returns one fresh semantic observation, preserving native or mod-defined data as open JSON.</summary>
public sealed record GameQueryResult
{
    /// <summary>Gets the owning launcher session.</summary>
    public required string SessionId { get; init; }
    /// <summary>Gets the authenticated Java incarnation.</summary>
    public required string GameIncarnation { get; init; }
    /// <summary>Gets the world generation actually observed.</summary>
    public required string WorldEpoch { get; init; }
    /// <summary>Gets the dimension actually observed.</summary>
    public required string Dimension { get; init; }
    /// <summary>Gets the UTC sampling instant.</summary>
    public required string SampledAt { get; init; }
    /// <summary>Gets client_synchronized, server_authoritative, or game_registry.</summary>
    public required string Source { get; init; }
    /// <summary>Gets the semantic observation kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets the native observation; NBT is explicitly identified as SNBT rather than guessed JSON.</summary>
    public required JsonElement Data { get; init; }
}

/// <summary>Holds native player inputs for a bounded number of actual game ticks, then releases them.</summary>
public sealed record GameControlRequest
{
    /// <summary>Creates an input lease with explicit defaults for source-generated deserialization.</summary>
    /// <param name="keys">Native keys to hold, or no keys for orientation alone.</param>
    /// <param name="ticks">The duration in actual game ticks.</param>
    /// <param name="interactionDelayTicks">The tick at which attack/use begins after movement starts.</param>
    [System.Text.Json.Serialization.JsonConstructor]
    public GameControlRequest(string[]? keys = null, int ticks = 1, int interactionDelayTicks = 1) { Keys = keys ?? []; Ticks = ticks; InteractionDelayTicks = interactionDelayTicks; }
    /// <summary>Gets forward, back, left, right, jump, sneak, sprint, attack, or use keys to hold.</summary>
    public string[] Keys { get; init; } = [];
    /// <summary>Gets the duration from 1 through 100 game ticks; a wall-clock watchdog also releases input.</summary>
    public int Ticks { get; init; } = 1;
    /// <summary>Gets the attack/use start tick within the lease, from one through Ticks; movement starts immediately.</summary>
    public int InteractionDelayTicks { get; init; } = 1;
    /// <summary>Gets an optional absolute yaw in degrees, between -360 and 360.</summary>
    public float? Yaw { get; init; }
    /// <summary>Gets an optional absolute pitch in degrees, between -90 and 90.</summary>
    public float? Pitch { get; init; }
    /// <summary>Gets an optional selected hotbar index, from zero through eight.</summary>
    public int? Hotbar { get; init; }
}

/// <summary>Clicks an observed native container slot through Minecraft's normal multiplayer game-mode handler.</summary>
public sealed record GameInventoryClick
{
    /// <summary>Gets the observed native menu identity.</summary>
    public required int ContainerId { get; init; }
    /// <summary>Gets its observed synchronization state; stale states are rejected before dispatch.</summary>
    public required int StateId { get; init; }
    /// <summary>Gets a native slot index, or -999 for the native outside-menu action.</summary>
    public required int Slot { get; init; }
    /// <summary>Gets the native mouse/hotbar button encoding appropriate to Mode.</summary>
    public int Button { get; init; }
    /// <summary>Gets PICKUP, QUICK_MOVE, SWAP, CLONE, THROW, QUICK_CRAFT, or PICKUP_ALL.</summary>
    public required string Mode { get; init; }
}

/// <summary>Activates a native menu action such as an enchantment offer without bypassing its server validation.</summary>
public sealed record GameInventoryButton
{
    /// <summary>Gets the observed native menu identity.</summary>
    public required int ContainerId { get; init; }
    /// <summary>Gets its observed synchronization state.</summary>
    public required int StateId { get; init; }
    /// <summary>Gets the menu's native button index, from zero through 255.</summary>
    public required int Button { get; init; }
}