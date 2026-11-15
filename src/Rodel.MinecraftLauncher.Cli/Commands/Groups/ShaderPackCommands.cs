// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Content;
using System.CommandLine;
using System.Text.Json;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class ShaderPackCommands
{
    internal LauncherCommand List { get; }
    internal LauncherCommand Select { get; }
    internal Option<string?> Engine { get; } = new("--engine") { Description = S.ShaderPackEngineDescription };
    internal Option<string?> FileName { get; } = new("--file") { Description = S.ShaderPackFileDescription };

    internal ShaderPackCommands(Command root)
    {
        Engine.AcceptOnlyFromAmong("iris", "oculus", "optifine");
        var packs = Group(root, "shaderpack", S.GroupShaderpackDescription);
        List = Add(packs, "list", S.ShaderPackListDescription, async (app, result, ct) =>
            JsonSerializer.SerializeToNode(await app.Client.GetShaderPacksAsync(result.GetRequiredValue(A.Instance), result.GetValue(Engine), ct).ConfigureAwait(false),
                ContentJsonContext.ShaderPackSelection), [A.Instance], [Engine]);
        Select = Add(packs, "select", S.ShaderPackSelectDescription, async (app, result, ct) =>
            JsonSerializer.SerializeToNode(await app.Client.SelectShaderPackAsync(result.GetRequiredValue(A.Instance), new()
            {
                FileName = result.GetValue(FileName),
                Engine = result.GetValue(Engine),
                ExpectedFingerprint = result.GetValue(O.ExpectedFingerprint),
                PlanOnly = result.GetValue(O.Plan)
            }, ct).ConfigureAwait(false), ContentJsonContext.ShaderPackSelectionOutcome),
            [A.Instance], [Engine, FileName, O.ExpectedFingerprint, O.Plan]);
    }
}