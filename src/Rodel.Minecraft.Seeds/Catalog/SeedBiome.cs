// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Describes one registered native biome; an empty dimension list identifies special-preset-only entries.</summary>
/// <param name="Id">Original registered biome path.</param>
/// <param name="Dimensions">Dimensions containing this biome in their default original sources.</param>
public sealed record SeedBiome(string Id, string[] Dimensions);