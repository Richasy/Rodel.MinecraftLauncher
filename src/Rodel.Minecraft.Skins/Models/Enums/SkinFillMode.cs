// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Skins;

/// <summary>Chooses the bucket's face-local or semantic model extent.</summary>
[JsonConverter(typeof(SkinEnumConverter<SkinFillMode>))]
public enum SkinFillMode { Connected, Matching, Face, Part, Layer }