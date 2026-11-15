using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// Verifies persisted command execution and lifecycle boundaries, not download-specific recovery policies.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class JobWorkflowTests
{
    /// <summary>
    /// The submitting process must return while a controlled write barrier still blocks the job;
    /// releasing it must preserve literal tokens and allow the detached host to finish.
    /// </summary>
    [TestMethod]
    [DataRow("--background")]
    [DataRow("\u6e38\u620f \"quoted\" C:\\fixture folder")]
    public async Task BackgroundConfiguration_ParentHasExited_PreservesLiteralAndCompletes(string literal)
    {
        using var cli = new CliSandbox();
        (await cli.RunAsync("config", "set", "backgroundLiteral", "before-job")).ReadEnvelope(0);
        var settingsFile = Path.Combine(cli.Root, "settings.json");
        var original = await File.ReadAllBytesAsync(settingsFile, TestContext.CancellationToken);

        // Only coordination uses Core directly; submission and execution still go through actual CLI processes.
        using var barrier = await ResourceLease.AcquireAsync(settingsFile, TestContext.CancellationToken);
        await using var job = await JobHost.StartAsync(cli, "config", "set", "--", "backgroundLiteral", literal);
        var waiting = (await cli.RunAsync("job", "show", job.Id)).ReadEnvelope(0);
        Assert.IsTrue(waiting["data"]?["status"]?.GetValue<string>() is "queued" or "running", "The parent must return before the blocked write can finish.");
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(settingsFile, TestContext.CancellationToken));

        barrier.Dispose();
        var terminal = await job.WaitForTerminalAsync();
        var read = (await cli.RunAsync("config", "get", "backgroundLiteral")).ReadEnvelope(0);

        Assert.AreEqual("succeeded", terminal["status"]?.GetValue<string>(), terminal.ToJsonString());
        Assert.AreEqual("command", terminal["request"]?["operation"]?.GetValue<string>());
        CollectionAssert.Contains(terminal["request"]!["parameters"]!["arguments"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray(), literal);
        Assert.AreEqual(literal, read["data"]?["value"]?.GetValue<string>());
        Assert.AreEqual(literal, terminal["result"]?["value"]?.GetValue<string>());
    }

    /// <summary>
    /// Canceling or resuming a completed job must not replay the mutation or create a second job.
    /// </summary>
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task CompletedJob_CancelAndResume_PreservesTerminalStateWithoutReplay()
    {
        using var cli = new CliSandbox();
        await using var job = await JobHost.StartAsync(cli, "config", "set", "payload", "written-once");
        var terminal = await job.WaitForTerminalAsync();
        Assert.AreEqual("succeeded", terminal["status"]?.GetValue<string>(), terminal.ToJsonString());
        var jobFile = Path.Combine(cli.Root, "jobs", job.Id + ".json");
        var original = await File.ReadAllBytesAsync(jobFile, TestContext.CancellationToken);

        // A replay would now fail to replace settings.json instead of silently rewriting the same value.
        using var locked = new FileStream(Path.Combine(cli.Root, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        var canceled = (await cli.RunAsync("job", "cancel", job.Id)).ReadEnvelope(0);
        var resumed = (await cli.RunAsync("job", "resume", job.Id)).ReadEnvelope(0);

        Assert.AreEqual("succeeded", canceled["data"]?["status"]?.GetValue<string>());
        Assert.AreEqual("succeeded", resumed["data"]?["status"]?.GetValue<string>());
        Assert.AreEqual(job.Id, resumed["data"]?["id"]?.GetValue<string>());
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(jobFile, TestContext.CancellationToken));
        Assert.HasCount(1, Directory.GetFiles(Path.Combine(cli.Root, "jobs"), "*.json"));
        Assert.IsFalse(File.Exists(jobFile + ".cancel"));
    }

    /// <summary>
    /// Cancellation persisted before dispatch must reach the real executor before any command side effect.
    /// </summary>
    [TestMethod]
    public async Task QueuedJob_CancelBeforeExecution_RecordsCanceledWithoutMutation()
    {
        using var cli = new CliSandbox();
        (await cli.RunAsync("config", "set", "payload", "unchanged")).ReadEnvelope(0);
        var original = await File.ReadAllBytesAsync(Path.Combine(cli.Root, "settings.json"), TestContext.CancellationToken);
        var id = await WriteQueuedRequestAsync(cli);
        var requested = (await cli.RunAsync("job", "cancel", id)).ReadEnvelope(0);
        Assert.IsTrue(requested["data"]?["cancelRequested"]?.GetValue<bool>());

        var execution = await cli.RunAsync("job-host", id);
        var terminal = (await cli.RunAsync("job", "show", id)).ReadEnvelope(0);

        Assert.AreEqual(1, execution.ExitCode, execution.Output + execution.Error);
        Assert.AreEqual("canceled", terminal["data"]?["status"]?.GetValue<string>());
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(Path.Combine(cli.Root, "settings.json"), TestContext.CancellationToken));
    }

    private static async Task<string> WriteQueuedRequestAsync(CliSandbox cli)
    {
        var id = Guid.NewGuid().ToString("N");
        var state = new JsonObject
        {
            ["id"] = id,
            ["status"] = "queued",
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["canResume"] = false,
            ["request"] = new JsonObject
            {
                ["operation"] = "command",
                ["parameters"] = new JsonObject { ["arguments"] = new JsonArray("config", "set", "--", "payload", "must-not-be-written") }
            }
        };
        Directory.CreateDirectory(Path.Combine(cli.Root, "jobs"));
        await File.WriteAllTextAsync(Path.Combine(cli.Root, "jobs", id + ".json"), state.ToJsonString());
        return id;
    }

    /// <summary>Gets or sets the MSTest context supplying this test's cancellation token.</summary>
    public TestContext TestContext { get; set; }
}