using Rodel.MinecraftLauncher.Tests;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Migrates client-exit cancellation from the legacy process probe; real CLI/Core process ownership is the subject.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class AssistantOwnershipTests
{
    /// <summary>
    /// Killing only a waiting --cancel-on-exit client must cancel its active turn without terminating the independently hosted Core.
    /// A durable text event establishes in-flight generation before the owned client is killed; no sleep infers readiness.
    /// </summary>
    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task OwnedClientExit_CancelsItsStreamingTurn_WithoutStoppingCoreHost()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            await ModelEndpoint.SseAsync(context,
            [
                new JsonObject { ["choices"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["index"] = 0, ["delta"] = new JsonObject { ["content"] = new string('看', 600) }
                }) }.ToJsonString()
            ]);
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        }, TestContext.CancellationToken);
        await using var session = await AssistantCliSession.StartAsync();
        await session.ConfigureAsync(endpoint.Endpoint);
        var conversation = (await session.Cli.RunAsync("ai", "chat", "create")).ReadEnvelope(0)["data"]!["id"]!.ToString();
        var idleHostId = (await session.Cli.RunAsync("ai", "host")).ReadEnvelope(0)["data"]!["processId"]!.GetValue<int>();
        // Force send to start its own background host: an idle PID observed before submission is not the turn's execution owner.
        using (var idleHost = Process.GetProcessById(idleHostId))
        {
            idleHost.Kill(entireProcessTree: true);
            await idleHost.WaitForExitAsync(TestContext.CancellationToken);
        }

        using var client = Process.Start(session.Cli.CreateStartInfo("ai", "chat", "send", conversation, "--expected-revision", "1",
            "--prompt", "Cancel with this client.", "--wait", "--cancel-on-exit", "--no-tools"))!;
        var stdout = client.StandardOutput.ReadToEndAsync(TestContext.CancellationToken);
        var stderr = client.StandardError.ReadToEndAsync(TestContext.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TestContext.CancellationToken);
            var shown = (await session.Cli.RunAsync("ai", "chat", "show", conversation)).ReadEnvelope(0)["data"]!;
            var turn = shown["turns"]![0]!;
            Assert.AreEqual("client_owned", turn["ownership"]!.ToString());
            var id = turn["id"]!.ToString();
            await session.EventAsync(conversation, id, "text_delta", TestContext.CancellationToken);
            var executingHostId = turn["runtimeProcessId"]!.GetValue<int>();
            Assert.AreNotEqual(idleHostId, executingHostId);
            // The contract is termination of this client, not termination of its independently owned Core descendants.
            client.Kill();
            await client.WaitForExitAsync(TestContext.CancellationToken);
            var canceled = (await session.Cli.RunAsync("ai", "chat", "turn", "wait", conversation, id, "--timeout-seconds", "10")).ReadEnvelope(3)["data"]!;
            Assert.AreEqual("canceled", canceled["status"]!.ToString());
            var current = (await session.Cli.RunAsync("ai", "host")).ReadEnvelope(0)["data"]!;
            Assert.AreEqual(executingHostId, current["processId"]!.GetValue<int>(), "Canceling a client-owned turn must not replace or stop Core.");
        }
        finally
        {
            if (!client.HasExited) client.Kill();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await client.WaitForExitAsync(cleanup.Token);
            await Task.WhenAll(stdout, stderr);
        }
    }

    /// <summary>
    /// The Core host may legitimately restart after idle expiry; cleanup must own the current process for this unique root, not a cached startup PID.
    /// Explicitly terminating and replacing the first host reaches that boundary without a five-second sleep or relying on scheduler load.
    /// </summary>
    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task ReplacedIdleHost_SessionCleanupStopsCurrentOwner_AndRemovesItsRoot()
    {
        var session = await AssistantCliSession.StartAsync();
        Process? replacement = null;
        try
        {
            await using (session)
            {
                var firstState = (await session.Cli.RunAsync("ai", "host")).ReadEnvelope(0)["data"]!;
                using var first = Process.GetProcessById(firstState["processId"]!.GetValue<int>());
                first.Kill(entireProcessTree: true);
                await first.WaitForExitAsync(TestContext.CancellationToken);
                var next = (await session.Cli.RunAsync("ai", "host")).ReadEnvelope(0)["data"]!;
                Assert.AreNotEqual(first.Id, next["processId"]!.GetValue<int>());
                replacement = Process.GetProcessById(next["processId"]!.GetValue<int>());
            }

            Assert.IsTrue(replacement.HasExited);
            Assert.IsFalse(Directory.Exists(session.Cli.Root));
        }
        finally
        {
            if (replacement is not null)
            {
                if (!replacement.HasExited)
                {
                    replacement.Kill(entireProcessTree: true);
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await replacement.WaitForExitAsync(cleanup.Token);
                }

                replacement.Dispose();
            }

            session.Cli.Dispose();
        }
    }

    /// <summary>Gets or sets the deadline for the exact processes owned by this scenario.</summary>
    public TestContext TestContext { get; set; }
}