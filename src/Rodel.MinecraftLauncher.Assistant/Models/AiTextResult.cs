// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;

namespace Rodel.MinecraftLauncher.Assistant;

internal record AiTextResult : AiProviderResult
{
    /// <summary>Gets the normalized completed, incomplete or refused outcome.</summary>
    public required string Status { get; init; }
    /// <summary>Gets the provider's visible answer.</summary>
    public required string Text { get; init; }
    /// <summary>Gets the provider's terminal reason without interpreting it as tool execution evidence.</summary>
    public string? FinishReason { get; init; }
    /// <summary>Gets the actual provider response identifier, if supplied.</summary>
    public string? ResponseId { get; init; }
    /// <summary>Gets the actual provider model identifier, if supplied.</summary>
    public string? Model { get; init; }
    /// <summary>Gets reported token accounting without estimating missing counts.</summary>
    public UsageDetails? Usage { get; init; }
}