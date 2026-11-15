// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Skins;

/// <summary>Identifies a face; front is the direction the character looks.</summary>
[JsonConverter(typeof(SkinEnumConverter<SkinFace>))]
public enum SkinFace { Front, Back, Right, Left, Top, Bottom }