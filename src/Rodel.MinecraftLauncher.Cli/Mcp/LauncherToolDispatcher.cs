// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class LauncherToolDispatcher(LauncherApplication application, IReadOnlyList<LauncherToolDefinition>? tools = null, string? skinScope = null)
{
    private readonly ToolResults _results = new(application.Paths.Root);

    internal Task<(byte[] Bytes, string MimeType)> ReadImageAsync(ResultImageReadInput input, CancellationToken ct) =>
        ToolImages.ReadAsync(application.Paths.Root, input, ct);

    /// <summary>Normalizes protocol JSON arguments and invokes the shared CLI path with bounded result output.</summary>
    /// <param name="name">The exact registered tool name.</param>
    /// <param name="arguments">An argument object; null or undefined means empty input, while other JSON shapes fail.</param>
    /// <param name="ct">Requests cancellation of the operation, reported using the result envelope.</param>
    /// <returns>The actual command outcome, including validation failures rather than a transport-only success.</returns>
    public Task<ToolResult> InvokeAsync(string name, JsonElement arguments, CancellationToken ct = default)
    {
        if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined or JsonValueKind.Null))
            return _results.BoundAsync(ToolResult.Failure(name, new ArgumentException("Tool arguments must be a JSON object.")));
        var input = arguments.ValueKind == JsonValueKind.Object ? JsonSerializer.SerializeToNode(arguments, CliJsonContext.Default.JsonElement)!.AsObject() : [];
        return InvokeAsync(name, input, ct: ct);
    }

    /// <summary>Validates typed input, executes the registered CLI command, and formats a bounded protocol result.</summary>
    /// <param name="name">The exact registered tool name; unknown names produce a failure envelope.</param>
    /// <param name="arguments">Caller-owned input, cloned before undeclared arguments are removed and reported as warnings.</param>
    /// <param name="ct">Requests cancellation of execution, without discarding an already completed operation's output.</param>
    /// <param name="clientRequestId">Optional transport metadata identity, separate from the tool's actual argument schema.</param>
    /// <returns>The command outcome with shared error classification, formatting, and retention semantics.</returns>
    public async Task<ToolResult> InvokeAsync(string name, JsonObject arguments, string? clientRequestId = null, CancellationToken ct = default)
    {
        ToolResult result;
        JsonObject? input = null;
        JsonObject? argumentWarning = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            var definition = (tools ?? LauncherToolsCatalog.Tools).FirstOrDefault(tool => tool.Name == name)
                ?? throw new LauncherOperationException("UNKNOWN_TOOL", $"Unknown launcher tool '{name}'. Call capabilities to discover implemented tools.");
            input = (JsonObject)arguments.DeepClone();
            var hasDeclared = definition.InputSchema.TryGetProperty("properties", out var declared);
            var ignored = input.Select(property => property.Key).Where(key => !hasDeclared || !declared.TryGetProperty(key, out _)).ToArray();
            if (ignored.Length > 0)
            {
                argumentWarning = ProtocolJson.Object(new IgnoredArgumentsWarning(
                    "IGNORED_ARGUMENTS", ignored,
                    "These arguments are not declared by this tool and had no effect. Inspect the tool schema for supported parameter names; no aliases were guessed.",
                    new ToolContinuation("capabilities", ProtocolJson.Object(new CapabilitiesInput(name), ToolInputJsonContext.Default.CapabilitiesInput))),
                    CliJsonContext.Default.IgnoredArgumentsWarning);
            }

            foreach (var key in ignored)
                input.Remove(key);

            if (name == "tool_call")
            {
                var call = new ToolInputContract<DiscoveredToolCall>(DiscoveryToolJsonContext.Default.DiscoveredToolCall).Deserialize(input);
                if (!ToolCatalog.Entries.ContainsKey(call.Name) || !LauncherToolsCatalog.Tools.Any(tool => tool.Name == call.Name))
                    throw new LauncherOperationException("UNKNOWN_TOOL", "Use an exact Core capability from capabilities; CLI-only operations use execute.");
                var observed = await new LauncherToolService(application.Client, application.StartToolTaskAsync).InvokeAsync(call.Name, call.Arguments, ct, clientRequestId).ConfigureAwait(false);
                var shared = observed.Deserialize(ToolJsonContext.Default.ToolResult)!;
                if (argumentWarning is not null) shared.Warnings.Add((JsonNode)argumentWarning);
                return shared;
            }

            if (skinScope is not null)
            {
                new Core.Skins.SkinService(application.Paths).Proposals.CheckBinding(skinScope,
                    input["documentId"]?.GetValue<string>(), input["proposalId"]?.GetValue<string>());
                if (input["scopeId"]?.GetValue<string>() is { } suppliedScope && suppliedScope != skinScope)
                    throw new UnauthorizedAccessException("The request names a different skin scope.");
            }

            if (ToolCatalog.Entries.ContainsKey(name))
            {
                var observed = await new LauncherToolService(application.Client, application.StartToolTaskAsync).InvokeAsync(name, input, ct, clientRequestId).ConfigureAwait(false);
                var shared = observed.Deserialize(ToolJsonContext.Default.ToolResult)!;
                if (argumentWarning is not null) shared.Warnings.Add((JsonNode)argumentWarning);
                return shared;
            }

            var command = definition.Parse?.Invoke(input) ?? throw new InvalidOperationException("A CLI-only tool needs its command binding.");
            var executor = clientRequestId is null ? application : application.WithClientRequestId(clientRequestId);
            var data = await executor.ExecuteAsync(command, ct).ConfigureAwait(false);
            data = await definition.Format(_results, data, input, ct).ConfigureAwait(false);
            result = CommandResults.FromCommand(name, data, command.CommandResult.Command as LauncherCommand);
        }
        catch (Exception ex)
        {
            result = ToolResult.Failure(name, ex);
        }

        if (argumentWarning is not null)
            result.Warnings.Add((JsonNode)argumentWarning);
        return await _results.BoundAsync(result, input).ConfigureAwait(false);
    }

}