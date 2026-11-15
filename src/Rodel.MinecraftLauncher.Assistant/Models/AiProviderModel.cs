// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>A discovered provider model, not a saved Core binding.</summary>
/// <param name="Id">The provider model reference.</param>
/// <param name="DisplayName">The provider display name, or its reference when no name was supplied.</param>
internal sealed record AiProviderModel(string Id, string DisplayName);