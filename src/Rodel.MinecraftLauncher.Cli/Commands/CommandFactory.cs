// Copyright (c) Richasy. All rights reserved.

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;

using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal static class CommandFactory
{
    internal static LauncherCommand Add<T>(Command parent, string name, string description,
        Func<LauncherApplication, ParseResult, CancellationToken, Task<T>> handler, JsonTypeInfo<T> resultType,
        Argument[]? arguments = null, Option[]? options = null) =>
        Add(parent, name, description, async (app, result, ct) =>
            JsonSerializer.SerializeToNode(await handler(app, result, ct).ConfigureAwait(false), resultType), arguments, options);

    internal static Command Group(Command parent, string name, string description)
    {
        var group = new Command(name, description);
        parent.Subcommands.Add(group);
        return group;
    }
    internal static LauncherCommand Add(Command parent, string name, string description, Func<LauncherApplication, ParseResult, CancellationToken, Task<JsonNode?>> handler, Argument[]? arguments = null, Option[]? options = null)
    {
        var command = new LauncherCommand(name, description, handler);
        foreach (var argument in arguments ?? [])
            command.Arguments.Add(argument);
        foreach (var option in options ?? [])
            command.Options.Add(option);
        command.SetAction(Program.InvokeApplicationAsync);
        parent.Subcommands.Add(command);
        return command;
    }
    internal static void Required(Command command, Option<string?> option) => command.Validators.Add(result =>
    {
        if (result.GetResult(option) is not { Tokens.Count: > 0 })
            result.AddError(string.Format(S.ErrorOptionRequired, option.Name));
    });
    internal static LauncherCommand Confirm(LauncherCommand command)
    {
        command.ValidateInput = result =>
        {
            if (!result.GetValue(O.Confirm))
                throw new ArgumentException(S.ErrorConfirmationRequired);
        };
        return command;
    }

    internal static Command Host(Command parent, string name, string description, Func<ParseResult, CancellationToken, Task<int>> action, Argument[]? arguments = null, Option[]? options = null)
    {
        var command = new Command(name, description);
        foreach (var argument in arguments ?? [])
            command.Arguments.Add(argument);
        foreach (var option in options ?? [])
            command.Options.Add(option);
        command.SetAction(action);
        parent.Subcommands.Add(command);
        return command;
    }
}