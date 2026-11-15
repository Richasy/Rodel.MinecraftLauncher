// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Runtime;
using System.CommandLine;
using System.Text.Json;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class RuntimeCommands
{
    internal LauncherCommand Show { get; }
    internal LauncherCommand Operations { get; }
    internal LauncherCommand Operation { get; }
    internal LauncherCommand Rollback { get; }
    internal LauncherCommand Change { get; }

    internal RuntimeCommands(Command root)
    {
        var runtime = Group(root, "runtime", S.GroupRuntimeDescription);
        Show = Add(runtime, "show", S.RuntimeShowDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Runtimes.ShowAsync(result.GetRequiredValue(A.Instance), ct).ConfigureAwait(false), RuntimeJsonContext.Default.RuntimeStatus), [A.Instance]);
        Operations = Add(runtime, "operations", S.RuntimeOperationsDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(app.Client.Runtimes.GetOperations(result.GetRequiredValue(A.Instance)), RuntimeJsonContext.Default.RuntimeOperationList)), [A.Instance]);
        Operations.Aliases.Add("list");
        Operation = Add(runtime, "operation", S.RuntimeOperationDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(app.Client.Runtimes.GetOperation(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Item)), RuntimeJsonContext.Default.RuntimeOperationSummary)), [A.Instance, A.Item]);
        Rollback = Add(runtime, "rollback", S.RuntimeRollbackDescription, async (app, result, ct) => await app.Client.Runtimes.RollbackAsync(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Item), ct).ConfigureAwait(false), RuntimeJsonContext.Default.RuntimeRollbackResult, [A.Instance, A.Item]);
        Option[] runtimeOptions = [O.Minecraft, O.Version, O.Loader, O.LoaderVersion, O.Java, O.LibraryMirrors, O.Plan, O.Offline];
        Change = Add(runtime, "change", S.RuntimeChangeDescription, async (app, result, ct) => await app.Client.Runtimes.ChangeAsync(result.GetRequiredValue(A.Instance), ParseOptions(result), ct).ConfigureAwait(false), RuntimeJsonContext.Default.RuntimeChangeResult, [A.Instance], runtimeOptions);
        Change.TaskFactory = result => new ChangeRuntimeTask(result.GetRequiredValue(A.Instance), ParseOptions(result));
        Add(runtime, "remove-loader", S.RuntimeRemoveLoaderDescription, async (app, result, ct) => await app.Client.Runtimes.ChangeAsync(result.GetRequiredValue(A.Instance), ParseOptions(result, removeLoader: true), ct).ConfigureAwait(false), RuntimeJsonContext.Default.RuntimeChangeResult, [A.Instance], runtimeOptions)
            .TaskFactory = result => new ChangeRuntimeTask(result.GetRequiredValue(A.Instance), ParseOptions(result, removeLoader: true));
    }

    private static RuntimeChangeOptions ParseOptions(ParseResult args, bool removeLoader = false) => new()
    {
        MinecraftVersion = args.GetValue(O.Minecraft),
        VersionId = args.GetValue(O.Version),
        Loader = args.GetValue(O.Loader),
        LoaderVersion = args.GetValue(O.LoaderVersion),
        JavaPath = args.GetValue(O.Java),
        LibraryMirrors = [.. args.GetValue(O.LibraryMirrors) ?? []],
        RemoveLoader = removeLoader,
        PlanOnly = args.GetValue(O.Plan),
        Offline = args.GetValue(O.Offline)
    };
}