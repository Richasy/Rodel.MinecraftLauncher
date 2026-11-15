// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Skins;

/// <summary>Chooses a rectangle, ellipse, polygon or explicit pixel mask.</summary>
[JsonConverter(typeof(SkinEnumConverter<SkinSelectionShape>))]
public enum SkinSelectionShape { Rectangle, Ellipse, Polygon, Mask }