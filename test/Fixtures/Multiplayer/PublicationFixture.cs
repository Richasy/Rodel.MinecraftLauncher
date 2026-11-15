// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.GameBridge;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Tests;

/// <summary>Holds an actual bridge transport at an uncertain publication receipt until a check explicitly resolves it.</summary>
internal sealed class PublicationFixture(BridgeFixture bridge)
{
    internal bool Ready;
    internal int Submissions, ReceiptReads;
    internal string? Operation;
    private int _appliedPort;
    internal int AppliedPort { set => Volatile.Write(ref _appliedPort, value); }

    internal BridgeRpc Respond(BridgeRpc request)
    {
        JsonElement result;
        if (request.Method == "bridge.capabilities")
        {
            result = JsonSerializer.SerializeToElement(new BridgeCapabilities { Capabilities = ["game.snapshot", "game.query", "world.publish"] }, BridgeWireJsonContext.Default.BridgeCapabilities);
        }
        else if (request.Method == "game.snapshot")
        {
            result = JsonSerializer.SerializeToElement(new GameSnapshot { SessionId = bridge.Session, GameIncarnation = bridge.Incarnation, WorldEpoch = "1", LocalWorldEpoch = "1", Dimension = "minecraft:overworld", Ready = Volatile.Read(ref Ready), Mode = "local_owned", Sequence = "1", SampledAt = DateTimeOffset.UtcNow.ToString("O") }, GameBridgeJsonContext.Default.GameSnapshot);
        }
        else if (request.Method == "game.query")
        {
            result = JsonSerializer.SerializeToElement(new GameQueryResult { SessionId = bridge.Session, GameIncarnation = bridge.Incarnation, WorldEpoch = "1", Dimension = "minecraft:overworld", Kind = "world", Source = "synthetic_server", SampledAt = DateTimeOffset.UtcNow.ToString("O"), Data = JsonSerializer.SerializeToElement(new JsonObject { ["name"] = "Manual LAN recovery" }) }, GameBridgeJsonContext.Default.GameQueryResult);
        }
        else if (request.Method is "game.edit" or "game.operation.show")
        {
            if (request.Method == "game.edit") { Operation = request.Params!.Value.GetProperty("operationId").GetString(); Interlocked.Increment(ref Submissions); }
            else
            {
                Interlocked.Increment(ref ReceiptReads);
            }

            var port = Volatile.Read(ref _appliedPort);
            result = JsonSerializer.SerializeToElement(new GameOperation { OperationId = Operation!, State = port == 0 ? "unknown" : "applied", ErrorCode = port == 0 ? "GAME_OPERATION_OUTCOME_UNKNOWN" : null, Result = port == 0 ? null : JsonSerializer.SerializeToElement(new JsonObject { ["port"] = port }) }, GameBridgeJsonContext.Default.GameOperation);
        }
        else
        {
            return bridge.Response(request);
        }

        return new() { Id = request.Id, Result = result };
    }
}