// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Worlds;
using Rodel.MinecraftLauncher.Core.Worlds.Maps;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using Sprout;
using Sprout.Graphics;
using Sprout.Reactive;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Keeps window-only choices separate from the Core-owned runtime, search checkpoints and bookmarks.</summary>
internal sealed class SeedExplorerState
{
    private readonly Signal<SeedExplorerSnapshot> _snapshot = new(new());
    private readonly Signal<SeedSceneCamera> _camera = new(new());
    internal SeedSceneCamera Camera { get => _camera.Value; set => _camera.Value = value; }
    internal SeedExplorerSnapshot Current { get => _snapshot.Value; set => _snapshot.Value = value; }
    internal SeedExplorerController Controller { get; set; } = null!;
}

internal sealed record SeedExplorerSnapshot
{
    internal bool WorldSource { get; init; }
    internal bool SavedMap { get; init; } = true;
    internal bool WorldLoaded { get; init; }
    internal bool WorldNeedsRefresh { get; init; }
    internal bool WorldReading { get; init; }
    internal bool WorldPreviewPreparing { get; init; }
    internal bool WorldPreviewUnavailable { get; init; }
    internal bool Panning { get; init; }
    internal SeedPosition[] PreviewPositions { get; init; } = [];
    internal bool PickedPointPreview { get; init; }
    internal bool ScenePreview { get; init; }
    internal bool TrackPlayer { get; init; } = true;
    internal bool FollowPlayer { get; init; }
    internal InstanceSummary[] Instances { get; init; } = [];
    internal WorldSummary[] Worlds { get; init; } = [];
    internal string? WorldInstanceId { get; init; }
    internal string? WorldFolder { get; init; }
    internal WorldMapSnapshot? WorldSnapshot { get; init; }
    internal WorldPlayerObservation? Player { get; init; }
    internal string? PlayerId { get; init; }
    internal bool HasMap => WorldSource && !SearchMode ? WorldLoaded : Seed.Length > 0;
    internal bool UseSavedMap => WorldSource && !SearchMode && SavedMap && WorldLoaded;
    internal SeedVersionOption[] Versions { get; init; } = [];
    internal int VersionIndex { get; init; } = -1;
    internal SeedCatalog? Catalog { get; init; }
    internal bool CatalogLoading { get; init; }
    internal SeedRuntimeProfile? Profile { get; init; }
    internal string Fingerprint { get; init; } = string.Empty;
    internal string SeedInput { get; init; } = string.Empty;
    internal string Seed { get; init; } = string.Empty;
    internal SeedPosition? Spawn { get; init; }
    internal SeedWorldSettings World { get; init; } = new();
    internal int? SliceY { get; init; }
    internal bool Terrain { get; init; } = true;
    internal bool SearchMode { get; init; }
    internal bool Opening { get; init; } = true;
    internal bool Preparing { get; init; }
    internal bool MapBusy { get; init; }
    internal bool LoadingMap { get; init; }
    internal bool Searching { get; init; }
    internal bool Exporting { get; init; }
    internal bool BookmarkBusy { get; init; }
    internal int SearchTab { get; init; }
    internal SeedPosition? SelectedPlace { get; init; }
    internal SeedPosition? FocusedPlace { get; init; }
    internal SeedPosition? PickedPoint { get; init; }
    internal string Error { get; init; } = string.Empty;
    internal string Phase { get; init; } = "initializing";
    internal double CenterX { get; init; }
    internal double CenterZ { get; init; }
    internal double BlocksPerPixel { get; init; } = 8;
    internal float MapWidth { get; init; } = 800;
    internal float MapHeight { get; init; } = 480;
    internal float Width { get; init; } = 1280;
    internal float Height { get; init; } = 800;
    internal int SampleStep { get; init; } = 32;
    internal int RemainingTiles { get; init; }
    internal int TotalTiles { get; init; }
    internal double CalculationMilliseconds { get; init; }
    internal SeedRenderedTile[] Tiles { get; init; } = [];
    internal SeedMapBackdrop? MapBackdrop { get; init; }
    internal SeedPosition[] MapPositions { get; init; } = [];
    internal bool MarkerBusy { get; init; }
    internal bool MarkerLimited { get; init; }
    internal SeedBounds? MarkerScope { get; init; }
    internal int MarkerRegionCount { get; init; }
    internal int MarkerRegionsChecked { get; init; }
    internal int RemainingMarkerRegions { get; init; }
    internal SeedCondition[] Conditions { get; init; } = [];
    internal int SearchSeedBudget { get; init; } = 10_000;
    internal int SearchResultBudget { get; init; } = 5;
    internal int SearchSeconds { get; init; } = 60;
    internal int BiomeStep { get; init; } = 32;
    internal SeedSearchState? Search { get; init; }
    internal SeedSearchState[] Searches { get; init; } = [];
    internal SeedResultPreview[] Previews { get; init; } = [];
    internal SeedBookmark[] Bookmarks { get; init; } = [];
    internal int BookmarkSegment { get; init; }
    internal string[]? StructureLayers { get; init; }
    internal bool ShowBiomeWitnesses { get; init; } = true;
    internal bool ShowOrigin { get; init; } = true;
    internal SeedPosition? PendingPlace { get; init; }
    internal int PendingTeleportY { get; init; } = 80;
    internal string[] StructureTargets { get; init; } = [];
    internal int StructureRadius { get; init; } = 2048;
    internal bool StructureSearching { get; init; }
    internal string StructurePhase { get; init; } = "ready";
    internal string StructureError { get; init; } = string.Empty;
    internal SeedStructureResult? StructureResult { get; init; }
    internal SeedBounds? RegionBounds { get; init; }
    internal bool RegionEditBounds { get; init; }
    internal bool SelectingRegion { get; init; }
    internal bool RegionBusy { get; init; }
    internal SeedRegionProgress? RegionProgress { get; init; }
    internal string RegionError { get; init; } = string.Empty;
    internal SeedRegionResult? RegionData { get; init; }
    internal int RegionTab { get; init; }
    internal bool RegionOnlyOres { get; init; } = true;
    internal string RegionFilter { get; init; } = string.Empty;
    internal SeedContainer? RegionContainer { get; init; }
    internal SeedLootItem? RegionItem { get; init; }
    internal bool SceneOpen { get; init; }
    internal bool SceneBusy { get; init; }
    internal bool SceneMeshing { get; init; }
    internal SeedRegionProgress? SceneProgress { get; init; }
    internal string SceneError { get; init; } = string.Empty;
    internal SeedPosition? ScenePosition { get; init; }
    internal SeedRegionResult? SceneData { get; init; }
    internal SeedBlockScene? Scene { get; init; }
    internal SeedSceneMesh? SceneMesh { get; init; }
    internal int SceneMinY { get; init; }
    internal int SceneMaxY { get; init; }
    internal int SceneTab { get; init; }
    internal int SceneRenderVersion { get; init; }
    internal bool SceneCropped { get; init; }
    internal string SceneFilter { get; init; } = string.Empty;
    internal SeedContainer? SceneContainer { get; init; }
    internal SeedLootItem? SceneItem { get; init; }
    internal IReadOnlyDictionary<string, ImageSource> ItemImages { get; init; } = new Dictionary<string, ImageSource>();
}

internal sealed record SeedRenderedTile(int Key, string RequestKey, SeedMapTile Tile, ImageSource TerrainImage, ImageSource BiomeImage);

/// <summary>Retains a previous saved revision strictly for painting while its replacement is loading.</summary>
/// <param name="Selection">The exact saved world whose prior pixels may still be shown.</param>
/// <param name="Tiles">Bounded prior raster images, excluded from current snapshot analysis and export.</param>
internal sealed record SeedMapBackdrop(WorldMapSelection Selection, SeedRenderedTile[] Tiles);
internal sealed record SeedResultPreview(string Seed, ImageSource Image);