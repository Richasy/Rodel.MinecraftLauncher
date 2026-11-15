// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.GameBridge;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

public static class AiChatStates
{
    public static bool IsTerminal(string? status) => status is "completed" or "canceled" or "failed" or "incomplete" or "interrupted" or "refused";
}

public sealed record AiChatCreate
{
    public string? Title { get; init; }
    public string? ModelId { get; init; }
    public string? InstanceId { get; init; }
    /// <summary>Gets the owning game session for temporary in-game history; null creates an independent persistent launcher conversation.</summary>
    public string? GameSessionId { get; init; }
    /// <summary>Gets whether to name the conversation automatically: a prompt label for local models or an independent bounded cloud title request.</summary>
    public bool GenerateTitle { get; init; }
    /// <summary>Gets a host-created skin grant; a skin conversation exposes only its bound candidate-generation tools.</summary>
    public string? SkinScopeId { get; init; }
    /// <summary>Gets a player-created repair incident; its exact instance and reversible tool scope remain fixed.</summary>
    public string? RepairId { get; init; }
}

public sealed record AiChatSend
{
    /// <summary>Gets a fresh game observation binding for game-originated turns; it cannot retarget another instance or world.</summary>
    public AiGameContext? GameContext { get; init; }
    public required string ConversationId { get; init; }
    public required string ClientKey { get; init; }
    public required string Prompt { get; init; }
    public required long ExpectedRevision { get; init; }
    public string? ModelId { get; init; }
    public bool UseTools { get; init; } = true;
    /// <summary>Gets whether this entry can display and answer interactive user questions; console turns disable this capability.</summary>
    public bool AllowUserPrompts { get; init; } = true;
    public bool AllowOperations { get; init; }
    public int MaximumModelCalls { get; init; } = 64;
    /// <summary>Gets an explicit per-request output-token limit; null leaves the saved model setting or provider default unchanged.</summary>
    public int? MaximumOutputTokens { get; init; }
    /// <summary>Gets the active execution budget in seconds, excluding explicit human decisions and independent background-job waits.</summary>
    public int TimeoutSeconds { get; init; } = 600;
    public int? OwnerPid { get; init; }
    public string? OwnerStartedUtc { get; init; }
    public string? RetryTurnId { get; init; }
    /// <summary>Gets up to three managed reference-image identities frozen before submission; paths and network URLs are not accepted.</summary>
    public string[]? ImageIds { get; init; }
}

/// <summary>Binds a game-originated turn to one authenticated Java process and observed world.</summary>
/// <param name="SessionId">The actual launcher game session.</param>
/// <param name="GameIncarnation">The authenticated Java process generation.</param>
/// <param name="WorldEpoch">The client world generation observed at submission.</param>
/// <param name="LocalWorldEpoch">The integrated server identity, retained across dimension changes.</param>
/// <param name="Dimension">The dimension observed at submission.</param>
public sealed record AiGameContext(string SessionId, string GameIncarnation, string WorldEpoch, string? LocalWorldEpoch, string Dimension)
{
    /// <summary>Captures stable authorization fields without volatile sequence, position or clock values.</summary>
    public static AiGameContext FromSnapshot(GameSnapshot snapshot) => new(snapshot.SessionId, snapshot.GameIncarnation, snapshot.WorldEpoch!, snapshot.LocalWorldEpoch, snapshot.Dimension!);
}

public sealed record AiConversationUpdate
{
    public required string ConversationId { get; init; }
    public required long ExpectedRevision { get; init; }
    public string? Title { get; init; }
    /// <summary>Gets the instance binding for future turns; null leaves it unchanged unless ClearInstance is set.</summary>
    public string? InstanceId { get; init; }
    /// <summary>Gets whether future turns should have no instance binding.</summary>
    public bool ClearInstance { get; init; }
    /// <summary>Gets a configured model for future turns, leaving existing turn snapshots intact.</summary>
    public string? ModelId { get; init; }
    /// <summary>Gets the conversation permission mode: read_only, safe (operations need approval), or allow_operations.</summary>
    public string? Mode { get; init; }
    public bool? FileAccessEnabled { get; init; }
    public bool? NetworkAccessEnabled { get; init; }
}

/// <summary>Cancels one turn and optionally discards its conversation's pending submissions atomically.</summary>
public sealed record AiChatCancellation
{
    /// <summary>Gets the conversation containing the selected turn and optional queue.</summary>
    public required string ConversationId { get; init; }
    /// <summary>Gets the turn whose current execution should stop.</summary>
    public required string TurnId { get; init; }
    /// <summary>Gets whether to cancel all still-pending turns in the same operation before stopping the selected turn.</summary>
    public bool CancelQueued { get; init; }
}

public sealed record AiConversationExport
{
    public required string ConversationId { get; init; }
    public required string Destination { get; init; }
    public string Format { get; init; } = "json";
    public bool Overwrite { get; init; }
}

public sealed record AiConversationRemoval
{
    public required string ConversationId { get; init; }
    public required long ExpectedRevision { get; init; }
    public required string Fingerprint { get; init; }
    public bool Confirm { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AiChatCreate))]
[JsonSerializable(typeof(AiChatSend))]
[JsonSerializable(typeof(AiChatCancellation))]
[JsonSerializable(typeof(AiChatImage[]))]
[JsonSerializable(typeof(AiConversationUpdate))]
[JsonSerializable(typeof(AiConversationExport))]
[JsonSerializable(typeof(AiConversationRemoval))]
public sealed partial class AiChatJsonContext : JsonSerializerContext;