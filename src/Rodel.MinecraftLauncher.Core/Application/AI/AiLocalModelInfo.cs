// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>GGUF context facts for configuration; the declaration is not a device-memory guarantee.</summary>
/// <param name="DeclaredContextLength">The positive architecture context length, or null when unavailable.</param>
/// <param name="DefaultContextLength">The launcher default capped by the model declaration when known.</param>
public sealed record AiLocalModelInfo(int? DeclaredContextLength, int DefaultContextLength);