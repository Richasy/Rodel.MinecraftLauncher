using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects incident evidence and reversible trial effects in real Core storage; neighboring session tests cover log classification.</summary>
[TestClass]
public sealed class AiRepairTests
{
    /// <summary>Review: a repair must retain its original evidence and reject drift before writing. Existing settings tests do not
    /// cross incident planning. Independent file edits distinguish frozen evidence from re-reading "latest" and stale writes from success.</summary>
    [TestMethod]
    public async Task Incident_ReentryRetainsEvidence_StalePlanPreservesNewSettings()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var client = new LauncherClient(paths);
            var (instance, session, log) = await FailureAsync(client);
            var request = new AiRepairCreate { InstanceId = instance, SessionId = session };
            var original = await client.Repairs.CreateAsync(request, TestContext.CancellationToken);
            await File.WriteAllTextAsync(log, "later unrelated log", TestContext.CancellationToken);
            var reopened = await client.Repairs.CreateAsync(request, TestContext.CancellationToken);
            Assert.IsTrue(JsonNode.DeepEquals(original.OriginalDiagnosis, reopened.OriginalDiagnosis));
            Assert.AreEqual(original.Id, reopened.Id);
            var plan = await client.Repairs.PlanAsync(new() { RepairId = original.Id!, Kind = "memory", MemoryMb = 4096, Reason = "Observed OutOfMemoryError" }, TestContext.CancellationToken);
            await client.Settings.SetAsync("maxMemoryMb", JsonValue.Create(3072), client.Instances.Get(instance), TestContext.CancellationToken);
            var error = await Assert.ThrowsExactlyAsync<LauncherOperationException>(() => client.Repairs.ApplyAsync(new() { RepairId = original.Id!, PlanId = plan.Id! }, TestContext.CancellationToken));
            Assert.AreEqual("REPAIR_PLAN_STALE", error.Code);
            Assert.AreEqual(3072, client.Settings.GetEffective(client.Instances.Get(instance)).Values["maxMemoryMb"]!.GetValue<int>());
            var other = await client.Instances.CreateAsync("Other", "fixture", TestContext.CancellationToken);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.Repairs.CreateAsync(new() { InstanceId = other.Id, SessionId = session }, TestContext.CancellationToken));
        });
    }

    /// <summary>Review: rollback must preserve intervening player edits, including unrelated settings, and restore inheritance rather than
    /// pinning an effective default. A replayed consumed plan or whole-settings snapshot overwrite would fail these durable-state checks.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MemoryTrial_ReplayDoesNotWrite_RollbackPreservesPlayerEditsAndInheritance(bool customJvmHeap)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var client = new LauncherClient(paths); var (instance, session, _) = await FailureAsync(client);
            if (customJvmHeap) await client.Settings.SetAsync("jvmArguments", new JsonArray("-Xmx2048M", "-Dfixture=keep"), client.Instances.Get(instance), TestContext.CancellationToken);
            var repair = await client.Repairs.CreateAsync(new() { InstanceId = instance, SessionId = session }, TestContext.CancellationToken);
            var plan = await client.Repairs.PlanAsync(new() { RepairId = repair.Id!, Kind = "memory", MemoryMb = 4096, Reason = "Observed heap exhaustion" }, TestContext.CancellationToken);
            var action = new AiRepairAction { RepairId = repair.Id!, PlanId = plan.Id! };
            await client.Repairs.ApplyAsync(action, TestContext.CancellationToken);
            if (customJvmHeap) Assert.AreEqual("-Xmx4096M", client.Settings.GetOverrides(client.Instances.Get(instance))["jvmArguments"]![0]!.ToString());
            await client.Settings.UpdateAsync(new() { ["maxMemoryMb"] = 5120, ["playerNote"] = "keep" }, instance: client.Instances.Get(instance), cancellationToken: TestContext.CancellationToken);
            await client.Repairs.ApplyAsync(action, TestContext.CancellationToken);
            Assert.AreEqual(5120, client.Settings.GetOverrides(client.Instances.Get(instance))["maxMemoryMb"]!.GetValue<int>());
            var error = await Assert.ThrowsExactlyAsync<LauncherOperationException>(() => client.Repairs.RestoreAsync(action, TestContext.CancellationToken));
            Assert.AreEqual("REPAIR_ROLLBACK_CONFLICT", error.Code);
            await client.Settings.SetAsync("maxMemoryMb", JsonValue.Create(4096), client.Instances.Get(instance), TestContext.CancellationToken);
            await client.Repairs.RestoreAsync(action, TestContext.CancellationToken);
            var restored = client.Settings.GetOverrides(client.Instances.Get(instance));
            Assert.IsFalse(restored.ContainsKey("maxMemoryMb"));
            Assert.IsFalse(restored.ContainsKey("memoryMode"));
            Assert.AreEqual("keep", restored["playerNote"]!.ToString());
            if (customJvmHeap) Assert.IsTrue(JsonNode.DeepEquals(new JsonArray("-Xmx2048M", "-Dfixture=keep"), restored["jvmArguments"]));
        });
    }

    /// <summary>Review: the new verification run must use real launch planning/session creation, then automatically roll back the same failure.
    /// Only the game monitor is replaced; Java is a reporting subprocess. Repeating verification must not start a second game or mutate the
    /// preserved origin. This crosses a boundary not covered by settings/session-only cases and has an explicit monitor checkpoint, no sleeps.</summary>
    [TestMethod]
    public async Task Verification_OriginalFailure_RestoresTrialAndNeverLaunchesTwice()
    {
        using var java = new JavaProbeFixture(); java.Paths.EnsureDirectories();
        var host = new FailedGameHost(); var client = new LauncherClient(java.Paths, host);
        var (instance, session, _) = await FailureAsync(client);
        var version = Path.Combine(client.Instances.Get(instance).MinecraftDirectory, "versions", "fixture");
        Directory.CreateDirectory(version);
        await File.WriteAllTextAsync(Path.Combine(version, "fixture.json"), """{"id":"fixture","mainClass":"fixture.Main","libraries":[],"minecraftArguments":""}""", TestContext.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(version, "fixture.jar"), [], TestContext.CancellationToken);
        await client.Settings.SetAsync("javaPath", JsonValue.Create(java.Executable), client.Instances.Get(instance), TestContext.CancellationToken);
        await client.Settings.SetAsync("gameArguments", new JsonArray("--quickPlaySingleplayer", "player-world", "--demo"), client.Instances.Get(instance), TestContext.CancellationToken);
        var repair = await client.Repairs.CreateAsync(new() { InstanceId = instance, SessionId = session }, TestContext.CancellationToken);
        var plan = await client.Repairs.PlanAsync(new() { RepairId = repair.Id!, Kind = "memory", MemoryMb = 4096, Reason = "Observed heap exhaustion" }, TestContext.CancellationToken);
        var action = new AiRepairAction { RepairId = repair.Id!, PlanId = plan.Id! };
        await client.Repairs.ApplyAsync(action, TestContext.CancellationToken);
        var verified = await client.Repairs.VerifyAsync(action, TestContext.CancellationToken);
        Assert.AreEqual("original_failure", verified.Outcome, verified.ToString());
        Assert.AreEqual("restored", verified.State);
        Assert.IsNotNull(verified.VerificationSessionId);
        Assert.AreNotEqual(session, verified.VerificationSessionId);
        Assert.IsFalse(client.Settings.GetOverrides(client.Instances.Get(instance)).ContainsKey("maxMemoryMb"));
        await client.Repairs.VerifyAsync(action, TestContext.CancellationToken);
        Assert.AreEqual(1, host.Starts);
        Assert.DoesNotContain("player-world", host.Arguments);
        Assert.DoesNotContain("--quickPlaySingleplayer", host.Arguments);
        Assert.Contains("--demo", host.Arguments);
        Assert.AreEqual(session, client.Repairs.Read(action.RepairId).OriginSessionId);
    }

    private async Task<(string Instance, string Session, string Log)> FailureAsync(LauncherClient client)
    {
        var instance = await client.Instances.CreateAsync("Repair fixture", "fixture", TestContext.CancellationToken);
        var session = Guid.NewGuid().ToString("N"); var log = Path.Combine(client.Paths.LogsDirectory, session + ".log");
        await File.WriteAllTextAsync(log, "[12:00:00] [Render thread/FATAL]: java.lang.OutOfMemoryError: Java heap space", TestContext.CancellationToken);
        await JsonFiles.WriteAsync(Path.Combine(client.Paths.SessionsDirectory, session + ".json"), new JsonObject
        {
            ["id"] = session,
            ["instanceId"] = instance.Id,
            ["gameDirectory"] = instance.GameDirectory,
            ["status"] = "exited",
            ["exitCode"] = 1,
            ["stdoutLog"] = log
        }, TestContext.CancellationToken);
        return (instance.Id, session, log);
    }

    private sealed class FailedGameHost : ISessionHost
    {
        internal int Starts { get; private set; }
        internal string Arguments { get; private set; } = string.Empty;
        /// <inheritdoc />
        public async Task StartAsync(LauncherPaths paths, string sessionId, CancellationToken cancellationToken = default)
        {
            Starts++;
            var path = Path.Combine(paths.SessionsDirectory, sessionId + ".json"); var record = JsonFiles.ReadObject(path);
            Arguments = record["arguments"]!.ToJsonString();
            var log = Path.Combine(paths.LogsDirectory, sessionId + ".log");
            await File.WriteAllTextAsync(log, "[12:01:00] [Render thread/FATAL]: java.lang.OutOfMemoryError: Java heap space", cancellationToken);
            record["status"] = "exited"; record["exitCode"] = 1; record["stdoutLog"] = log;
            await JsonFiles.WriteAsync(path, record, cancellationToken);
        }
    }

    /// <summary>Gets cancellation for this test's isolated file and process work.</summary>
    public TestContext TestContext { get; set; } = null!;
}