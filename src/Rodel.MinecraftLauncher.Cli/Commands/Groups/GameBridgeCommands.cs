// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.GameBridge;
using System.CommandLine;
using System.Text.Json;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class GameBridgeCommands
{
    internal LauncherCommand Status { get; }
    internal LauncherCommand Snapshot { get; }
    internal LauncherCommand Edit { get; }
    internal LauncherCommand Region { get; }
    internal LauncherCommand Query { get; }
    internal LauncherCommand Operation { get; }
    internal LauncherCommand Cancel { get; }
    internal Option<string> Request { get; } = new("--request") { Description = S.BridgeEditRequest, Required = true };
    internal Option<string> RegionRequest { get; } = new("--request") { Description = S.BridgeRegionRequest, Required = true };
    internal Option<string> QueryRequest { get; } = new("--request") { Description = S.BridgeQueryRequest, Required = true };
    private readonly Option<string> _aiRequest = new("--request") { Description = S.BridgeAiRequest, Required = true };

    internal GameBridgeCommands(Command process)
    {
        var bridge = Group(process, "bridge", S.BridgeGroup);
        Status = Add(bridge, "show", S.BridgeShow, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.GameBridge.GetStatusAsync(result.GetRequiredValue(A.Target), ct).ConfigureAwait(false), GameBridgeJsonContext.Default.GameBridgeStatus), [A.Target]);
        var game = Group(process, "game", S.BridgeGameGroup);
        Add(game, "ai", S.BridgeAi, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.GameBridge.RequestAiAsync(result.GetRequiredValue(A.Target), JsonSerializer.Deserialize(result.GetRequiredValue(_aiRequest), GameAiJsonContext.Default.GameAiRequest) ?? throw new ArgumentException(S.BridgeAiRequest), ct).ConfigureAwait(false), GameAiJsonContext.Default.GameAiState), [A.Target], [_aiRequest]);
        Query = Add(game, "query", S.BridgeQuery, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.GameBridge.QueryAsync(result.GetRequiredValue(A.Target), JsonSerializer.Deserialize(result.GetRequiredValue(QueryRequest), GameBridgeJsonContext.Default.GameQueryRequest) ?? throw new ArgumentException(S.BridgeQueryRequest), ct).ConfigureAwait(false), GameBridgeJsonContext.Default.GameQueryResult), [A.Target], [QueryRequest]);
        Snapshot = Add(game, "snapshot", S.BridgeSnapshot, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.GameBridge.SnapshotAsync(result.GetRequiredValue(A.Target), ct).ConfigureAwait(false), GameBridgeJsonContext.Default.GameSnapshot), [A.Target]);
        Region = Add(game, "region", S.BridgeRegion, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.GameBridge.ReadRegionAsync(result.GetRequiredValue(A.Target), JsonSerializer.Deserialize(result.GetRequiredValue(RegionRequest), GameBridgeJsonContext.Default.GameRegionRequest) ?? throw new ArgumentException(S.BridgeRegionRequest), ct).ConfigureAwait(false), GameBridgeJsonContext.Default.GameRegion), [A.Target], [RegionRequest]);
        var interval = new Option<int>("--interval") { Description = S.BridgeInterval, DefaultValueFactory = _ => 500 };
        Add(game, "events", S.BridgeEvents, async (app, result, ct) =>
        {
            if (app.EventSink is null) throw new ArgumentException(S.BridgeEventsRequireEvents);
            await foreach (var snapshot in app.Client.GameBridge.WatchAsync(result.GetRequiredValue(A.Target), result.GetValue(interval), ct).ConfigureAwait(false))
                app.EventSink(JsonSerializer.SerializeToNode(snapshot, GameBridgeJsonContext.Default.GameSnapshot)!.AsObject());
            return null;
        }, [A.Target], [interval]);
        Edit = Add(game, "edit", S.BridgeEdit, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.GameBridge.EditAsync(result.GetRequiredValue(A.Target), ParseEdit(result.GetRequiredValue(Request)), ct).ConfigureAwait(false), GameBridgeJsonContext.Default.GameOperation), [A.Target], [Request]);
        var operation = Group(game, "operation", S.BridgeOperationGroup);
        Operation = Add(operation, "show", S.BridgeOperationShow, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.GameBridge.GetOperationAsync(result.GetRequiredValue(A.Target), result.GetRequiredValue(A.OptionalTarget), ct).ConfigureAwait(false), GameBridgeJsonContext.Default.GameOperation), [A.Target, A.OptionalTarget]);
        Cancel = Add(operation, "cancel", S.BridgeOperationCancel, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.GameBridge.CancelOperationAsync(result.GetRequiredValue(A.Target), result.GetRequiredValue(A.OptionalTarget), ct).ConfigureAwait(false), GameBridgeJsonContext.Default.GameOperation), [A.Target, A.OptionalTarget]);
    }

    private static GameEditRequest ParseEdit(string json) => JsonSerializer.Deserialize(json, GameBridgeJsonContext.Default.GameEditRequest) ?? throw new ArgumentException(S.BridgeEditRequest);
}