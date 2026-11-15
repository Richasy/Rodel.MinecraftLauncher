// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Skins;

/// <summary>Chooses symmetry across the character or within the addressed body part.</summary>
[JsonConverter(typeof(SkinEnumConverter<SkinMirrorMode>))]
public enum SkinMirrorMode { None, Body, Part }