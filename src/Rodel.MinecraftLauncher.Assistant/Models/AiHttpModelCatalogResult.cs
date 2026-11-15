// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>Adds HTTP request correlation without adding HTTP-only fields to local SDK results.</summary>
/// <param name="Models">The discovered native model references.</param>
/// <param name="RequestId">The sanitized last available HTTP request identity, or null.</param>
internal sealed record AiHttpModelCatalogResult(AiProviderModel[] Models, string? RequestId) : AiModelCatalogResult(Models);