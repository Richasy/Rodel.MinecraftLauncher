// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Content;

/// <summary>Describes one launcher-owned mod and its actual installation in the selected instance.</summary>
public sealed record BundledMod
{
    /// <summary>Gets the mod identifier used for selection and existing-package matching across loaders.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the package's display name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the exact version shipped with this launcher.</summary>
    public required string Version { get; init; }
    /// <summary>Gets the declared Minecraft version.</summary>
    public required string MinecraftVersion { get; init; }
    /// <summary>Gets the exact loader for this built artifact.</summary>
    public required string Loader { get; init; }
    /// <summary>Gets the currently installed version, including disabled packages.</summary>
    public string? InstalledVersion { get; init; }
    /// <summary>Gets whether the shipped bytes are already installed and enabled.</summary>
    public bool Installed { get; init; }
    /// <summary>Gets the other visible bundled mods required by this choice.</summary>
    public required IReadOnlyList<string> Requires { get; init; }
}

/// <summary>Reports the shipped choices against the selected instance's real runtime context.</summary>
public sealed record BundledModCatalog
{
    /// <summary>Gets the owning instance, retained by callers across asynchronous selection.</summary>
    public required string InstanceId { get; init; }
    /// <summary>Gets resolved compatibility evidence, including unknown or manually configured runtime data.</summary>
    public required ContentCompatibility Context { get; init; }
    /// <summary>Gets a known incompatibility reason, or null when no incompatible runtime was established.</summary>
    public string? Incompatibility { get; init; }
    /// <summary>Gets exact Minecraft targets included in this launcher build.</summary>
    public required IReadOnlyList<string> MinecraftVersions { get; init; }
    /// <summary>Gets the exact shipped Minecraft/loader combinations for availability feedback.</summary>
    public IReadOnlyList<string> RuntimeTargets { get; init; } = [];
    /// <summary>Gets the three visible launcher-owned choices; shared dependencies are installed automatically.</summary>
    public required IReadOnlyList<BundledMod> Mods { get; init; }
}