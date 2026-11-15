// Copyright (c) Richasy. All rights reserved.
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
namespace Rodel.MinecraftLauncher.Cli;
/// <summary>Wraps one discovered Core contract without publishing its full schema on initial connection.</summary>
/// <param name="Name">An exact capability name returned by capabilities.</param>
/// <param name="Arguments">Parameters conforming to that capability's separately requested schema.</param>
internal sealed record DiscoveredToolCall(string Name, JsonObject Arguments);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(DiscoveredToolCall))]
internal partial class DiscoveryToolJsonContext : JsonSerializerContext;
/// <summary>Provides a fixed small MCP surface for clients without native lazy tool loading.</summary>
internal static class DiscoveryToolsCatalog
{
    internal const string Instructions = "Use capabilities to inspect only relevant names and request one toolName schema. Invoke that contract through tool_call with name and arguments. Do not enumerate all schemas or read state unless the user's task needs it. execute is for CLI-only operations; inspect the exact topic help first. The external client owns authorization; discovery grants none. ";
    internal static IReadOnlyList<LauncherToolDefinition> Tools { get; } = Build();
    private static IReadOnlyList<LauncherToolDefinition> Build()
    {
        var contract = new ToolInputContract<DiscoveredToolCall>(DiscoveryToolJsonContext.Default.DiscoveredToolCall);
        return [.. LauncherToolsCatalog.Tools.Where(tool => tool.Name is "capabilities" or "execute" or "result_read" or "result_image_read"),
            new("tool_call", "Invoke one exact Core capability after reading its schema with capabilities(toolName). The external client must authorize the operation. Unknown capabilities fail without fallback.", contract.Schema, false, true, null)];
    }
    internal static IReadOnlyList<LauncherToolDefinition> Select(string profile, string? skinScope) => profile switch
    {
        "discovery" => Tools,
        "skins" => skinScope is null ? SkinToolsCatalog.Tools : SkinToolsCatalog.ScopedTools,
        _ => LauncherToolsCatalog.Tools
    };
}