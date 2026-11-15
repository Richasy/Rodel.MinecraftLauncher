// Copyright (c) Richasy. All rights reserved.
using Rodel.MinecraftLauncher.Core.Application;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
namespace Rodel.MinecraftLauncher.Cli;

internal static class CommandResults
{
    internal static ToolResult FromCommand(string? operation, JsonNode? data, LauncherCommand? command)
    {
        var result = ToolResults.Success(operation ?? string.Empty, data);
        var status = command?.ResultStatus?.Invoke(data) ?? LauncherResultStatus.Succeeded;
        return status == LauncherResultStatus.Succeeded ? result : result with
        {
            Status = status,
            Error = (data as JsonObject)?["error"]?.Deserialize(ToolJsonContext.Default.LauncherOperationError)
        };
    }
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CommandJobRequest))]
internal partial class CommandJobJsonContext : JsonSerializerContext;