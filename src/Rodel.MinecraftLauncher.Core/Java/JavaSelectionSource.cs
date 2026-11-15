// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Java;

/// <summary>Identifies the basis of a Java selection without using protocol strings in business code.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<JavaSelectionSource>))]
public enum JavaSelectionSource
{
    /// <summary>The caller explicitly selected the Java executable.</summary>
    [JsonStringEnumMemberName("explicit")]
    Explicit,
    /// <summary>Version metadata supplied Java runtime information.</summary>
    [JsonStringEnumMemberName("metadata")]
    Metadata,
    /// <summary>A known Minecraft version supplied the fallback preference.</summary>
    [JsonStringEnumMemberName("known-version-preference")]
    KnownVersionPreference,
    /// <summary>No version preference was known; available installations were considered.</summary>
    [JsonStringEnumMemberName("available-fallback")]
    AvailableFallback,
    /// <summary>The launch workflow prepared a runtime automatically before rebuilding its plan.</summary>
    [JsonStringEnumMemberName("automatic-runtime")]
    AutomaticRuntime
}