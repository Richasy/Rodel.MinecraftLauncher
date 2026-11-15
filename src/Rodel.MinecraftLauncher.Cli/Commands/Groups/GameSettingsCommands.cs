// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Configuration;
using System.CommandLine;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class GameSettingsCommands
{
    internal LauncherCommand Get { get; }
    internal LauncherCommand Set { get; }
    internal LauncherCommand Apply { get; }
    internal LauncherCommand DiscardPending { get; }

    internal GameSettingsCommands(Command root)
    {
        var game = Group(root, "game-settings", S.GroupGameSettingsDescription);
        Get = Add(game, "get", S.GameSettingsGetDescription, async (app, result, ct) => ProtocolJson.Object(await new GameSettingsService(app.Paths).GetAsync(app.Instances.Get(result.GetRequiredValue(A.Instance)).GameDirectory, result.GetValue(O.Encoding), ct).ConfigureAwait(false), ConfigurationJsonContext.Default.GameSettingsSnapshot), [A.Instance], [O.Encoding]);
        Set = Add(game, "set", S.GameSettingsSetDescription, async (app, result, ct) => await SetAsync(app, result, ct).ConfigureAwait(false), ConfigurationJsonContext.Default.GameSettingsChangeResult, [A.Instance, A.Changes], [O.Encoding, O.ExpectedFingerprint, O.Plan]);
        Apply = Add(game, "apply", S.GameSettingsApplyDescription, async (app, result, ct) => await new GameSettingsService(app.Paths).ApplyPendingAsync(app.Instances.Get(result.GetRequiredValue(A.Instance)).GameDirectory, result.GetValue(O.Overwrite), ct).ConfigureAwait(false), ConfigurationJsonContext.Default.GameSettingsChangeResult, [A.Instance], [O.Overwrite]);
        DiscardPending = Add(game, "discard-pending", S.GameSettingsDiscardPendingDescription, async (app, result, ct) => await GameSettingsService.DiscardPendingAsync(app.Instances.Get(result.GetRequiredValue(A.Instance)).GameDirectory, ct).ConfigureAwait(false), ConfigurationJsonContext.Default.GameSettingsChangeResult, [A.Instance]);
    }

    private static async Task<GameSettingsChangeResult> SetAsync(LauncherApplication app, ParseResult args, CancellationToken ct)
    {
        var target = app.Instances.Get(args.GetRequiredValue(A.Instance));
        var changes = JsonNode.Parse(args.GetRequiredValue(A.Changes)) as JsonObject ?? throw new ArgumentException(S.ErrorGameSettingsObject);
        return await new GameSettingsService(app.Paths).SetAsync(target.GameDirectory, changes, args.GetValue(O.Plan), args.GetValue(O.ExpectedFingerprint), args.GetValue(O.Encoding), ct).ConfigureAwait(false);
    }
}