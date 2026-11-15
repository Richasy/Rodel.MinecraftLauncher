// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Packs;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed record PackWorkflowOptions
{
    /// <summary>Assigns a newly imported client to this server favorite before runtime preparation; null keeps an ordinary local pack.</summary>
    public string? ServerId { get; init; }
    public string? Name { get; init; }
    public string? ManualDirectory { get; init; }
    public string Platform { get; init; } = "modrinth";
    public string? ProjectId { get; init; }
    public string? VersionId { get; init; }
    /// <summary>Restricts automatic online pack selection to release, beta-or-release, or all alpha/beta/release versions; exact versions remain explicit choices.</summary>
    public string? Channel { get; init; }
    public PackOptionalSelection? OptionalSelection { get; init; }
    public IReadOnlyList<string> LibraryMirrors { get; init; } = [];
    public bool Offline { get; init; }
    public bool PlanOnly { get; init; }
    public bool ContentOnly { get; init; }
}

public sealed record ContentWorkflowOptions
{
    /// <summary>Gets whether inventory reading computes current file hashes; false is a display snapshot, never mutation authority.</summary>
    public bool IncludeHashes { get; init; } = true;
    public string Kind { get; init; } = "mods";
    public string? World { get; init; }
    public string Platform { get; init; } = "modrinth";
    public string? VersionId { get; init; }
    public string? MinecraftVersion { get; init; }
    public string? Loader { get; init; }
    public string Algorithm { get; init; } = "sha1";
    public bool Attach { get; init; }
    public string? ExpectedProjectId { get; init; }
    public bool PlanOnly { get; init; }
    public bool UseSourceContext { get; init; }
    public ContentInstallOptions InstallOptions { get; init; } = new();
}

public sealed record InstanceImportOptions
{
    /// <summary>Assigns the completed import to this server favorite without changing its physical storage layout.</summary>
    public string? ServerId { get; init; }
    public string? GameDirectory { get; init; }
    public string? VersionId { get; init; }
    public string? Profile { get; init; }
    public string? MinecraftDirectory { get; init; }
    public string? LauncherConfiguration { get; init; }
    public bool Migrate { get; init; }
    public bool Copy { get; init; }
    public bool PlanOnly { get; init; }
}

public sealed record InstanceInstallationOptions
{
    /// <summary>Discover or prepare Java after installing game files, without starting the game.</summary>
    public bool PrepareJava { get; init; }
    public string? VersionId { get; init; }
    public string? JavaPath { get; init; }
    public bool PlanOnly { get; init; }
}

public sealed record LoaderInstallationOptions
{
    public string? MinecraftVersion { get; init; }
    public string? LoaderVersion { get; init; }
    public string? JavaPath { get; init; }
    public IReadOnlyList<string> LibraryMirrors { get; init; } = [];
}