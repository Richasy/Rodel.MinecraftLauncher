// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.GameBridge;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Tools;

/// <summary>Targets one actual game session.</summary>
/// <param name="SessionId">The explicit session ID returned by launch or process_list.</param>
internal sealed record GameSessionInput(string SessionId);
/// <summary>Targets one retained edit receipt without submitting another edit.</summary>
/// <param name="SessionId">The owning game session.</param>
/// <param name="OperationId">The exact edit ID returned by game_edit.</param>
internal sealed record GameOperationInput(string SessionId, string OperationId);
/// <summary>Submits a fixed local operation against an observed game/world generation.</summary>
/// <param name="SessionId">The explicit owning game session.</param>
/// <param name="Request">The bounded edit, including its unique identity and observed world.</param>
internal sealed record GameEditInput(string SessionId, GameEditRequest Request);
/// <summary>Reads a bounded client-synchronized region of an explicitly observed world.</summary>
/// <param name="SessionId">The owning session.</param>
/// <param name="Request">The observed world identity and region bounds.</param>
internal sealed record GameRegionInput(string SessionId, GameRegionRequest Request);
/// <summary>Reads a semantic observation or a bounded native NBT selection.</summary>
/// <param name="SessionId">The owning game session.</param>
/// <param name="Request">The exact world identity, query kind and selection.</param>
internal sealed record GameQueryInput(string SessionId, GameQueryRequest Request);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(GameSessionInput))]
[JsonSerializable(typeof(GameOperationInput))]
[JsonSerializable(typeof(GameEditInput))]
[JsonSerializable(typeof(GameRegionInput))]
[JsonSerializable(typeof(GameQueryInput))]
internal partial class GameBridgeToolJsonContext : JsonSerializerContext;