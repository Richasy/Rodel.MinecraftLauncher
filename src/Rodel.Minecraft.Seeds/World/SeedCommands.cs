// Copyright (c) Richasy. All rights reserved.

using System.Globalization;

namespace Rodel.Minecraft.Seeds;

/// <summary>Formats copy-only original Minecraft coordinates and dimension-aware teleport commands.</summary>
public static class SeedCommands
{
    /// <summary>Builds a command for the selected dimension and exact player-chosen coordinates; this method never executes a game command.</summary>
    public static string Teleport(SeedWorldSettings world, int x, int y, int z)
    {
        SeedValidation.World(world);
        if (Math.Abs((long)x) > 30_000_000 || Math.Abs((long)z) > 30_000_000 || y is < -20_000_000 or > 20_000_000)
            throw new ArgumentOutOfRangeException(nameof(x), "Teleport coordinates exceed the original command bounds.");
        return string.Create(CultureInfo.InvariantCulture, $"/execute in minecraft:{world.Dimension} run tp @s {x} {y} {z}");
    }
}