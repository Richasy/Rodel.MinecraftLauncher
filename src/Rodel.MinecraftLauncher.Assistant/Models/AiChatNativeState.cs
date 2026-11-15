// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>Binds opaque provider continuation data to the exact connection that produced it.</summary>
/// <param name="Protocol">The provider protocol identifier.</param>
/// <param name="ModelId">The Core model-binding identity, not the provider's model name.</param>
/// <param name="ServiceRevision">The captured service configuration revision.</param>
/// <param name="Payload">Unprojected provider message items, parts or blocks.</param>
/// <param name="ContentHash">The standard content snapshot hash, invalidated by edits or compaction.</param>
internal sealed record AiChatNativeState(string Protocol, string ModelId, long ServiceRevision, JsonNode Payload, string? ContentHash = null);