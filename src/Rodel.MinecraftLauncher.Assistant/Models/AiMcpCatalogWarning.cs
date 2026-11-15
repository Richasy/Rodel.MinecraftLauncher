// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>Identifies one rejected external tool without retaining its unsafe description or schema.</summary>
/// <param name="Code">The host-owned catalog diagnostic identifier.</param>
/// <param name="ToolOrdinal">The entry's zero-based position in discovery order.</param>
public sealed record AiMcpCatalogWarning(string Code, int ToolOrdinal);