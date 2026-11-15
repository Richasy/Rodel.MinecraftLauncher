// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Skins;

/// <summary>Chooses the brush footprint in model pixel units.</summary>
[JsonConverter(typeof(SkinEnumConverter<SkinBrushShape>))]
public enum SkinBrushShape { Square, Circle }