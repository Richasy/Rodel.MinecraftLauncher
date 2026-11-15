// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.Storage;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using S = Rodel.MinecraftLauncher.Cli.Strings.AssistantStrings;

namespace Rodel.MinecraftLauncher.Cli.Assistant;

internal sealed class AssistantInvocation(LauncherApplication application)
{
    /// <summary>Gets paths scoped to the current CLI invocation's data root.</summary>
    public LauncherPaths Paths => application.Paths;
    /// <summary>Gets the same Core assistant service used by native callers.</summary>
    public AssistantService Client => application.Client.Assistant;
    /// <summary>Gets the explicit background Core host selected by the CLI.</summary>
    public AiHostOptions Host => application.AssistantHost;
    /// <summary>Gets whether this invocation permits interactive secret input.</summary>
    public bool AllowsSecretInput => application.AllowsInteractiveSecrets;
}

internal static class AssistantCommandFactory
{
    /// <summary>Adds an assistant command group to the shared System.CommandLine tree.</summary>
    public static Command Group(Command parent, string name, string description)
    {
        return CommandFactory.Group(parent, name, description);
    }
    /// <summary>Binds a concrete operation and serializes its typed result only at the CLI output boundary.</summary>
    public static Command Add<T>(Command parent, string name, string description,
        Func<AssistantInvocation, ParseResult, CancellationToken, Task<T>> action,
        Argument[]? arguments = null, Option[]? options = null, bool execution = false)
    {
        var command = CommandFactory.Add(parent, name, description,
            async (application, parsed, ct) =>
            {
                var result = await action(new AssistantInvocation(application), parsed, ct).ConfigureAwait(false);
                return result is null ? null : result is JsonNode node ? node
                    : JsonSerializer.SerializeToNode(result, AiJsonContext.Default.GetTypeInfo(result.GetType())
                        ?? throw new InvalidOperationException("Missing assistant output serialization metadata."));
            }, arguments, options);
        command.UsesCache = false;
        command.ValidateInput = parsed =>
        {
            if (parsed.GetValue(CliOptions.Background)) throw new ArgumentException(S.BackgroundNotSupported);
        };
        if (execution)
        {
            command.ResultStatus = data => data?["status"]?.ToString() switch
            {
                "canceled" => LauncherResultStatus.Canceled,
                "failed" or "incomplete" or "refused" or "partial" => LauncherResultStatus.Failed,
                _ => LauncherResultStatus.Succeeded
            };
        }

        return command;
    }
}