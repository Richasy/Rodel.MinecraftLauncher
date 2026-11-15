// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Installation;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class InformationCommands
{
    internal LauncherCommand Capabilities { get; }
    internal LauncherCommand Context { get; }
    internal LauncherCommand ResultRead { get; }
    internal LauncherCommand ResultImageRead { get; }

    internal InformationCommands(Command root)
    {
        var refreshVersions = new Option<bool>("--refresh") { Description = S.VersionCatalogRefresh };
        Add(root, "version", S.InformationRootVersionDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(LauncherClient.GetVersion(), ApplicationJsonContext.Default.LauncherVersion)));
        var self = Group(root, "self", S.GroupSelfDescription);
        Add(self, "check-update", S.InformationSelfCheckUpdateDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await new MaintenanceService(app.Paths).CheckUpdateAsync(ct).ConfigureAwait(false), ApplicationJsonContext.Default.UpdateCheck));
        Add(Group(root, "versions", S.GroupVersionsDescription), "list", S.InformationVersionsListDescription, async (app, result, ct) => ProtocolJson.Object(await app.Client.Installation.GetVersionsAsync(offline: result.GetValue(O.Offline), refresh: result.GetValue(refreshVersions), cancellationToken: ct).ConfigureAwait(false), InstallationJsonContext.Default.VersionCatalog), options: [O.Offline, refreshVersions]);
        Capabilities = Add(root, "capabilities", S.InformationRootCapabilitiesDescription, (app, result, ct) => Task.FromResult<JsonNode?>(DescribeCapabilities(result)), options: [O.Tool, O.Topic]);
        Context = Add(root, "context", S.InformationRootContextDescription, async (app, result, ct) => await new ContextCommandHandler(app.ExecuteCommandAsync).ExecuteAsync(result, ct).ConfigureAwait(false), options: [O.Instance, O.Limit]);
        var retained = Group(root, "result", S.GroupResultDescription);
        ResultRead = Add(retained, "read", S.InformationResultReadDescription, async (app, result, ct) => await ReadResultAsync(app, result, ct).ConfigureAwait(false), [A.Target], [O.Pointer, O.Offset, O.Limit, O.Characters, O.PropertyOffset]);
        ResultImageRead = Add(retained, "image", S.InformationResultImageDescription, async (app, result, ct) =>
        {
            var input = new ResultImageReadInput(result.GetRequiredValue(A.Target), result.GetValue(O.Pointer) ?? string.Empty);
            var (Bytes, MimeType) = await ToolImages.ReadAsync(app.Paths.Root, input, ct).ConfigureAwait(false);
            return ProtocolJson.Object(new ToolImageInfo(input.ResultId, input.Pointer, MimeType, Bytes.Length), CliJsonContext.Default.ToolImageInfo);
        }, [A.Target], [O.Pointer]);
    }

    private static JsonObject DescribeCapabilities(ParseResult args)
    {
        if (args.GetValue(O.Tool) is { } name)
        {
            var tool = LauncherToolsCatalog.Tools.FirstOrDefault(tool => tool.Name == name)
                ?? throw new LauncherOperationException("UNKNOWN_TOOL", string.Format(S.ErrorUnknownTool, name));
            return LauncherToolsCatalog.Describe(tool);
        }

        if (args.GetValue(O.Topic) is { } topic)
            return LauncherCommandHelp.Describe(topic);
        return ProtocolJson.Object(new ToolCapabilityIndex(
            [.. LauncherToolsCatalog.Tools.Select(tool => tool.Name)],
            [.. LauncherCommands.Root.Subcommands.Where(command => !command.Hidden).Select(command => command.Name)], 7,
            "Use toolName for one tool's schema, or topic for one CLI command's help. Image references are optional; fetch only for a visual task."), CliJsonContext.Default.ToolCapabilityIndex);
    }

    private static Task<JsonObject> ReadResultAsync(LauncherApplication app, ParseResult args, CancellationToken ct) =>
        new ToolResults(app.Paths.Root).ReadResultAsync(ProtocolJson.Object(new ResultReadInput(
            args.GetRequiredValue(A.Target),
            args.GetValue(O.Pointer),
            args.GetValue(O.Offset) ?? 0,
            args.GetValue(O.Limit) ?? 20,
            args.GetValue(O.Characters) ?? 8000,
            args.GetValue(O.PropertyOffset) ?? 0), ToolInputJsonContext.Default.ResultReadInput), ct);
}