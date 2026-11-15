// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Skins;

/// <summary>Names the supported basic paint-layer transactions.</summary>
[JsonConverter(typeof(SkinEnumConverter<SkinLayerOperation>))]
public enum SkinLayerOperation { Add, Set, Remove, Reorder, Duplicate, MergeDown }