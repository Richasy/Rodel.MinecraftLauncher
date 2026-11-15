// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Skins;

/// <summary>Provides NativeAOT-compatible JSON metadata for skin documents, drawing actions and atlas descriptions.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(SkinHistoryEntry[]))]
[JsonSerializable(typeof(SkinBrushMark[]))]
[JsonSerializable(typeof(SkinDocument))]
[JsonSerializable(typeof(SkinEdit[]))]
[JsonSerializable(typeof(SkinEdit))]
[JsonSerializable(typeof(SkinFaceInfo[]))]
[JsonSerializable(typeof(SkinLayerInfo[]))]
[JsonSerializable(typeof(SkinSelection))]
[JsonSerializable(typeof(SkinSelectionRequest))]
[JsonSerializable(typeof(SkinPickRequest))]
[JsonSerializable(typeof(SkinHit))]
[JsonSerializable(typeof(SkinViewOptions))]
[JsonSerializable(typeof(SkinCaptureRequest))]
[JsonSerializable(typeof(SkinPixelBlock))]
[JsonSerializable(typeof(SkinAtlasBlock))]
[JsonSerializable(typeof(SkinAtlasTransfer))]
public sealed partial class SkinJsonContext : JsonSerializerContext;