// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using System.CommandLine;
using System.Text.Json;

using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal static class CommandJobs
{
    internal const string Operation = "command";

    internal static async Task<JobStatus> CreateCommandAsync(JobService jobs, LauncherPaths paths, ParseResult command, string? clientRequestId = null, CancellationToken ct = default)
    {
        if (command.CommandResult.Command is LauncherCommand { AsyncTaskFactory: { } factory })
        {
            var client = new LauncherClient(paths);
            return await client.Tasks.QueueAsync(await factory(client, command, ct).ConfigureAwait(false), clientRequestId, ct).ConfigureAwait(false);
        }

        var request = (command.CommandResult.Command as LauncherCommand)?.TaskFactory?.Invoke(command);
        return await (request is null
            ? jobs.CreateAsync(new JobDefinition(Operation, ProtocolJson.Object(new CommandJobRequest(
                LauncherCommands.Tokens(command, false),
                command.GetValue(CliArguments.Instance) ?? command.GetValue(CliOptions.Instance)), CommandJobJsonContext.Default.CommandJobRequest)), ct: ct, clientRequestId: clientRequestId)
            : new LauncherClient(paths).Tasks.QueueAsync(request, clientRequestId, ct)).ConfigureAwait(false);
    }

    internal static ParseResult Parse(JobDefinition definition)
    {
        if (definition.Operation != Operation)
            throw new InvalidDataException(string.Format(S.ErrorUnsupportedJobOperation, definition.Operation));
        var request = JsonSerializer.Deserialize(definition.Parameters, CommandJobJsonContext.Default.CommandJobRequest)
            ?? throw new InvalidDataException(S.ErrorMissingCommandRequest);
        return LauncherCommands.Parse(request.Arguments);
    }
}

internal sealed record CommandJobRequest(string[] Arguments, string? InstanceId = null);