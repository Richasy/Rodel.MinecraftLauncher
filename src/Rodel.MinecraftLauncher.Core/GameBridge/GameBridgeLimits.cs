// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.GameBridge;

/// <summary>Defines the domain limits shared by Core validation, public schemas and native presentation.</summary>
public static class GameBridgeLimits
{
    /// <summary>Bounds retained operation identities while allowing long gameplay sessions.</summary>
    public const int MaximumRetainedOperations = 100000;
    /// <summary>Bounds one native input lease to five seconds of normal game ticks.</summary>
    public const int MaximumControlTicks = 100;
    /// <summary>Bounds a semantic list query's page size.</summary>
    public const int MaximumQueryItems = 100;
    /// <summary>Gets the canonical operation identifiers shared by validation and generated MCP constraints.</summary>
    public static IReadOnlyList<string> EditKinds { get; } = ["world.setTime", "world.publish", "player.setHealth", "command.execute", "player.control", "player.release", "inventory.click", "inventory.button", "screen.close", "player.respawn"];
    /// <summary>Gets the canonical semantic observation families.</summary>
    public static IReadOnlyList<string> QueryKinds { get; } = ["player", "inventory", "screen", "block", "entity", "world", "registry", "recipes", "advancements", "quests", "books"];
    /// <summary>Gets the native player input roles accepted by an input lease.</summary>
    public static IReadOnlyList<string> ControlKeys { get; } = ["forward", "back", "left", "right", "jump", "sneak", "sprint", "attack", "use"];
    /// <summary>Gets the game's native container click modes.</summary>
    public static IReadOnlyList<string> ClickModes { get; } = ["PICKUP", "QUICK_MOVE", "SWAP", "CLONE", "THROW", "QUICK_CRAFT", "PICKUP_ALL"];
    /// <summary>Gets the smallest nonlethal health value accepted by the bounded edit.</summary>
    public const int MinimumHealth = 1;
    /// <summary>Gets the largest health value supported by this edit capability.</summary>
    public const int MaximumHealth = 20;
    /// <summary>Gets the largest region extent along one axis; at most 4096 cells are requested.</summary>
    public const int MaximumRegionEdge = 16;
    /// <summary>Gets the largest absolute X or Z coordinate supported by this observation capability.</summary>
    public const int MaximumHorizontalCoordinate = 30000000;
    /// <summary>Gets the largest absolute Y coordinate supported by this observation capability.</summary>
    public const int MaximumVerticalCoordinate = 4096;
}