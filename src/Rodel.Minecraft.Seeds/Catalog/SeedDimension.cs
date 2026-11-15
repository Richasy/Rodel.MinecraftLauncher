// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Describes a native dimension and its legal biome slice range.</summary>
/// <param name="Id">Original dimension path.</param>
/// <param name="MinY">Inclusive minimum biome slice height.</param>
/// <param name="Height">Original dimension height in blocks.</param>
/// <param name="Biomes">Registered biomes used by its default original source.</param>
public sealed record SeedDimension(string Id, int MinY, int Height, string[] Biomes);