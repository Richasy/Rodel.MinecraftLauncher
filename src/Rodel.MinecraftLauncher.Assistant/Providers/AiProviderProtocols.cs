// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Assistant;

internal static class AiProviderProtocols
{
    public const string OpenAiResponses = "openai-responses";
    public const string OpenAiChat = "openai-chat";
    public const string AnthropicMessages = "anthropic-messages";
    public const string Gemini = "gemini";
    public const string RodelInference = "rodel-inference";
    /// <summary>Recognizes implemented wire protocols and the Embedded local-engine path.</summary>
    public static bool IsKnown(string protocol) => protocol is OpenAiResponses or OpenAiChat or AnthropicMessages or Gemini or RodelInference;
}