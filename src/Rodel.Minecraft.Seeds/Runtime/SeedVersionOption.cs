// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Describes an original-version choice backed by registered game files or a successfully opened runtime.</summary>
/// <param name="Profile">Installed version metadata and optional Java selection.</param>
/// <param name="MinecraftVersion">Resolved original base version, independently of its loader profile name.</param>
/// <param name="Installed">Whether installed game files are available for selecting this choice.</param>
/// <param name="Error">Unsupported or unreadable profile explanation, when applicable.</param>
public sealed record SeedVersionOption(SeedRuntimeProfile Profile, string MinecraftVersion, bool Installed, string? Error = null)
{
    /// <summary>Gets registered instances supplying this resolved original version.</summary>
    public string[] InstanceIds { get; init; } = [];
    /// <summary>Gets the provider's release, snapshot, old_alpha or old_beta category when known.</summary>
    public string? Type { get; init; }
}