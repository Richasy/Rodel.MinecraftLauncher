// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Skins;

/// <summary>Identifies a body part from the character's perspective.</summary>
[JsonConverter(typeof(SkinEnumConverter<SkinPart>))]
public enum SkinPart { Head, Body, RightArm, LeftArm, RightLeg, LeftLeg, Cape }