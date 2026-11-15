// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.CommandLine;
using System.Text.Json.Nodes;
using C = Rodel.MinecraftLauncher.Cli.LauncherCommands;

using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class ContextCommandHandler(Func<ParseResult, CancellationToken, Task<JsonNode?>> executeCommand)
{
    internal async Task<JsonObject> ExecuteAsync(ParseResult args, CancellationToken ct)
    {
        var limit = (args.GetValue(CliOptions.Limit) ?? 10);
        if (limit is < 1 or > 100)
            throw new ArgumentException(S.ErrorContextLimit);
        var selected = args.GetValue(CliOptions.Instance);
        var result = ProtocolJson.Object(new ToolContextHeader(DateTimeOffset.UtcNow.ToString("O"), selected, false), CliJsonContext.Default.ToolContextHeader);
        async Task Section(string name, Func<ParseResult> command, Func<JsonNode?, JsonNode?>? transform = null)
        {
            try
            {
                var value = await executeCommand(command(), ct).ConfigureAwait(false);
                result[name] = transform is null ? value : transform(value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result[name] = LauncherOperationException.ToResult(ex);
            }
        }

        if (selected is null)
        {
            await Section("instances", () => C.FromCommand(C.Instances.List), node => ToolResults.Page(node, new JsonObject { ["limit"] = limit }, "instance_list")).ConfigureAwait(false);
        }
        else
        {
            await Section("instance", () => C.FromCommand(C.Instances.Show, [selected])).ConfigureAwait(false);
            await Section("settings", () => new CliInvocation(C.Configuration.Get).Option(CliOptions.Instance, selected).Parse()).ConfigureAwait(false);
            await Section("contentCompatibility", () => C.FromCommand(C.Instances.ContentContext, [selected])).ConfigureAwait(false);
            await Section("mods", () => C.FromCommand(C.Content.List, [selected]), node => ToolResults.Page(node, new JsonObject { ["limit"] = limit, ["instanceId"] = selected }, "content_list")).ConfigureAwait(false);
        }

        var game = result["instance"]?["gameDirectory"]?.GetValue<string>();
        JsonNode? Activities(JsonNode? node, string tool)
        {
            if (node is not JsonArray array)
                return node;
            if (selected is not null)
                array = new JsonArray([.. array.Where(item => item?["activityUnknown"]?.GetValue<bool>() == true || item?["status"]?.GetValue<string>() == "unreadable" || (game is not null && string.Equals(item?["gameDirectory"]?.GetValue<string>(), game, StringComparison.OrdinalIgnoreCase)) || item?["request"]?["parameters"]?["instanceId"]?.ToString() == selected).Select(item => item?.DeepClone())]);
            var ordered = new JsonArray([.. array.OrderByDescending(item => item?["createdAt"]?.GetValue<string>() ?? item?["startedAt"]?.GetValue<string>(), StringComparer.Ordinal).Select(item => item?.DeepClone())]);
            var page = ToolResults.Page(ordered, new JsonObject { ["limit"] = limit }, tool);
            page["scope"] = selected is null ? "all" : "matching instance plus records with unknown activity; continuation lists all activity";
            // The continuation lists all activity, not the filtered summary.
            if (selected is not null && page["continuation"] is JsonObject continuation)
                continuation["arguments"]!["offset"] = 0;
            return page;
        }

        await Section("jobs", () => C.FromCommand(C.Jobs.List), node => Activities(node, "job_list")).ConfigureAwait(false);
        await Section("sessions", () => C.FromCommand(C.Sessions.List), node => Activities(node, "process_list")).ConfigureAwait(false);
        result["nextSteps"] = new JsonArray("Use launch_plan or diagnose before troubleshooting a launch.", "Use content_search then content_plan, install selected versions, and verify with content_list.", "Use capabilities with a toolName or topic to discover detailed syntax.");
        return result;
    }

}