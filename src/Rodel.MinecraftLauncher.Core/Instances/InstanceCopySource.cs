// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Instances;

/// <summary>The resolved source for a copy; only extensible settings and original launcher metadata remain open.</summary>
internal sealed record InstanceCopySource(string GameDirectory, string MinecraftDirectory, string? VersionId,
    JsonObject Settings, JsonNode? SourceSettings);