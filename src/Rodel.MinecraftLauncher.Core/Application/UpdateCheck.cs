// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>Result of querying the optional launcher release service.</summary>
/// <param name="CurrentVersion">Current launcher version.</param>
/// <param name="Status">Whether an accessible release was found.</param>
[JsonDerivedType(typeof(AvailableUpdate))]
[JsonDerivedType(typeof(UnavailableUpdate))]
public abstract record UpdateCheck(string CurrentVersion, string Status);

/// <summary>An accessible GitHub release with original provider metadata values.</summary>
/// <param name="LatestVersion">Original provider tag.</param>
/// <param name="Url">Original provider release URL.</param>
/// <param name="PublishedAt">Original publication timestamp.</param>
public sealed record AvailableUpdate(JsonNode? LatestVersion, JsonNode? Url, JsonNode? PublishedAt) : UpdateCheck("0.1.0-dev", "release_available");

/// <summary>No accessible release; optional authentication remains an explicit external operation.</summary>
/// <param name="Message">Explanation of release availability and optional credentials.</param>
public sealed record UnavailableUpdate(string Message) : UpdateCheck("0.1.0-dev", "release_unavailable");