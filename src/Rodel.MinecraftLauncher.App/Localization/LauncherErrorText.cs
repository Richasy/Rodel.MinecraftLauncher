// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Localization;

/// <summary>Localizes observed instance activity while retaining unknown failures as their original explanations.</summary>
internal static class LauncherErrorText
{
    internal static string Describe(Exception error) => error is LauncherOperationException operation
        ? Describe(operation.Code, operation.Details, operation.Message) : error.Message;

    internal static string Describe(JsonNode? failure, string fallback) =>
        Describe(failure?["error"]?["code"]?.ToString(), failure?["data"], fallback);

    private static string Describe(string? code, JsonNode? details, string fallback) => code switch
    {
        "CONTENT_INSTANCE_BUSY" => Resources.InstanceTaskBusy,
        "INSTANCE_BUSY" => (details as JsonObject)?["reason"]?.ToString() switch
        {
            "game" => Resources.InstanceGameBusy,
            "content-download" => Resources.InstanceContentDownloadBusy,
            "task" => Resources.InstanceTaskBusy,
            "unknown-task" => Resources.InstanceTaskActivityUnknown,
            _ => Resources.InstanceBusyConflict,
        },
        _ => fallback,
    };
}