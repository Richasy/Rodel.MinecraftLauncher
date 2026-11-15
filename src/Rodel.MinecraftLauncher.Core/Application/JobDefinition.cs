// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>A durable application task descriptor. Parameters are owned by its executor, never parsed as command tokens by Core.</summary>
public sealed record JobDefinition(string Operation, JsonObject Parameters, bool CanResume = false);