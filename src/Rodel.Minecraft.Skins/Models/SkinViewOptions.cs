// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Defines the shared orthographic preview configuration.</summary>
/// <param name="Yaw">Camera yaw in degrees.</param>
/// <param name="Pitch">Camera pitch from -89 through 89 degrees.</param>
/// <param name="Size">Square PNG size from 64 through 512 pixels.</param>
/// <param name="BaseVisible">Whether to include the base model surface.</param>
/// <param name="OuterVisible">Whether to include the inflated outer surface.</param>
/// <param name="Parts">Visible body parts; null includes all and an empty array includes none.</param>
public sealed record SkinViewOptions(float Yaw = 25, float Pitch = 10, int Size = 256,
    bool BaseVisible = true, bool OuterVisible = true, SkinPart[]? Parts = null);