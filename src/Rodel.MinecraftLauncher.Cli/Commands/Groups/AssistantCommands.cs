// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Cli.Assistant;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.Storage;
using System.CommandLine;
using S = Rodel.MinecraftLauncher.Cli.Strings.AssistantStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class AssistantCommands
{
    internal Command Root { get; }
    internal Command Serve { get; }

    internal AssistantCommands(Command parent)
    {
        Root = CommandFactory.Group(parent, "ai", S.Root);
        Serve = CommandFactory.Host(Root, "serve", S.Serve, async (parsed, ct) =>
        {
            await new AssistantService(new LauncherPaths(parsed.GetValue(CliOptions.DataDirectory))).RunHostAsync(ct).ConfigureAwait(false);
            return 0;
        });
        Serve.Hidden = true;
        ServiceCommands.Register(Root);
        ConversationCommands.Register(Root);
        ExtensionCommands.Register(Root);
    }
}