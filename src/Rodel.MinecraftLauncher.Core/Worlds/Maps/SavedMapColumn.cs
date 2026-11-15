// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Retains a saved column's actual state and independently measured top, bed and fluid depth after section compaction.</summary>
/// <param name="State">Saved state at the solid top or fluid bed.</param>
/// <param name="Biome">Saved biome at the visible surface.</param>
/// <param name="Y">Solid top or bed height.</param>
/// <param name="Fluid">Visible fluid category, or zero for land.</param>
/// <param name="SurfaceY">Visible canopy or fluid top height.</param>
/// <param name="Depth">Measured fluid depth, or zero for land.</param>
internal sealed record SavedMapColumn(SeedBlockState State, string Biome, int Y, int Fluid, int SurfaceY, int Depth);