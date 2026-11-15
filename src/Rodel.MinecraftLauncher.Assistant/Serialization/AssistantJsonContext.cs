// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Assistant;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, Converters = [typeof(AiUsageDetailsConverter)])]
[JsonSerializable(typeof(AiChatNativeState))]
[JsonSerializable(typeof(UsageDetails))]
[JsonSerializable(typeof(AiReasoningObservation))]
[JsonSerializable(typeof(AiProviderResult))]
[JsonSerializable(typeof(AiProviderRequest))]
[JsonSerializable(typeof(AiOpenAiChatRequest.Tool), TypeInfoPropertyName = "OpenAiChatTool")]
[JsonSerializable(typeof(AiOpenAiChatRequest.Tool[]), TypeInfoPropertyName = "OpenAiChatTools")]
[JsonSerializable(typeof(AiOpenAiResponsesRequest.Tool), TypeInfoPropertyName = "OpenAiResponsesTool")]
[JsonSerializable(typeof(AiOpenAiResponsesRequest.Tool[]), TypeInfoPropertyName = "OpenAiResponsesTools")]
[JsonSerializable(typeof(AiAnthropicRequest.Tool), TypeInfoPropertyName = "AnthropicTool")]
[JsonSerializable(typeof(AiAnthropicRequest.Tool[]), TypeInfoPropertyName = "AnthropicTools")]
[JsonSerializable(typeof(AiOpenAiChatRequest.FunctionDefinition), TypeInfoPropertyName = "OpenAiFunction")]
[JsonSerializable(typeof(AiGeminiRequest.FunctionDefinition), TypeInfoPropertyName = "GeminiFunction")]
[JsonSerializable(typeof(AiMcpRegistration))]
[JsonSerializable(typeof(AiMcpCallResult))]
[JsonSerializable(typeof(AiMcpCatalogWarning))]
internal sealed partial class AssistantJsonContext : JsonSerializerContext;