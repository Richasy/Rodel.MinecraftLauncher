// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Assistant;

[JsonDerivedType(typeof(AiTextResult))]
[JsonDerivedType(typeof(AiHttpTextResult))]
[JsonDerivedType(typeof(AiModelCatalogResult))]
[JsonDerivedType(typeof(AiHttpModelCatalogResult))]
internal abstract record AiProviderResult
{
    /// <summary>Gets the duration of this bounded provider operation.</summary>
    public long ElapsedMilliseconds { get; init; }
    /// <summary>Gets the tool count; isolated provider probes never offer tools.</summary>
    public int ToolsOffered { get; } = 0;
    /// <summary>Gets whether this operation retains conversation state; always false.</summary>
    public bool PersistentConversation { get; } = false;
}