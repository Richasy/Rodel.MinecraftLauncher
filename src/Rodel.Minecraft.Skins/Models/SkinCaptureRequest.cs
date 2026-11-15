// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Requests clipboard pixels from a selected model face without mutating the document.</summary>
/// <param name="Surface">The captured face.</param>
/// <param name="Selection">The captured footprint, or null for the complete face.</param>
/// <param name="PaintLayerId">The source paint layer; null samples the visible composite.</param>
public sealed record SkinCaptureRequest(SkinSurface Surface, SkinSelection? Selection = null, string? PaintLayerId = null);