// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Multiplayer;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class MultiplayerCommands
{
    private readonly Option<string?> _code = new("--code") { Description = S.MultiplayerCodeOption };
    private readonly Option<string?> _session = new("--session") { Description = S.MultiplayerSessionOption };
    private readonly Option<bool> _autoOpen = new("--auto-open") { Description = S.MultiplayerAutoOpenOption, DefaultValueFactory = _ => true };
    private readonly Option<bool> _exitGame = new("--exit-game") { Description = S.MultiplayerExitGameOption };
    private readonly Option<bool> _startGame = new("--start-game") { Description = S.MultiplayerStartGameOption, DefaultValueFactory = _ => true };
    private readonly Option<bool> _restartGame = new("--restart-game") { Description = S.MultiplayerRestartGameOption };

    internal MultiplayerCommands(Command root)
    {
        var group = Group(root, "multiplayer", S.MultiplayerGroup);
        var host = Add(group, "host", S.MultiplayerHost, async (app, args, ct) =>
            JsonSerializer.SerializeToNode(await app.Client.Multiplayer.RunAsync(Request("host", args), ct).ConfigureAwait(false), MultiplayerJsonContext.Default.MultiplayerRoom),
            [A.Instance], [_session, _autoOpen, O.Name, O.Player, O.World, O.Account, O.Java, O.Offline]);
        host.TaskFactory = args => new MultiplayerTask(Request("host", args));
        var join = Add(group, "join", S.MultiplayerJoin, async (app, args, ct) =>
            JsonSerializer.SerializeToNode(await app.Client.Multiplayer.RunAsync(Request("guest", args), ct).ConfigureAwait(false), MultiplayerJsonContext.Default.MultiplayerRoom),
            [A.Instance], [_code, _session, _startGame, _restartGame, O.Name, O.Player, O.Account, O.Java, O.Offline]);
        Required(join, _code);
        join.TaskFactory = args => new MultiplayerTask(Request("guest", args));
        Add(group, "show", S.MultiplayerShow, (app, _, _) => Task.FromResult(JsonSerializer.SerializeToNode(app.Client.Multiplayer.Get(), MultiplayerJsonContext.Default.MultiplayerRoom)));
        Add(group, "stop", S.MultiplayerStop, async (app, args, ct) =>
            JsonSerializer.SerializeToNode(await app.Client.Multiplayer.EndAsync(args.GetRequiredValue(A.Target), args.GetValue(_exitGame) ? MultiplayerEndMode.ExitGame : MultiplayerEndMode.KeepGame, ct).ConfigureAwait(false), MultiplayerJsonContext.Default.MultiplayerRoom), [A.Target], [_exitGame]);
        AddControl(group, "retry", S.MultiplayerRetry);
        AddControl(group, "launch", S.MultiplayerLaunch);
        var invitation = Add(group, "change-code", S.MultiplayerChangeCode, async (app, args, ct) =>
            JsonSerializer.SerializeToNode(await app.Client.Multiplayer.ChangeInvitationAsync(args.GetRequiredValue(A.Target), args.GetValue(_code)!, ct).ConfigureAwait(false), MultiplayerJsonContext.Default.MultiplayerRoom), [A.Target], [_code]);
        Required(invitation, _code);
        var fallback = Add(group, "fallback", S.MultiplayerFallback, async (app, args, ct) =>
            JsonSerializer.SerializeToNode(await app.Client.Multiplayer.ControlAsync(args.GetRequiredValue(A.Target), "fallback", args.GetValue(O.ServerAddress), ct).ConfigureAwait(false), MultiplayerJsonContext.Default.MultiplayerRoom),
            [A.Target], [O.ServerAddress]);
        Required(fallback, O.ServerAddress);
        Add(group, "export-log", S.MultiplayerExportLog, async (app, args, ct) =>
        {
            await app.Client.Multiplayer.ExportLogAsync(args.GetRequiredValue(A.Target), args.GetRequiredValue(A.Destination), ct).ConfigureAwait(false);
            return JsonValue.Create(args.GetRequiredValue(A.Destination));
        }, [A.Target, A.Destination]);
    }

    private static void AddControl(Command parent, string action, string description) => Add(parent, action, description, async (app, args, ct) =>
        JsonSerializer.SerializeToNode(await app.Client.Multiplayer.ControlAsync(args.GetRequiredValue(A.Target), action, ct: ct).ConfigureAwait(false), MultiplayerJsonContext.Default.MultiplayerRoom), [A.Target]);

    private MultiplayerRequest Request(string role, ParseResult args) => new()
    {
        RoomId = Guid.NewGuid().ToString("N"),
        Role = role,
        InstanceId = args.GetRequiredValue(A.Instance),
        Name = args.GetValue(O.Name) ?? string.Empty,
        PlayerName = args.GetValue(O.Player) ?? string.Empty,
        SessionId = args.GetValue(_session),
        AutoOpen = role == "host" && args.GetValue(_autoOpen),
        StartGame = role == "guest" && args.GetValue(_startGame),
        RestartGame = role == "guest" && args.GetValue(_restartGame),
        Invitation = role == "guest" ? args.GetValue(_code) : null,
        Launch = new() { World = role == "host" ? args.GetValue(O.World) : null, AccountId = args.GetValue(O.Account), PlayerName = args.GetValue(O.Player), JavaPath = args.GetValue(O.Java), Offline = args.GetValue(O.Offline) }
    };
}