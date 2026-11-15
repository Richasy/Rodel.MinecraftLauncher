// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Skins;

/// <summary>Chooses geometric selection or merged/specific-layer color selection.</summary>
[JsonConverter(typeof(SkinEnumConverter<SkinSelectionKind>))]
public enum SkinSelectionKind { Rectangle, Ellipse, Polygon, Matching, Connected }