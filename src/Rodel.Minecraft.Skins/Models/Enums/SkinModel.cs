// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Skins;

/// <summary>Chooses the standard/slim character geometry or an independent cape.</summary>
[JsonConverter(typeof(SkinEnumConverter<SkinModel>))]
public enum SkinModel { Classic, Slim, Cape }