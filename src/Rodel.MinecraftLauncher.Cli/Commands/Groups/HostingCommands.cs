// Copyright (c) Richasy. All rights reserved.

using System.CommandLine;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class HostingCommands
{
    internal Command Mcp { get; }
    internal Command SessionHost { get; }
    internal Command JobHost { get; }
    internal Command JobDispatch { get; }
    internal Option<string> ToolProfile { get; } = new("--tool-profile") { Description = S.SkinToolProfileDescription, DefaultValueFactory = _ => "launcher" };

    internal Option<string?> SkinScope { get; } = new("--skin-scope") { Description = S.SkinScopeBindingDescription };

    internal HostingCommands(Command root)
    {
        Mcp = Group(root, "mcp", S.GroupMcpDescription);
        ToolProfile.AcceptOnlyFromAmong("launcher", "skins", "discovery");
        var serve = Host(Mcp, "serve", S.HostingServeDescription, Program.RunMcpAsync, options: [O.Stdio, ToolProfile, SkinScope]);
        var tools = Group(root, "tools", S.GroupToolsDescription);
        var list = Host(tools, "list", S.HostingListDescription, Program.ListToolsAsync, options: [ToolProfile, SkinScope]);
        var call = Host(tools, "call", S.HostingCallDescription, Program.CallToolAsync, [A.Target], [O.Arguments, O.ArgumentsFile, ToolProfile, SkinScope]);
        call.Validators.Add(result =>
        {
            if (result.GetResult(O.Arguments) is not null && result.GetResult(O.ArgumentsFile) is not null)
                result.AddError(S.ErrorToolArgumentsConflict);
        });
        foreach (var command in new[] { serve, list, call })
        {
            command.Validators.Add(result =>
            {
                if (result.GetValue(SkinScope) is not null && result.GetValue(ToolProfile) != "skins")
                    result.AddError(S.SkinScopeProfileError);
            });
        }

        SessionHost = Host(root, "session-host", S.HostingSessionHostDescription, Program.RunSessionHostAsync, [A.Target]);
        SessionHost.Hidden = true;
        JobHost = Host(root, "job-host", S.HostingJobHostDescription, Program.RunJobHostAsync, [A.Target]);
        JobHost.Hidden = true;
        JobDispatch = Host(root, "job-dispatch", S.HostingJobHostDescription, Program.RunJobDispatchAsync, [A.Target]);
        JobDispatch.Hidden = true;
    }
}