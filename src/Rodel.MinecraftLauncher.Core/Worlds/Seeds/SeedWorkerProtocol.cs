// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Worlds.Seeds;

internal sealed record SeedWorkerRequest(string? Id, string Operation, SeedMapQuery? Map = null, SeedSearchQuery? Search = null, string? CancelId = null, string? Seed = null, SeedStructureQuery? Structures = null, SeedRegionQuery? Region = null, SeedSpawnQuery? Spawn = null, SeedFrozenTintQuery? Tints = null, SeedLootQuery? Loot = null);
internal sealed record SeedWorkerEnvelope(string? Id, string Kind, JsonElement Data, string? Error);
internal sealed record SeedWorkerReady(int Protocol, string Version);
internal sealed record SeedValue(string Seed);
internal sealed record SeedGeneratedWorld(string Directory, SeedRegionResult Region, SeedGeneratedInventory[] Inventories);
internal sealed record SeedGeneratedInventory(int X, int Y, int Z, int Slots, string Id);
/// <summary>Transports bounded original top states and native colors before installed-resource rasterization.</summary>
/// <param name="Tile">Original sample context, solid heights and biome indices.</param>
/// <param name="Palette">Exact original states including their properties.</param>
/// <param name="Blocks">Row-major top-state palette indices.</param>
/// <param name="Tints">Row-major native tint-index colors for each top state.</param>
/// <param name="SurfaceHeights">Actual visible canopy or fluid heights.</param>
/// <param name="WaterDepths">Actual column fluid depths.</param>
/// <param name="WaterColors">Native biome water RGB colors.</param>
/// <param name="ReliefHeights">Visible heights with one neighboring sample of padding.</param>
internal sealed record SeedSurfaceMap(SeedMapTile Tile, SeedBlockState[] Palette, int[] Blocks, int[][] Tints, int[] SurfaceHeights, int[] WaterDepths, uint[] WaterColors, int[] ReliefHeights);