// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Content;
using System.CommandLine;
using System.Text.Json;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class ResourcePackCommands
{
    internal LauncherCommand List { get; }
    internal LauncherCommand Select { get; }

    internal ResourcePackCommands(Command root)
    {
        var packs = Group(root, "resourcepack", S.GroupResourcepackDescription);
        List = Add(packs, "list", S.ResourcePackListDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.GetResourcePacksAsync(result.GetRequiredValue(A.Instance), result.GetValue(O.Encoding), ct).ConfigureAwait(false), ContentJsonContext.ResourcePackSelection), [A.Instance], [O.Encoding]);
        Select = Add(packs, "select", S.ResourcePackSelectDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.SelectResourcePacksAsync(result.GetRequiredValue(A.Instance), new ResourcePackSelectionOptions { Names = result.GetValue(A.Names) ?? [], Encoding = result.GetValue(O.Encoding), ExpectedFingerprint = result.GetValue(O.ExpectedFingerprint), PlanOnly = result.GetValue(O.Plan), Replace = result.GetValue(O.Replace), AllowIncompatible = result.GetValue(O.AllowIncompatible) }, ct).ConfigureAwait(false), ContentJsonContext.Default.ResourcePackSelectionResult), [A.Instance, A.Names], [O.Encoding, O.ExpectedFingerprint, O.Plan, O.Replace, O.AllowIncompatible]);
    }
}