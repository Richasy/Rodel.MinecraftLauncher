// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Assistant;

internal sealed record AiHttpTextResult : AiTextResult
{
    /// <summary>Gets the provider's refusal text or block reason.</summary>
    public string? Refusal { get; init; }
    /// <summary>Gets the sanitized HTTP request identifier, if supplied.</summary>
    public string? RequestId { get; init; }
}