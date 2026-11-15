// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Content;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>Reads installed shader packs separately from the rendering engine's actual selection.</summary>
/// <param name="Installed">Recognized installed shader archives and folders.</param>
/// <param name="Engine">Selected configuration owner, or null when none can be determined.</param>
/// <param name="AvailableEngines">Configuration owners discovered from actual files or installed mod identities.</param>
/// <param name="Selected">Decoded shader-pack file identity, or null for no selection.</param>
/// <param name="Enabled">Whether the configuration enables shaders and names a pack.</param>
/// <param name="Present">Whether the named pack is currently available to the game.</param>
/// <param name="Fingerprint">Configuration concurrency fingerprint.</param>
/// <param name="Warnings">Nonblocking configuration or engine discovery observations.</param>
public sealed record ShaderPackSelectionSummary(IReadOnlyList<ContentEntry> Installed, string? Engine, IReadOnlyList<string> AvailableEngines,
    string? Selected, bool Enabled, bool Present, string Fingerprint, IReadOnlyList<string> Warnings);

/// <summary>Changes one rendering engine's shader selection; a null file name disables shaders.</summary>
public sealed record ShaderPackSelectionOptions
{
    /// <summary>Gets the installed file identity to use, or null to stop using shaders.</summary>
    public string? FileName { get; init; }
    /// <summary>Gets an explicit configuration owner: iris, oculus or optifine; null uses observed discovery.</summary>
    public string? Engine { get; init; }
    /// <summary>Gets the observed configuration fingerprint required before changing it.</summary>
    public string? ExpectedFingerprint { get; init; }
    /// <summary>Gets whether to inspect the change without enabling files or publishing configuration.</summary>
    public bool PlanOnly { get; init; }
}

/// <summary>Reports the actual shader configuration outcome without claiming that a game rendered it.</summary>
/// <param name="State">The operation state: planned, unchanged or applied.</param>
/// <param name="Selection">The observed or planned shader selection.</param>
/// <param name="RecoveryPath">Original configuration retained before publication, if it existed.</param>
public sealed record ShaderPackSelectionResult(string State, ShaderPackSelectionSummary Selection, string? RecoveryPath);