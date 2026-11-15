// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;

namespace Rodel.MinecraftLauncher.Core.Worlds.Seeds;

/// <summary>Transports a bounded frozen block/biome subset to original tint providers without opening a world.</summary>
internal sealed record SeedFrozenTintQuery(SeedBounds Bounds, SeedBlockState[] Palette, byte[] Blocks, string[] BiomePalette, byte[] Biomes);

/// <summary>Returns original tint-index colors and their voxel ownership independently of block-state palettes.</summary>
internal sealed record SeedRegionTints(SeedRegionTint[] TintPalette, byte[] TintIndices);