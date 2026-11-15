// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>A tool name mapping and its external input schema.</summary>
public sealed record AiMcpToolDescription
{
    /// <summary>Gets the scrubbed source-qualified model-visible tool name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the scrubbed original server-side tool name.</summary>
    public required string ActualName { get; init; }
    /// <summary>Gets the scrubbed external JSON schema without imposing launcher-owned fields.</summary>
    public required JsonObject InputSchema { get; init; }
}