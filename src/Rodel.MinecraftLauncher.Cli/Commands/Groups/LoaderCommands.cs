// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Loaders;
using System.CommandLine;
using System.Text.Json;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class LoaderCommands
{

    internal LoaderCommands(Command root)
    {
        var loader = Group(root, "loader", S.GroupLoaderDescription);
        Required(Add(loader, "list", S.LoaderListDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await new LoaderService(app.Paths).ListAsync(result.GetRequiredValue(A.Target), result.GetValue(O.Minecraft) ?? throw new ArgumentException(S.ErrorMinecraftVersionRequired), offline: result.GetValue(O.Offline), cancellationToken: ct).ConfigureAwait(false), LoaderJsonContext.Default.LoaderCatalog), [A.Target], [O.Minecraft, O.Offline]), O.Minecraft);
        static LoaderInstallationOptions LoaderOptions(ParseResult result) => new() { MinecraftVersion = result.GetValue(O.Minecraft), LoaderVersion = result.GetValue(O.LoaderVersion), JavaPath = result.GetValue(O.Java), LibraryMirrors = result.GetValue(O.LibraryMirrors) ?? [] };
        Add(loader, "install", S.LoaderInstallDescription, async (app, result, ct) => await app.Client.InstallLoaderAsync(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Item), LoaderOptions(result), ct).ConfigureAwait(false), LoaderJsonContext.Default.LoaderInstallationResult, [A.Instance, A.Item], [O.Minecraft, O.LoaderVersion, O.Java, O.LibraryMirrors])
            .TaskFactory = result => new InstallLoaderTask(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Item), LoaderOptions(result));
    }
}