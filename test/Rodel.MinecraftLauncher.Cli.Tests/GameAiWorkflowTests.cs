// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.GameBridge;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects the canonical typed game-AI command through an actual CLI process and Core monitor.</summary>
[TestClass]
public sealed class GameAiWorkflowTests
{
    /// <summary>Review: Core ingress tests bypass command parsing and source-generated wire metadata. Reading an unconfigured empty slot through a separate CLI process must work without starting a model, while the same request with a stale world must fail before any history is created.</summary>
    [TestMethod]
    public async Task EmptySave_RealCliTypedIngress_OpensWithoutModelAndRejectsStaleWorld()
    {
        using var cli = new CliSandbox();
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken, cli.Root);
        await using var peer = await fixture.ConnectAsync(request => fixture.Response(request), TestContext.CancellationToken);
        var observed = (await cli.RunAsync("process", "game", "snapshot", fixture.Session)).ReadEnvelope(0);
        var snapshot = observed["data"]!.Deserialize(GameBridgeJsonContext.Default.GameSnapshot)!;
        var request = new GameAiRequest { Action = "open", WorldKey = "synthetic-save", Snapshot = snapshot };
        var opened = (await cli.RunAsync("process", "game", "ai", fixture.Session, "--request", JsonSerializer.Serialize(request, GameAiJsonContext.Default.GameAiRequest))).ReadEnvelope(0);
        Assert.IsFalse(opened["data"]!["modelAvailable"]!.GetValue<bool>());
        Assert.IsEmpty(opened["data"]!["turns"]!.AsArray());
        Assert.IsNull(opened["data"]!["conversationId"]);
        var stale = request with { Snapshot = snapshot with { WorldEpoch = "replaced" } };
        var rejected = (await cli.RunAsync("process", "game", "ai", fixture.Session, "--request", JsonSerializer.Serialize(stale, GameAiJsonContext.Default.GameAiRequest))).ReadEnvelope(1);
        Assert.AreEqual("AI_GAME_WORLD_CHANGED", rejected["error"]!["code"]!.ToString());
    }

    /// <summary>Gets cancellation for this test's owned game peer and Core monitor.</summary>
    public TestContext TestContext { get; set; } = null!;
}