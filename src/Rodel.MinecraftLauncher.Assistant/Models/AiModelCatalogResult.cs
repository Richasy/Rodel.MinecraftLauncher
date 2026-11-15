// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>A read-only model discovery result with no generation or saved-configuration effects.</summary>
/// <param name="Models">The bounded, deduplicated discovery snapshot.</param>
internal record AiModelCatalogResult(AiProviderModel[] Models) : AiProviderResult
{
    /// <summary>Gets whether discovery preserved all saved bindings; always true.</summary>
    public bool ConfiguredModelsUnchanged { get; } = true;
    /// <summary>Gets whether listing tested generation; always false.</summary>
    public bool GenerationTested { get; } = false;
}