// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>Describes a resolved launch profile while retaining third-party version metadata.</summary>
/// <param name="VersionId">Selected launch profile identifier.</param>
/// <param name="MinecraftVersion">Base game version, or the selected identifier if unavailable.</param>
/// <param name="Loader">Recognized loader kind, if any.</param>
/// <param name="LoaderVersion">Recognized loader version, if any.</param>
/// <param name="MainClass">Main-class metadata, retained without imposing new schema validation.</param>
/// <param name="JavaVersion">Open third-party Java requirement metadata.</param>
/// <param name="Source">Origin of this description.</param>
/// <param name="Metadata">Resolved third-party version metadata.</param>
public sealed record RuntimeDescription(string VersionId, string MinecraftVersion, string? Loader, string? LoaderVersion, JsonNode? MainClass, JsonNode? JavaVersion, string Source, JsonObject Metadata);

/// <summary>Source-generated serialization metadata for launch read models.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RuntimeDescription))]
[JsonSerializable(typeof(ILaunchResult))]
[JsonSerializable(typeof(LaunchPlan))]
[JsonSerializable(typeof(LoggingCompatibilityPlan))]
[JsonSerializable(typeof(SessionStopResult))]
[JsonSerializable(typeof(SessionStatus))]
[JsonSerializable(typeof(IReadOnlyList<SessionStatus>))]
[JsonSerializable(typeof(LogPreview))]
[JsonSerializable(typeof(StartupReport))]
[JsonSerializable(typeof(LogEventRead))]
[JsonSerializable(typeof(TextLogEvent))]
[JsonSerializable(typeof(LogCaptureStatus))]
[JsonSerializable(typeof(MemoryRecommendation))]
[JsonSerializable(typeof(MemorySelectionReport))]
public partial class LaunchJsonContext : JsonSerializerContext;