// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant;

internal sealed class AiProtocolException(string code, string message, JsonNode? details = null) : Exception(message)
{
    /// <summary>Gets the stable provider/protocol failure identifier consumed by Core.</summary>
    public string Code { get; } = code;
    /// <summary>Gets optional protocol diagnostics; Core sanitizes them before exposing a public result.</summary>
    public JsonNode? Details { get; } = details;
}