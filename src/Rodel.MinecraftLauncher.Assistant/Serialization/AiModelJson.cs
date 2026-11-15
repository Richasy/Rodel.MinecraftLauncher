// Copyright (c) Richasy. All rights reserved.

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>Lossless compact JSON for model input, independent of human-readable persisted records.</summary>
internal static class AiModelJson
{
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    /// <summary>Serializes JSON without indentation or unnecessary Unicode escaping; strings and schema semantics remain intact.</summary>
    public static string Text(JsonNode node) => node.ToJsonString(Options);
}