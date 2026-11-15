// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>Separates host correlation from schema-free third-party tool payloads.</summary>
/// <param name="SourceId">The unchanged Core-owned registration identity.</param>
/// <param name="ToolName">The sanitized original server-side tool name.</param>
/// <param name="Content">Text blocks and explicit placeholders for unsupported block kinds.</param>
/// <param name="StructuredContent">The external structured result, preserved without imposing a launcher schema.</param>
internal sealed record AiMcpCallData(string SourceId, string ToolName, AiMcpContent[] Content, JsonNode? StructuredContent);