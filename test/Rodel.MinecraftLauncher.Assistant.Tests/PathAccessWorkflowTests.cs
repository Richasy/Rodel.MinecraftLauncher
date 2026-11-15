using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Exercises the real durable host gate without asking a model to request permission.</summary>
[TestClass]
public sealed class PathAccessWorkflowTests
{
    /// <summary>Value/design review: existing root revocation tests do not exercise host-triggered consent or
    /// scoped conversation reuse. These variants wait at the persisted confirmation boundary, reject a stale
    /// fingerprint, then check real subsequent gates for sibling paths, prefix collisions, other conversations
    /// and write escalation. A callback fixture replaces only the caller after consent, not approval persistence.</summary>
    [TestMethod]
    [DataRow("once", true)]
    [DataRow("parent_folder", true)]
    [DataRow("all_paths", true)]
    [DataRow("parent_folder", false)]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task HostConsent_ResumesExactCall_AndLimitsReuseToSelectedScope(string scope, bool write)
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        var target = Path.Combine(sandbox.Root, "chosen", "source.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "unchanged source", TestContext.CancellationToken);
        var prompt = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new AiPathAccess(sandbox.Paths);
        var pending = gate.AuthorizeAsync(turn, Guid.NewGuid().ToString("N"), "read_file_lines", new JsonObject { ["path"] = target },
            target, write, _ => Task.CompletedTask, (kind, _, data) =>
            {
                if (kind == "confirmation_required") prompt.TrySetResult(data.DeepClone().AsObject());
                return Task.CompletedTask;
            }, TestContext.CancellationToken);
        var approval = await prompt.Task.WaitAsync(TestContext.CancellationToken);
        Assert.IsFalse(pending.IsCompleted);
        Assert.AreEqual(target, approval["pathAccess"]!["path"]!.ToString());
        var request = new AiToolConfirmation
        {
            ConversationId = conversation["id"]!.ToString(),
            TurnId = turn["id"]!.ToString(),
            InvocationId = approval["id"]!.ToString(),
            Fingerprint = approval["fingerprint"]!.ToString(),
            Approve = true,
            Scope = scope
        };
        var approvals = new AiToolApprovals(sandbox.Paths);
        await Assert.ThrowsAsync<LauncherOperationException>(() => approvals.DecideAsync(request with { Fingerprint = "stale" }, TestContext.CancellationToken));
        Assert.IsFalse(pending.IsCompleted);
        await approvals.DecideAsync(request, TestContext.CancellationToken);
        await pending;
        Assert.AreEqual("unchanged source", AiScopedFiles.ReadText(Path.GetDirectoryName(target)!, "source.txt"));

        await CheckNextGateAsync(gate, turn, target, write, scope != "once");
        await CheckNextGateAsync(gate, turn, Path.Combine(sandbox.Root, "chosen", "nested", "neighbor.txt"), write, scope != "once");
        await CheckNextGateAsync(gate, turn, Path.Combine(sandbox.Root, "chosen-other", "neighbor.txt"), write, scope == "all_paths");
        if (!write) await CheckNextGateAsync(gate, turn, target, true, false);
        var other = await sandbox.ConversationAsync(TestContext.CancellationToken);
        await CheckNextGateAsync(gate, await sandbox.TurnAsync(other, true, TestContext.CancellationToken), target, write, false);
    }

    /// <summary>A declined host prompt must return a recoverable tool denial, create no persistent grant and leave source bytes intact.</summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task DeclinedPath_DoesNotGrantOrDispatch()
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        var gate = new AiPathAccess(sandbox.Paths);
        var target = Path.Combine(sandbox.Root, "outside.txt");
        var call = gate.AuthorizeAsync(turn, Guid.NewGuid().ToString("N"), "read_file_lines", [], target, false,
            _ => Task.CompletedTask, async (kind, _, data) =>
            {
                if (kind != "confirmation_required") return;
                await new AiToolApprovals(sandbox.Paths).DecideAsync(new()
                {
                    ConversationId = conversation["id"]!.ToString(),
                    TurnId = turn["id"]!.ToString(),
                    InvocationId = data["id"]!.ToString(),
                    Fingerprint = data["fingerprint"]!.ToString(),
                    Approve = false
                }, TestContext.CancellationToken);
            }, TestContext.CancellationToken);
        var error = await Assert.ThrowsAsync<LauncherOperationException>(async () => await call);
        Assert.AreEqual("AI_CONFIRMATION_DECLINED", error.Code);
        Assert.IsFalse(gate.Allows(conversation["id"]!.ToString(), target, false));
        Assert.IsFalse(File.Exists(target));
    }

    private async Task CheckNextGateAsync(AiPathAccess gate, JsonObject turn, string target, bool write, bool allowed)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var prompt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = gate.AuthorizeAsync(turn, Guid.NewGuid().ToString("N"), "file", [], target, write, _ => Task.CompletedTask,
            (kind, _, _) => { if (kind == "confirmation_required") prompt.TrySetResult(); return Task.CompletedTask; }, cancellation.Token);
        var reached = await Task.WhenAny(call, prompt.Task).WaitAsync(TestContext.CancellationToken);
        Assert.AreEqual(allowed, reached == call, target);
        if (allowed)
        {
            await call;
        }
        else
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await call);
        }
    }

    /// <summary>Gets or sets the runner-owned deadline for durable consent waits.</summary>
    public TestContext TestContext { get; set; }
}