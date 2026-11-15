// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.CommandLine;
using System.Text.Json;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class JobCommands
{
    internal LauncherCommand List { get; }
    internal LauncherCommand Show { get; }
    internal LauncherCommand Cancel { get; }

    internal JobCommands(Command root)
    {
        var jobs = Group(root, "job", S.GroupJobDescription);
        List = Add(jobs, "list", S.JobListDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(app.Client.Jobs.List(), ApplicationJsonContext.Jobs)));
        Show = Add(jobs, "show", S.JobShowDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(app.Client.Jobs.Get(result.GetRequiredValue(A.Target)), ApplicationJsonContext.Job)), [A.Target]);
        Cancel = Add(jobs, "cancel", S.JobCancelDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Jobs.CancelAsync(result.GetRequiredValue(A.Target), ct).ConfigureAwait(false), ApplicationJsonContext.Job), [A.Target]);
        Add(jobs, "resume", S.JobResumeDescription, (app, result, ct) => app.ResumeJobAsync(result.GetRequiredValue(A.Target), ct), [A.Target]);
    }
}