// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>Optional game-window dimensions used by launch argument expansion.</summary>
/// <param name="Width">Explicit width, or null to use the existing default.</param>
/// <param name="Height">Explicit height, or null to use the existing default.</param>
public sealed record LaunchResolution(int? Width = null, int? Height = null);