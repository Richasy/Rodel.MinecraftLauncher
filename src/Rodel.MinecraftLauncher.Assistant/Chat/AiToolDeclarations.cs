// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant;

internal static class AiToolDeclarations
{
    /// <summary>Creates a non-executable MEAI declaration from an external or Core-generated input schema.</summary>
    public static AIFunctionDeclaration Create(string name, string description, JsonObject schema)
    {
        using var document = JsonDocument.Parse(schema.ToJsonString());
        return AIFunctionFactory.CreateDeclaration(name, description, document.RootElement.Clone());
    }

    /// <summary>Copies the declared schema for Core's explicit scope augmentation or external protocol mapping.</summary>
    public static JsonObject Schema(AIFunctionDeclaration tool)
        => JsonNode.Parse(tool.JsonSchema.GetRawText())!.AsObject();
}