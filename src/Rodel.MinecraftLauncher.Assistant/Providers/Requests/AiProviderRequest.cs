// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Assistant;

[JsonDerivedType(typeof(AiOpenAiChatRequest))]
[JsonDerivedType(typeof(AiOpenAiResponsesRequest))]
[JsonDerivedType(typeof(AiAnthropicRequest))]
[JsonDerivedType(typeof(AiGeminiRequest))]
internal abstract record AiProviderRequest;