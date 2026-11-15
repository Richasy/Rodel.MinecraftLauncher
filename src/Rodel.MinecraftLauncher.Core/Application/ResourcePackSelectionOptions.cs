// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed record ResourcePackSelectionOptions
{
    public IReadOnlyList<string> Names { get; init; } = [];
    public string? Encoding { get; init; }
    public string? ExpectedFingerprint { get; init; }
    public bool Replace { get; init; }
    public bool AllowIncompatible { get; init; }
    public bool PlanOnly { get; init; }
}