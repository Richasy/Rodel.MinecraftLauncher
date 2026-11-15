// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Assistant;

internal sealed record AiProviderContext(string Protocol, long ServiceRevision, string ModelId, string Model,
    string? Credential, double? Temperature, int? MaxOutputTokens, AiLocalInferenceRuntime? LocalInference = null, int? ContextLength = null);