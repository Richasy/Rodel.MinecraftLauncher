using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Servers;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects the distinction between retained game logs and an observed game failure.</summary>
[TestClass]
public sealed class SessionDiagnosticTests
{
    /// <summary>Preparation failures already have original launcher evidence before Java exists. Real stored sessions must retain that evidence and its sanitized details through instance diagnostics and source-generated export, even with a misleading old game log present. This does not exercise the remote authentication service.</summary>
    [TestMethod]
    [DataRow("AUTH_TLS_ERROR", "authentication", "manage-accounts")]
    [DataRow("JAVA_NOT_FOUND", "java-discovery", "configure-instance")]
    [DataRow("IO_ERROR", "download", "view-details")]
    public async Task Diagnose_PreProcessFailure_PreservesLauncherSource(string code, string stage, string action)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var instance = await new InstanceStore(paths).CreateAsync("Preparation fixture", "1.20.1", TestContext.CancellationToken);
            var session = Guid.NewGuid().ToString("N");
            var retainedJobId = Guid.NewGuid().ToString("N");
            var log = Path.Combine(paths.LogsDirectory, "old-game.log");
            await File.WriteAllTextAsync(log, "[21:50:00] [Render thread/FATAL]: unrelated old game failure\n", TestContext.CancellationToken);
            var original = "Original " + code + " failure before Java started";
            var state = new JsonObject
            {
                ["id"] = session,
                ["jobId"] = retainedJobId,
                ["instanceId"] = instance.Id,
                ["status"] = "failed",
                ["gameDirectory"] = instance.GameDirectory,
                ["error"] = original,
                ["errorCode"] = code,
                ["errorDetails"] = new JsonObject { ["stage"] = stage, ["host"] = "user.auth.xboxlive.com" },
                ["stdoutLog"] = log
            };
            await File.WriteAllTextAsync(Path.Combine(paths.SessionsDirectory, session + ".json"), state.ToJsonString(), TestContext.CancellationToken);
            // The original task may have been pruned; its matching ID must not make readable session evidence unavailable.
            var diagnosis = (SessionDiagnosis)await new MaintenanceService(paths).DiagnoseLaunchAsync(instance.Id, session, retainedJobId, TestContext.CancellationToken);
            Assert.AreEqual(original, diagnosis.Problem?.ReportedError?.Message);
            Assert.AreEqual(code, diagnosis.Problem?.Code);
            Assert.AreEqual(stage, diagnosis.Problem?.Phase);
            Assert.AreEqual("session-error", diagnosis.Problem?.Source);
            Assert.AreEqual(action, diagnosis.Problem?.Action);
            Assert.IsEmpty(diagnosis.ReportedErrors);
            Assert.IsEmpty(diagnosis.CrashReports);
            var exported = JsonSerializer.SerializeToNode<ILaunchDiagnosis>(diagnosis, DiagnosticJsonContext.Default.ILaunchDiagnosis)!;
            Assert.AreEqual("user.auth.xboxlive.com", exported["preparationFailure"]?["errorDetails"]?["host"]?.ToString());
        });
    }

    /// <summary>A task can fail before creating a session and may only retain Error. Its actual message must survive without borrowing another attempt's session, while explicit mismatched job/session IDs must be rejected.</summary>
    [TestMethod]
    public async Task Diagnose_TaskOnlyFailureAndMismatchedAttempt_KeepEvidenceBoundToTheAttempt()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var instance = await new InstanceStore(paths).CreateAsync("Task fixture", "1.20.1", TestContext.CancellationToken);
            var job = Guid.NewGuid().ToString("N");
            var record = new JsonObject
            {
                ["id"] = job,
                ["status"] = "failed",
                ["error"] = "Original task submission failure",
                ["request"] = new JsonObject { ["operation"] = "launcher-task", ["parameters"] = new JsonObject { ["instanceId"] = instance.Id, ["kind"] = "launch" } }
            };
            await File.WriteAllTextAsync(Path.Combine(paths.JobsDirectory, job + ".json"), record.ToJsonString(), TestContext.CancellationToken);
            var maintenance = new MaintenanceService(paths);
            var diagnosis = await maintenance.DiagnoseLaunchAsync(instance.Id, jobId: job, ct: TestContext.CancellationToken);
            Assert.AreEqual("Original task submission failure", diagnosis.Problem?.ReportedError?.Message);
            Assert.AreEqual("job-error", diagnosis.Problem?.Source);
            var session = Guid.NewGuid().ToString("N");
            var state = new JsonObject { ["id"] = session, ["instanceId"] = instance.Id, ["status"] = "exited", ["jobId"] = Guid.NewGuid().ToString("N") };
            await File.WriteAllTextAsync(Path.Combine(paths.SessionsDirectory, session + ".json"), state.ToJsonString(), TestContext.CancellationToken);
            await Assert.ThrowsAsync<ArgumentException>(() => maintenance.DiagnoseLaunchAsync(instance.Id, session, job, TestContext.CancellationToken));
        });
    }

    /// <summary>
    /// Real retained-log parsing must keep optional-service errors without promoting them during normal play or exit.
    /// Existing process/window cases do not exercise diagnostic promotion. The same independent log fixture with a
    /// nonzero exit must remain actionable, distinguishing lifecycle evidence from an error-message blacklist.
    /// </summary>
    [TestMethod]
    [DataRow("exited", 0, false)]
    [DataRow("exited", 1, true)]
    [DataRow("running", 0, false)]
    public async Task Diagnose_BackgroundError_RequiresAbnormalExit(string status, int exitCode, bool expectedError)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var session = Guid.NewGuid().ToString("N");
            var log = Path.Combine(paths.LogsDirectory, "game.log");
            await File.WriteAllTextAsync(log, "[21:50:00] [IO-Worker-1/ERROR] [RealmsClient]: Couldn't connect to realms\njava.lang.RuntimeException: Failed to parse into SignedJWT: 0\n", TestContext.CancellationToken);
            var state = new JsonObject
            {
                ["id"] = session,
                ["status"] = status,
                ["exitCode"] = exitCode,
                ["stdoutLog"] = log,
            };
            await File.WriteAllTextAsync(Path.Combine(paths.SessionsDirectory, session + ".json"), state.ToJsonString(), TestContext.CancellationToken);
            var diagnosis = await new MaintenanceService(paths).DiagnoseSessionAsync(session, TestContext.CancellationToken);
            Assert.HasCount(1, diagnosis.ReportedErrors);
            Assert.Contains("Couldn't connect to realms", diagnosis.ReportedErrors[0].Message!);
            Assert.AreEqual(expectedError, diagnosis.Problem?.ReportedError is not null);
            Assert.IsNull(diagnosis.Problem?.SessionReportedError);
            Assert.Contains(line => line.Contains("SignedJWT", StringComparison.Ordinal),
                diagnosis.LogExcerpt!.Stdout.Concat(diagnosis.LogExcerpt.Stderr));
        });
    }

    /// <summary>A genuine connection rejection remains visible even when Minecraft itself exits successfully.</summary>
    [TestMethod]
    public void Describe_ConnectionFailed_PreservesOriginalReasonAfterNormalExit()
    {
        var problem = LaunchProblem.Describe(new SessionDiagnosis("fixture-session", "exited", 0,
            [], [], [], [], new Dictionary<string, LogStreamSummary>())
        {
            ServerConnection = new ServerConnectionReport
            {
                Server = "server.example:25565",
                Observation = "connect_failed",
                IssueCode = "whitelist",
                Evidence = "You are not whitelisted on this server!",
            },
        });
        Assert.AreEqual("SERVER_WHITELIST", problem.Code);
        Assert.AreEqual("You are not whitelisted on this server!", problem.ReportedError?.Message);
    }

    /// <summary>Gets cancellation for file I/O owned by the test runner.</summary>
    public TestContext TestContext { get; set; } = null!;
}