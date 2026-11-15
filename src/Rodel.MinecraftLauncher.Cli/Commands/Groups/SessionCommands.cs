// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Launch;
using System.CommandLine;
using System.Text.Json;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class SessionCommands
{
    internal LauncherCommand List { get; }
    internal LauncherCommand Stop { get; }
    internal GameBridgeCommands Bridge { get; }

    internal SessionCommands(Command root)
    {
        var process = Group(root, "process", S.GroupProcessDescription);
        Bridge = new(process);
        List = Add(process, "list", S.SessionListDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(new SessionService(app.Paths).List(), LaunchJsonContext.Default.IReadOnlyListSessionStatus)));
        Add(process, "show", S.SessionShowDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(new SessionService(app.Paths).Get(result.GetRequiredValue(A.Target)), LaunchJsonContext.Default.SessionStatus)), [A.Target]);
        Stop = Add(process, "stop", S.SessionStopDescription, (app, result, ct) => Task.FromResult(new SessionService(app.Paths).Stop(result.GetRequiredValue(A.Target), result.GetValue(O.Force))), LaunchJsonContext.Default.SessionStopResult, [A.Target], [O.Force]);
    }
}