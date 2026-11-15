// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Runtime;

/// <summary>Per-operation choices for planning or changing an instance runtime.</summary>
public sealed record RuntimeChangeOptions
{
    /// <summary>Base game version; inferred from the selected profile when omitted.</summary>
    public string? MinecraftVersion { get; init; }

    /// <summary>Exact existing or vanilla profile ID; takes precedence over loader installation.</summary>
    public string? VersionId { get; init; }
    public string? Loader { get; init; }
    public string? LoaderVersion { get; init; }
    public string? JavaPath { get; init; }
    public IReadOnlyList<string> LibraryMirrors { get; init; } = [];

    /// <summary>Selects vanilla while retaining local mods, settings and worlds.</summary>
    public bool RemoveLoader { get; init; }

    /// <summary>Describes the proposed selection without installing or committing it.</summary>
    public bool PlanOnly { get; init; }

    /// <summary>Uses an exact locally installed profile without downloading dependencies.</summary>
    public bool Offline { get; init; }
}