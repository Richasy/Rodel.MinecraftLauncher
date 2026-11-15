// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Describes layer settings without returning the complete pixel arrays.</summary>
/// <param name="Id">The stable layer identifier.</param>
/// <param name="Name">The user-facing layer label.</param>
/// <param name="Visible">Whether the layer contributes to the composite.</param>
/// <param name="Opacity">The compositing opacity.</param>
/// <param name="Locked">Whether pixel changes are rejected.</param>
public sealed record SkinLayerInfo(string Id, string Name, bool Visible, double Opacity, bool Locked);