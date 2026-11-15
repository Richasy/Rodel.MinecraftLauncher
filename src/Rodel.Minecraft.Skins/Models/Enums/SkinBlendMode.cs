// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Skins;

/// <summary>Chooses source-over painting or exact pixel replacement with coverage interpolation.</summary>
[JsonConverter(typeof(SkinEnumConverter<SkinBlendMode>))]
public enum SkinBlendMode { Normal, Replace }