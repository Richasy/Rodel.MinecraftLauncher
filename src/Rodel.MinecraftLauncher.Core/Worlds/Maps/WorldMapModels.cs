// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Identifies one save inside a registered instance, without accepting an arbitrary filesystem path.</summary>
/// <param name="InstanceId">Registered instance identifier.</param>
/// <param name="World">Single save folder or finite-world file name.</param>
public sealed record WorldMapSelection(string InstanceId, string World);

/// <summary>Contains an actual saved player position; missing coordinates never become the world origin.</summary>
/// <param name="Id">Stored UUID or the singleplayer record identifier.</param>
/// <param name="Name">Stored player name, when present.</param>
/// <param name="Dimension">Stored dimension identifier.</param>
/// <param name="X">Saved X coordinate.</param>
/// <param name="Y">Saved Y coordinate.</param>
/// <param name="Z">Saved Z coordinate.</param>
/// <param name="Yaw">Saved horizontal rotation.</param>
/// <param name="SavedAt">Last write time of the file supplying this position.</param>
public sealed record SavedWorldPlayer(string Id, string? Name, string Dimension, double X, double Y, double Z, float? Yaw, DateTimeOffset SavedAt);

/// <summary>Describes the bounded save snapshot used by one map session, independently of original generation.</summary>
public sealed record WorldMapSnapshot
{
    /// <summary>Gets the exact registered instance and selected save.</summary>
    public required WorldMapSelection Selection { get; init; }
    /// <summary>Gets the saved display name, falling back to the selected folder.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the stored game version or the instance's resolved original version.</summary>
    public required string Version { get; init; }
    /// <summary>Gets the installed original runtime selection; it does not change the save version.</summary>
    public required SeedRuntimeProfile Profile { get; init; }
    /// <summary>Gets the lossless signed seed when the save actually records one.</summary>
    public string? Seed { get; init; }
    /// <summary>Gets the recorded generator preset and default dimension.</summary>
    public SeedWorldSettings Settings { get; init; } = new();
    /// <summary>Gets whether the recorded vanilla generator can be represented exactly by the original preview settings.</summary>
    public bool PreviewSupported { get; init; }
    /// <summary>Gets why original preview is unavailable while saved map inspection remains usable.</summary>
    public string? PreviewUnavailableReason { get; init; }
    /// <summary>Gets the stored shared spawn, independently of a player's saved position.</summary>
    public SeedPosition? Spawn { get; init; }
    /// <summary>Gets dimensions with actual saved data or recorded world-generation settings.</summary>
    public string[] Dimensions { get; init; } = [];
    /// <summary>Gets independently readable saved player records.</summary>
    public SavedWorldPlayer[] Players { get; init; } = [];
    /// <summary>Gets the metadata file's last write time, not an invented global world transaction time.</summary>
    public DateTimeOffset SavedAt { get; init; }
    /// <summary>Gets the metadata and region-file revision owning this read session.</summary>
    public required string Revision { get; init; }
    /// <summary>Gets individual metadata/player failures without hiding other readable data.</summary>
    public string[] Warnings { get; init; } = [];
}

/// <summary>Requests samples from an explicit save, with optional isolated original preview for confirmed unexplored terrain.</summary>
public sealed record WorldMapQuery
{
    /// <summary>Gets whether a one-shot service read may fill confirmed unsaved chunks through an isolated original runtime; default retains saved-only reads.</summary>
    public bool FillUnexplored { get; set; }
    /// <summary>Gets the selected instance and save.</summary>
    [JsonRequired]
    public WorldMapSelection Selection { get; set; } = null!;
    /// <summary>Gets the stored dimension to read.</summary>
    public string Dimension { get; set; } = "overworld";
    /// <summary>Gets the western sample origin in blocks.</summary>
    public int MinX { get; set; }
    /// <summary>Gets the northern sample origin in blocks.</summary>
    public int MinZ { get; set; }
    /// <summary>Gets the positive interval between samples.</summary>
    public int Step { get; set; } = 16;
    /// <summary>Gets samples per side, bounded to 256.</summary>
    public int Side { get; set; } = 64;
    /// <summary>Gets an explicit block slice, or null for the saved surface.</summary>
    public int? Y { get; set; }
    /// <summary>Gets whether missing close surface samples may use actual decorated materials when the original adapter supports the bounded request; quick previews, larger footprints and slices retain base sampling.</summary>
    public bool Decorated { get; set; }
}

/// <summary>Requests a bounded block/inventory snapshot from one save without generating chunks or evaluating loot tables.</summary>
public sealed record WorldRegionQuery
{
    /// <summary>Gets the registered instance and exact saved world.</summary>
    [JsonRequired]
    public WorldMapSelection Selection { get; set; } = null!;
    /// <summary>Gets the dimension owning every coordinate in Bounds.</summary>
    public string Dimension { get; set; } = "overworld";
    /// <summary>Gets inclusive block bounds; horizontal axes and optional volume transport are bounded.</summary>
    [JsonRequired]
    public SeedBounds Bounds { get; set; } = null!;
    /// <summary>Gets whether to include the paletted saved volume for rendering, in addition to counts and inventories.</summary>
    public bool IncludeBlocks { get; set; }
}

/// <summary>Finds recorded structures in an explicit saved-world radius without predicting ungenerated terrain.</summary>
public sealed record WorldStructureQuery
{
    /// <summary>Gets the owning registered instance and exact save.</summary>
    [JsonRequired]
    public WorldMapSelection Selection { get; set; } = null!;
    /// <summary>Gets the saved dimension to search.</summary>
    public string Dimension { get; set; } = "overworld";
    /// <summary>Gets the reference X coordinate.</summary>
    public int CenterX { get; set; }
    /// <summary>Gets the reference Z coordinate.</summary>
    public int CenterZ { get; set; }
    /// <summary>Gets the inclusive horizontal radius, bounded to 8192 blocks.</summary>
    public int Radius { get; set; } = 1024;
    /// <summary>Gets exact recorded IDs to retain; empty selects all recorded structures.</summary>
    public string[] Targets { get; set; } = [];
    /// <summary>Gets the maximum returned records, bounded to 1024.</summary>
    public int Limit { get; set; } = 100;
}

/// <summary>Reports the actual saved scene and original model geometry built through the same Core path as the App.</summary>
/// <param name="Fingerprint">Frozen saved revision owning the inspected scene.</param>
/// <param name="Bounds">Actual inclusive read and mesh bounds.</param>
/// <param name="UnknownBlocks">Positions with no readable saved data, excluded from rendering.</param>
/// <param name="RenderedBlocks">Saved blocks contributing original model faces.</param>
/// <param name="Vertices">Number of triangle vertices in the built mesh.</param>
/// <param name="UnavailableModels">Saved IDs whose installed original models cannot be rendered.</param>
public sealed record WorldSceneInspection(string Fingerprint, SeedBounds Bounds, long UnknownBlocks, int RenderedBlocks, int Vertices, string[] UnavailableModels);

/// <summary>Reports player provenance for an exact selected save; absent or mismatched observations contain no fabricated position.</summary>
/// <param name="Selection">Owning registered instance and selected save.</param>
/// <param name="State">live, saved, disabled, disconnected, other_world, or unavailable.</param>
/// <param name="Player">Verified live or recorded saved position, when available.</param>
/// <param name="SessionId">Matching running session, when present.</param>
/// <param name="WorldEpoch">Observed runtime world incarnation, when live.</param>
public sealed record WorldPlayerObservation(WorldMapSelection Selection, string State, SavedWorldPlayer? Player, string? SessionId = null, string? WorldEpoch = null);

/// <summary>Provides NativeAOT metadata for world explorer requests and observations.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WorldMapSelection))]
[JsonSerializable(typeof(WorldMapSnapshot))]
[JsonSerializable(typeof(WorldMapSnapshot[]))]
[JsonSerializable(typeof(WorldMapQuery))]
[JsonSerializable(typeof(WorldRegionQuery))]
[JsonSerializable(typeof(WorldStructureQuery))]
[JsonSerializable(typeof(WorldSceneInspection))]
[JsonSerializable(typeof(WorldContainerPrediction))]
[JsonSerializable(typeof(WorldContainerQuery))]
[JsonSerializable(typeof(SeedRegionResult))]
[JsonSerializable(typeof(SeedStructureResult))]
[JsonSerializable(typeof(WorldPlayerObservation))]
[JsonSerializable(typeof(SeedMapTile))]
public partial class WorldMapJsonContext : JsonSerializerContext;