// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Java;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(JavaProbeCacheEntry))]
[JsonSerializable(typeof(JavaSelectionReport))]
internal sealed partial class JavaJsonContext : JsonSerializerContext;