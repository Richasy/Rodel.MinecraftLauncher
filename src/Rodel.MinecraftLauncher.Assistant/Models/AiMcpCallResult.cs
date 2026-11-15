// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>A bounded MCP transport response, not independent evidence of a durable business effect.</summary>
/// <param name="Status">Succeeded or failed according to the server's MCP error flag.</param>
/// <param name="Data">The source identity and sanitized external response content.</param>
internal sealed record AiMcpCallResult(string Status, AiMcpCallData Data);