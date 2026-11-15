// Copyright (c) Richasy. All rights reserved.

using System.CommandLine;
using System.Text.Json.Nodes;

using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal static class LauncherCommandHelp
{
    internal static JsonObject Describe(string? topic = null) =>
        Describe(topic is null ? LauncherCommands.Root : LauncherCommands.Find(topic.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

    internal static JsonObject Describe(Command command)
    {
        var commands = new Dictionary<string, string[]>();
        void Append(Command current, string path)
        {
            if (current.Hidden)
                return;
            using var writer = new StringWriter();
            LauncherCommands.WriteHelp(current, writer);
            commands[path] = [writer.ToString()];
        }

        if (command == LauncherCommands.Root)
        {
            foreach (var child in command.Subcommands)
                Append(child, child.Name);
        }
        else
        {
            Append(command, command.Name);
        }

        return ProtocolJson.Object(new ToolCommandHelp(false, commands,
            [
                S.HelpTokenBoundaries,
                S.HelpEntryPoints,
                S.HelpTaskLifetimes
            ]), CliJsonContext.Default.ToolCommandHelp);
    }
}