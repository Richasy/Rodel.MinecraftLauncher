// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Skins;

/// <summary>Chooses a linear ramp or radial color transition.</summary>
[JsonConverter(typeof(SkinEnumConverter<SkinGradientKind>))]
public enum SkinGradientKind { Linear, Radial }