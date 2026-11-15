// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Skins;

/// <summary>Chooses the opaque body texture or transparent outer clothing texture.</summary>
[JsonConverter(typeof(SkinEnumConverter<SkinLayer>))]
public enum SkinLayer { Base, Outer }