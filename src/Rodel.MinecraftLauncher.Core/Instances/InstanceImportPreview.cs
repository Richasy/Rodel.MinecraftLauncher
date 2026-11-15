// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Instances;

/// <summary>The closed choice between a non-mutating preview and a registered instance, without a JSON transport bag.</summary>
[JsonDerivedType(typeof(InstanceImportPreview))]
[JsonDerivedType(typeof(InstanceSummary))]
public interface IInstanceImportResult;

/// <summary>A non-mutating migration preview preserving original launcher settings and field provenance.</summary>
/// <param name="Format">Recognized source launcher format.</param>
/// <param name="SourceDirectory">Original source directory.</param>
/// <param name="GameDirectory">Selected game-content directory.</param>
/// <param name="MinecraftDirectory">Selected version and shared-dependency root.</param>
/// <param name="VersionId">Selected launch profile, if known.</param>
/// <param name="LocalVersions">Available local profile identifiers.</param>
/// <param name="NeedsGameDirectory">Whether isolation ambiguity requires a game-directory choice.</param>
/// <param name="GameDirectoryCandidates">Actual discovered directory candidates.</param>
/// <param name="Name">Source display name, if declared.</param>
/// <param name="ProfileId">Selected source profile identifier.</param>
/// <param name="Settings">Recognized extensible launcher configuration.</param>
/// <param name="Fields">Field provenance keyed by open configuration names.</param>
/// <param name="SourceSettings">Original third-party settings and unknown fields.</param>
/// <param name="Warnings">Recoverable migration warnings.</param>
/// <param name="SourceModified">Always false for a read-only preview.</param>
public sealed record InstanceImportPreview(string Format, string SourceDirectory, string GameDirectory, string MinecraftDirectory, string? VersionId,
    IReadOnlyList<string> LocalVersions, bool NeedsGameDirectory, IReadOnlyList<string> GameDirectoryCandidates, string? Name, string? ProfileId,
    JsonObject Settings, JsonObject Fields, JsonObject SourceSettings, JsonArray Warnings, bool SourceModified = false) : IInstanceImportResult;