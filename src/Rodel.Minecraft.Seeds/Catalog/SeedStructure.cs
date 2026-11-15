// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Describes one registered structure and its native allowed-biome/dimension membership.</summary>
/// <param name="Id">Original registered structure path, including any original variant.</param>
/// <param name="Dimensions">Original dimensions where its biome set is applicable.</param>
/// <param name="Biomes">Biomes in the original allowed-biome holder set.</param>
public sealed record SeedStructure(string Id, string[] Dimensions, string[] Biomes);