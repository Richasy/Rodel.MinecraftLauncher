// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Checks which durable uncertainties still need the player's attention.</summary>
[TestClass]
public sealed class EffectPresentationTests
{
    /// <summary>
    /// Review: acknowledging uncertainty must remove the repeated UI prompt without erasing unknown attribution
    /// or allowing the old goal to replay. The real journal, settings store and exact acknowledgement fingerprint
    /// distinguish this behavior from merely hiding a button; no model or game operation is substituted as success.
    /// </summary>
    [TestMethod]
    public async Task AcceptedUnknownEffect_StopsPrompting_ButStillBlocksOldGoalReplay()
    {
        using var sandbox = new AiSandbox();
        var client = new LauncherClient(sandbox.Paths);
        await client.Settings.UpdateAsync(new JsonObject { ["maxMemoryMb"] = 1024 }, [], cancellationToken: TestContext.CancellationToken);
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        var journal = new AiToolJournal(sandbox.Paths);
        var arguments = new JsonObject { ["changes"] = new JsonObject { ["maxMemoryMb"] = 2048 } };
        var intent = await journal.PrepareAsync(turn, Guid.NewGuid().ToString("N"), "missing-receipt", "config_patch", arguments, TestContext.CancellationToken);
        await journal.DispatchingAsync(turn, intent, TestContext.CancellationToken);
        AiToolJournal.Recover(turn);
        turn["status"] = "completed";
        await sandbox.Store.SaveTurnAsync(turn, TestContext.CancellationToken);

        var observed = await journal.CheckAsync(conversation["id"]!.ToString(), turn["id"]!.ToString(), TestContext.CancellationToken);
        Assert.IsTrue(AiTurnObservation.HasPendingEffects(observed));
        Assert.IsTrue(AiTurnObservation.HasEffectProblem(observed));
        var effect = observed.UnknownEffects![0]!;
        var accepted = await journal.AcknowledgeAsync(new()
        {
            ConversationId = conversation["id"]!.ToString(),
            TurnId = turn["id"]!.ToString(),
            InvocationId = effect["invocationId"]!.ToString(),
            Fingerprint = effect["fingerprint"]!.ToString(),
            AcceptUnknown = true,
        }, TestContext.CancellationToken);

        Assert.IsFalse(AiTurnObservation.HasPendingEffects(accepted));
        Assert.IsFalse(AiTurnObservation.HasEffectProblem(accepted));
        Assert.HasCount(1, accepted.UnknownEffects!);
        Assert.AreEqual("unknown_effect", accepted.Intents![0]!["state"]!.ToString());
        var stored = sandbox.Store.GetTurnDocument(conversation["id"]!.ToString(), turn["id"]!.ToString(), true);
        var blocked = await Assert.ThrowsExactlyAsync<LauncherOperationException>(() => journal.PrepareAsync(
            stored, Guid.NewGuid().ToString("N"), "repeat", "config_patch", arguments, TestContext.CancellationToken));
        Assert.AreEqual("AI_EFFECT_REVIEW_REQUIRED", blocked.Code);
    }

    /// <summary>Gets the test runner cancellation context.</summary>
    public TestContext TestContext { get; set; } = null!;
}