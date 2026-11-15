// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Stores editable window preferences without restoring generated output or initiating work.</summary>
internal sealed record SeedWorkspaceDraft
{
    /// <summary>Gets the last selected installed profile, resolved against the current installed version list.</summary>
    public SeedRuntimeProfile? Profile { get; init; }
    /// <summary>Gets the exact input text, including seeds outside signed-long range and unsubmitted edits.</summary>
    public string SeedInput { get; init; } = string.Empty;
    /// <summary>Gets the last dimension and world preset.</summary>
    public SeedWorldSettings World { get; init; } = new();
    /// <summary>Gets the optional map slice height.</summary>
    public int? SliceY { get; init; }
    /// <summary>Gets whether the map uses surface colors.</summary>
    public bool Terrain { get; init; } = true;
    /// <summary>Gets the selected editor mode without resuming its operation.</summary>
    public bool SearchMode { get; init; }
    /// <summary>Gets whether the map opens on an explicitly selected instance save.</summary>
    public bool WorldSource { get; init; }
    /// <summary>Gets the last selected registered instance without starting a game or reading tiles.</summary>
    public string? WorldInstanceId { get; init; }
    /// <summary>Gets the last selected save folder inside the instance.</summary>
    public string? WorldFolder { get; init; }
    /// <summary>Gets whether saved chunks are preferred over original generation.</summary>
    public bool SavedMap { get; set; } = true;
    /// <summary>Gets whether player observations are requested while this window is open.</summary>
    public bool TrackPlayer { get; set; } = true;
    /// <summary>Gets editable condition lists keyed by installed version and world settings.</summary>
    public Dictionary<string, SeedCondition[]> Conditions { get; init; } = [];
    /// <summary>Gets the candidate limit for a new search round.</summary>
    public int SeedBudget { get; init; } = 10_000;
    /// <summary>Gets the cumulative hit limit.</summary>
    public int ResultBudget { get; init; } = 5;
    /// <summary>Gets the time allowance for a new round.</summary>
    public int Seconds { get; init; } = 60;
    /// <summary>Gets the biome sampling interval.</summary>
    public int BiomeStep { get; init; } = 32;

    internal static SeedWorkspaceDraft Read(string json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return new();
            var draft = JsonSerializer.Deserialize(json, SeedDraftJsonContext.Default.SeedWorkspaceDraft);
            if (draft is null || draft.SeedInput is null || draft.World is null || draft.Conditions is null) return new();
            return draft with
            {
                Conditions = draft.Conditions.Where(pair => pair.Value is { Length: <= 12 } && pair.Value.All(condition => condition is not null
                    && !string.IsNullOrEmpty(condition.Id) && condition.Kind is "structure" or "biome" && condition.Targets is { Length: > 0 }
                    && condition.Targets.All(target => !string.IsNullOrEmpty(target)))).ToDictionary(),
                SeedBudget = Math.Clamp(draft.SeedBudget, 1, 1_000_000),
                ResultBudget = Math.Clamp(draft.ResultBudget, 1, 100),
                Seconds = Math.Clamp(draft.Seconds, 1, 3600),
                BiomeStep = Math.Clamp(draft.BiomeStep, 4, 256),
            };
        }
        catch (JsonException) { return new(); }
    }
    internal string Serialize() => JsonSerializer.Serialize(this, SeedDraftJsonContext.Default.SeedWorkspaceDraft);
    internal static string Key(SeedRuntimeProfile profile, SeedWorldSettings world) => string.Join('|',
        profile.MinecraftDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant(), profile.VersionId, world.Dimension, world.Preset, world.Biome);
}

/// <summary>Supplies NativeAOT metadata for App-only seed editor preferences.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SeedWorkspaceDraft))]
internal sealed partial class SeedDraftJsonContext : JsonSerializerContext;