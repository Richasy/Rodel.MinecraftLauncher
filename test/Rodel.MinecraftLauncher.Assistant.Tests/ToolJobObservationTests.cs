using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tools;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects job observation ownership and lossless nested progress evidence.</summary>
[TestClass]
public sealed class ToolJobObservationTests
{
    /// <summary>
    /// Author review: the direct Core adapter must distinguish reading a failed job from failing to read it.
    /// The existing polling cases substitute dispatch and cannot detect interpreting a job's string error as
    /// an operation error envelope. Real durable failure data must remain readable for recovery decisions.
    /// </summary>
    [TestMethod]
    public async Task DirectJobRead_PreservesFailureEvidence_AsASuccessfulObservation()
    {
        using var sandbox = new AiSandbox();
        var client = new LauncherClient(sandbox.Paths);
        var job = await client.Jobs.CreateAsync(new("fixture", []), ct: TestContext.CancellationToken);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.Jobs.RunAsync(job.Id!, (_, _) => throw new InvalidOperationException("Controlled job failure."), TestContext.CancellationToken));
        var observed = await new LauncherToolService(client).InvokeAsync("job_show", new() { ["jobId"] = job.Id }, TestContext.CancellationToken);
        Assert.AreEqual("succeeded", observed["status"]!.ToString(), observed.ToJsonString());
        Assert.AreEqual("failed", observed["data"]!["status"]!.ToString());
        Assert.AreEqual("Controlled job failure.", observed["data"]!["error"]!.ToString());
        Assert.IsNull(observed["error"]);
    }

    /// <summary>
    /// Review: the existing running-job case protects polling ownership, not phase-specific JSON presence.
    /// AI and preparation-diagnostic contexts must reuse application progress metadata; their default generated
    /// nested metadata would invent null fields or discard original values. Independent persisted fixtures
    /// distinguish omitted totals from explicit unknown totals and retain unfamiliar/noncanonical fields.
    /// An explicitly unknown overall percent must also survive these nested converters without a duplicate key.
    /// </summary>
    /// <param name="sourceJson">An independent persisted progress fixture, not output from the serializer under test.</param>
    [TestMethod]
    [DataRow("""{"stage":"preparing"}""")]
    [DataRow("""{"stage":"download","bytes":7,"totalBytes":null,"overallPercent":null,"displayName":null,"future":{"keep":true},"completedFiles":"unavailable"}""")]
    public void NestedProgress_UsesApplicationMetadataWithoutInventingFields(string sourceJson)
    {
        var source = JsonNode.Parse(sourceJson)!.AsObject();
        var progress = LauncherProgress.Read(source);
        var observation = new AiJobObservation
        {
            Id = "job-fixture",
            Status = "running",
            Read = new(new("job-fixture")),
            Progress = progress
        };
        var observed = JsonSerializer.SerializeToNode(observation, AiToolObservationJsonContext.Default.AiJobObservation)!;
        Assert.IsTrue(JsonNode.DeepEquals(source, observed["progress"]));

        var diagnosis = new PreparationDiagnosis("instance-fixture",
            new JobStatus { Id = "job-fixture", Status = "running", Progress = progress }, false, [],
            new LaunchDiagnosisProblem("UNCLASSIFIED", null, "view-details") { Phase = "unconfirmed", Source = "none" });
        var diagnostic = JsonSerializer.SerializeToNode<ILaunchDiagnosis>(diagnosis, DiagnosticJsonContext.Default.ILaunchDiagnosis)!;
        Assert.IsTrue(JsonNode.DeepEquals(source, diagnostic["job"]!["progress"]));
    }

    /// <summary>
    /// A deliberately still-running persisted job cancels at the first unwanted observation checkpoint.
    /// The fixed read must return its snapshot without polling or stopping that job. Only MCP transport is substituted.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task JobShow_ReturnsRunningSnapshot_WithoutWaitingForCompletion()
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        var id = Guid.NewGuid().ToString("N");
        await JsonFiles.WriteAsync(Path.Combine(sandbox.Paths.JobsDirectory, id + ".json"), new JsonObject
        {
            ["id"] = id,
            ["status"] = "running",
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["request"] = new JsonObject { ["operation"] = "launcher-task", ["parameters"] = new JsonObject { ["kind"] = "apply-content-review" } }
        }, TestContext.CancellationToken);
        var client = new LauncherClient(sandbox.Paths);
        var progressEvents = 0;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        Task Emit(string kind, string? step, JsonObject detail)
        {
            if (kind == "job_progress")
            {
                progressEvents++;
                deadline.Cancel();
            }

            return Task.CompletedTask;
        }

        Task<JsonObject> Read(string name, JsonObject input, CancellationToken ct, string? requestId) => Task.FromResult(new JsonObject
        {
            ["status"] = "succeeded",
            ["data"] = JsonSerializer.SerializeToNode(client.Jobs.Get(id), ApplicationJsonContext.Job)
        });
        var launcher = new AiCoreTools(sandbox.Paths);
        var result = await new AiToolExecutor(sandbox.Paths, turn, launcher, Read).InvokeAsync("job_show",
            new JsonObject { ["jobId"] = id }, Guid.NewGuid().ToString("N"), "snapshot", Emit, deadline.Token);
        Assert.AreEqual("running", result["data"]!["status"]!.ToString());
        Assert.AreEqual("running", client.Jobs.Get(id).Status);
        Assert.AreEqual(0, progressEvents);
        Assert.IsNull(result["jobObservation"]);
    }

    /// <summary>
    /// A live job can outlast the AI's remaining execution budget without canceling its observer. The real executor's
    /// wait events drive the same deadline handler as the runner; only MCP dispatch is substituted. A start barrier
    /// proves the timed interval belongs to observation, and explicit stop must leave the independent job untouched.
    /// Unlike the read-only snapshot case, this protects an admitted mutation and restoration of its execution budget.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task MutationWait_ExcludesJobTime_AndPreservesCallerCancellation(bool cancel)
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        var id = Guid.NewGuid().ToString("N");
        await WriteJobAsync(sandbox, id, "running", true, TestContext.CancellationToken);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        using var deadline = new AiTurnDeadline(TimeSpan.FromMilliseconds(500), caller.Token);
        var dispatches = 0;
        var finishedWaits = 0;
        Task Emit(string kind, string? step, JsonObject detail)
        {
            deadline.ObserveWaitEvent(kind, step);
            if (kind == "job_wait_started") started.TrySetResult();
            if (kind == "job_wait_finished") finishedWaits++;
            return Task.CompletedTask;
        }

        Task<JsonObject> Dispatch(string name, JsonObject input, CancellationToken ct, string? requestId)
        {
            dispatches++;
            return Task.FromResult(new JsonObject { ["status"] = "succeeded", ["data"] = new JsonObject { ["jobId"] = id } });
        }

        var launcher = new AiCoreTools(sandbox.Paths);
        var execution = new AiToolExecutor(sandbox.Paths, turn, launcher, Dispatch).InvokeAsync("config_set",
            new JsonObject { ["key"] = "maxMemoryMb", ["value"] = 1024 }, Guid.NewGuid().ToString("N"), "mutation", Emit, deadline.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.CancellationToken);
            await Task.Delay(650, TestContext.CancellationToken);
            Assert.IsFalse(deadline.IsCancellationRequested, "Independent work must not consume the AI execution budget.");
            Assert.IsFalse(execution.IsCompleted, "The observer must remain attached to the known live job.");
            if (cancel)
            {
                await caller.CancelAsync();
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await execution);
                Assert.AreEqual("running", new LauncherClient(sandbox.Paths).Jobs.Get(id).Status);
            }
            else
            {
                await WriteJobAsync(sandbox, id, "succeeded", true, TestContext.CancellationToken);
                var result = await execution.WaitAsync(TimeSpan.FromSeconds(3), TestContext.CancellationToken);
                Assert.AreEqual("succeeded", result["jobObservation"]!["status"]!.ToString());
                await Assert.ThrowsAsync<TaskCanceledException>(async () =>
                    await Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token).WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationToken));
            }

            Assert.AreEqual(1, dispatches, "Observation must never replay the admitted mutation.");
            Assert.AreEqual(1, finishedWaits, "Both completion and stop must close the execution-budget pause.");
        }
        finally
        {
            await caller.CancelAsync();
            try { await execution; }
            catch (OperationCanceledException) { }
        }
    }

    /// <summary>
    /// Excluding background time must not turn an unobservable owner into an endless wait or a fake success.
    /// The persisted job deliberately has no process identity; the result must retain that uncertainty for inspection.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task UnobservableMutationJob_ReturnsUncertainty_WithoutWaitingOrReplay()
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        var id = Guid.NewGuid().ToString("N");
        await WriteJobAsync(sandbox, id, "running", false, TestContext.CancellationToken);
        var waits = 0;
        var dispatches = 0;
        Task Emit(string kind, string? step, JsonObject detail)
        {
            if (kind == "job_wait_started") waits++;
            return Task.CompletedTask;
        }

        Task<JsonObject> Dispatch(string name, JsonObject input, CancellationToken ct, string? requestId)
        {
            dispatches++;
            return Task.FromResult(new JsonObject { ["status"] = "succeeded", ["data"] = new JsonObject { ["jobId"] = id } });
        }

        var launcher = new AiCoreTools(sandbox.Paths);
        var result = await new AiToolExecutor(sandbox.Paths, turn, launcher, Dispatch).InvokeAsync("config_set",
            new JsonObject { ["key"] = "maxMemoryMb", ["value"] = 1024 }, Guid.NewGuid().ToString("N"), "mutation", Emit, TestContext.CancellationToken);
        Assert.IsTrue(result["jobObservation"]!["activityUnknown"]!.GetValue<bool>());
        Assert.AreEqual("running", result["jobObservation"]!["status"]!.ToString());
        Assert.IsNotNull(result["jobObservation"]!["ownerWarning"]);
        Assert.AreEqual(0, waits);
        Assert.AreEqual(1, dispatches);
    }

    private static Task WriteJobAsync(AiSandbox sandbox, string id, string status, bool knownOwner, CancellationToken ct)
    {
        using var owner = Process.GetCurrentProcess();
        return JsonFiles.WriteAsync(Path.Combine(sandbox.Paths.JobsDirectory, id + ".json"), new JsonObject
        {
            ["id"] = id,
            ["status"] = status,
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["processId"] = knownOwner ? owner.Id : null,
            ["processStartUtc"] = knownOwner ? owner.StartTime.ToUniversalTime().ToString("O") : null,
            ["request"] = new JsonObject { ["operation"] = "launcher-task", ["parameters"] = new JsonObject { ["kind"] = "apply-content-review" } }
        }, ct);
    }

    /// <summary>Gets or sets the cancellation token used for this case's isolated state and executor.</summary>
    public TestContext TestContext { get; set; }
}