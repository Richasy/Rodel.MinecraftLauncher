// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Freezes one rectangular viewport and its completed native tile snapshots for faithful PNG export.</summary>
/// <param name="MinX">Western world-block edge of the exported viewport.</param>
/// <param name="MinZ">Northern world-block edge of the exported viewport.</param>
/// <param name="BlocksPerPixel">World-block span represented by each nearest-sampled output pixel.</param>
/// <param name="Width">Output width, limited to 4096 pixels.</param>
/// <param name="Height">Output height, limited to 4096 pixels.</param>
/// <param name="Tiles">Completed same-context native tiles; finer tiles take precedence where they overlap.</param>
public sealed record SeedMapView(double MinX, double MinZ, double BlocksPerPixel, int Width, int Height, SeedMapTile[] Tiles);