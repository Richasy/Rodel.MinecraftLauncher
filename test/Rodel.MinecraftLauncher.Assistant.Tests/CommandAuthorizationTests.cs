using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects the full CLI gateway's exact-consent and scope boundaries, separately from direct CLI authorization.</summary>
[TestClass]
public sealed class CommandAuthorizationTests
{
    /// <summary>
    /// The broad gateway cannot escape read-only, instance-only or disabled capability scopes.
    /// A dispatch substitute would perform a real write if called, so rejection must preserve both state and dispatch count.
    /// </summary>
    [TestMethod]
    [DataRow("read-only", "AI_TOOL_READ_ONLY")]
    [DataRow("instance", "AI_COMMAND_SCOPE_UNCONSTRAINED")]
    [DataRow("network-off", "AI_NETWORK_ACCESS_DISABLED")]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task GenericCommand_InsufficientScope_DoesNotDispatch(string restriction, string code)
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, restriction != "read-only", TestContext.CancellationToken);
        if (restriction == "instance") turn["instanceId"] = "selected-instance";
        if (restriction == "network-off") turn["networkAccessEnabled"] = false;
        var file = Path.Combine(sandbox.Root, "must-not-write.txt");
        var dispatched = 0;
        var launcher = new AiCoreTools(sandbox.Paths);
        var executor = new AiToolExecutor(sandbox.Paths, turn, launcher, async (_, _, ct, _) =>
        {
            dispatched++;
            await File.WriteAllTextAsync(file, "unexpected", ct);
            return new JsonObject { ["status"] = "succeeded" };
        });
        var failure = await Assert.ThrowsAsync<LauncherOperationException>(async () =>
            await executor.InvokeAsync("execute", new JsonObject { ["arguments"] = new JsonArray("config", "set", "playerName", "changed") },
                Guid.NewGuid().ToString("N"), "command", (_, _, _) => Task.CompletedTask, TestContext.CancellationToken));
        Assert.AreEqual(code, failure.Code);
        Assert.AreEqual(0, dispatched);
        Assert.IsFalse(File.Exists(file));
    }

    /// <summary>
    /// Safe mode requires a stored decision for a broad command. A named-tool grant permits later calls,
    /// and a repeated goal reuses the journal result instead of writing twice.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task SafeCommand_UsesToolDecision_AndRecordedResultIsNotReplayed()
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        conversation["mode"] = "safe";
        await sandbox.Store.SaveConversationAsync(conversation, TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        turn["mode"] = "safe";
        await sandbox.Store.SaveTurnAsync(turn, TestContext.CancellationToken);
        var file = Path.Combine(sandbox.Root, "effect.txt");
        var dispatched = 0;
        var decisions = 0;
        var launcher = new AiCoreTools(sandbox.Paths);
        var executor = new AiToolExecutor(sandbox.Paths, turn, launcher, async (_, _, ct, _) =>
        {
            dispatched++;
            await File.WriteAllTextAsync(file, "one real effect", ct);
            return new JsonObject { ["status"] = "succeeded", ["data"] = new JsonObject { ["saved"] = true } };
        });
        async Task Decide(string kind, string? invocation, JsonObject detail)
        {
            if (kind != "confirmation_required") return;
            Assert.AreEqual(0, dispatched);
            Assert.IsFalse(File.Exists(file));
            decisions++;
            await new AiToolApprovals(sandbox.Paths).DecideAsync(new AiToolConfirmation
            {
                ConversationId = conversation["id"]!.ToString(),
                TurnId = turn["id"]!.ToString(),
                InvocationId = invocation!,
                Fingerprint = detail["fingerprint"]!.ToString(),
                Approve = true,
                Scope = "tool"
            }, TestContext.CancellationToken);
        }

        var arguments = new JsonObject { ["arguments"] = new JsonArray("config", "set", "playerName", "explicit") };
        await executor.InvokeAsync("execute", arguments.DeepClone().AsObject(), Guid.NewGuid().ToString("N"), "first", Decide, TestContext.CancellationToken);
        Assert.AreEqual("one real effect", await File.ReadAllTextAsync(file, TestContext.CancellationToken));
        await executor.InvokeAsync("execute", arguments.DeepClone().AsObject(), Guid.NewGuid().ToString("N"), "repeat", Decide, TestContext.CancellationToken);
        Assert.AreEqual(1, dispatched);
        Assert.AreEqual(1, decisions);
    }

    /// <summary>Gets or sets the deadline for real stored consent and effect records.</summary>
    public TestContext TestContext { get; set; }
}