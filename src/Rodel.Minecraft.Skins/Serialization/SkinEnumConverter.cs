// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Skins;

/// <summary>Serializes skin enums as named strings only, rejecting numeric values outside the document format.</summary>
/// <typeparam name="T">The specific skin contract enumeration.</typeparam>
public sealed class SkinEnumConverter<T> : JsonStringEnumConverter<T> where T : struct, Enum
{
    /// <summary>Creates a source-generation-compatible converter that rejects numeric enum inputs.</summary>
    public SkinEnumConverter() : base(namingPolicy: null, allowIntegerValues: false) { }
}