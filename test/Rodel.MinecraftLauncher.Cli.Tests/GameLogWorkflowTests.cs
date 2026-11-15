using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Exercises live console reads and exports across the real detached CLI monitor boundary.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class GameLogWorkflowTests
{
    /// <summary>
    /// File barriers keep a real producer alive while independent CLI processes read and export its memory.
    /// Normal exit and an explicit force-stop must leave no console captures; a nonzero exit must persist the same
    /// observed output for later diagnosis. This distinguishes memory-only behavior from delayed disk capture.
    /// </summary>
    [TestMethod]
    [DataRow("normal")]
    [DataRow("crash")]
    [DataRow("stop")]
    public async Task Console_LiveReadAndExport_SaveOnlyOnCrash(string finish)
    {
        using var cli = new CliSandbox();
        var java = new JavaProbeFixture(cli.Root);
        var instance = await CreateInstanceAsync(cli);
        var started = (await cli.RunAsync("launch", instance, "--offline", "--java", java.Executable)).ReadEnvelope(0)["data"]!;
        var session = started["id"]!.GetValue<string>();
        using var child = Process.GetProcessById(started["processId"]!.GetValue<int>());
        using var monitor = Process.GetProcessById(started["monitorId"]!.GetValue<int>());
        try
        {
            await WaitUntilAsync(() => File.Exists(Path.Combine(java.Home, "console-ready")));
            var logs = await ReadUntilAsync(cli, session, "synthetic-console-error");
            Assert.AreEqual("memory", logs["streams"]!["stdoutLog"]!["source"]!.ToString());
            AssertNoCaptures(cli);
            var exported = Path.Combine(cli.Root, "requested-export.zip");
            (await cli.RunAsync("logs", "export", session, exported)).ReadEnvelope(0);
            using (var archive = ZipFile.OpenRead(exported))
            {
                using var reader = new StreamReader(archive.GetEntry("stderrLog.txt")!.Open());
                Assert.Contains("synthetic-console-error", await reader.ReadToEndAsync(TestContext.CancellationToken));
                Assert.IsNotNull(archive.GetEntry("session.json"));
            }

            var bundle = Path.Combine(cli.Root, "requested-bundle.zip");
            (await cli.RunAsync("logs", "bundle", session, bundle)).ReadEnvelope(0);
            using (var archive = ZipFile.OpenRead(bundle))
            {
                using var reader = new StreamReader(archive.GetEntry("logs/stderrLog.log")!.Open());
                Assert.Contains("synthetic-console-error", await reader.ReadToEndAsync(TestContext.CancellationToken));
                Assert.IsNotNull(archive.GetEntry("manifest.json"));
            }

            AssertNoCaptures(cli);
            if (finish == "stop")
                (await cli.RunAsync("process", "stop", session, "--force")).ReadEnvelope(0);
            else
                await SignalExitAsync(java.Home, finish == "crash" ? "9" : "0", TestContext.CancellationToken);
            await child.WaitForExitAsync(TestContext.CancellationToken);
            if (finish != "stop") Assert.AreEqual(finish == "crash" ? 9 : 0, child.ExitCode, "The producer must observe the complete exit barrier.");
            await monitor.WaitForExitAsync(TestContext.CancellationToken);
            var ended = (await cli.RunAsync("logs", session)).ReadEnvelope(0)["data"]!;
            Assert.IsTrue(File.Exists(exported), "A user-requested export is not an expiring launcher capture.");
            if (finish == "crash")
            {
                Assert.AreEqual("saved", ended["streams"]!["stdoutLog"]!["source"]!.ToString());
                Assert.Contains("console-finished", ended.ToJsonString());
                Assert.Contains("synthetic-console-error", ended.ToJsonString());
                Assert.HasCount(2, Directory.GetFiles(Path.Combine(cli.Root, "logs"), "*.log"));
            }
            else
            {
                AssertNoCaptures(cli);
                Assert.AreEqual("unavailable", ended["streams"]!["stdoutLog"]!["source"]!.ToString());
                Assert.IsFalse(string.IsNullOrWhiteSpace(ended["streams"]!["stdoutLog"]!["warning"]?.ToString()));
            }
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await child.WaitForExitAsync(cleanup.Token);
            await monitor.WaitForExitAsync(cleanup.Token);
        }
    }

    /// <summary>
    /// A real read-only destination must not turn a game exit into monitor failure or erase the other stream.
    /// Successful crash exports cannot detect this persistence boundary; the existing bytes are the independent oracle.
    /// </summary>
    [TestMethod]
    public async Task Crash_UnwritableSnapshot_PreservesExitAndOtherStream()
    {
        using var cli = new CliSandbox();
        var java = new JavaProbeFixture(cli.Root);
        var instance = await CreateInstanceAsync(cli);
        var started = (await cli.RunAsync("launch", instance, "--offline", "--java", java.Executable)).ReadEnvelope(0)["data"]!;
        var session = started["id"]!.GetValue<string>();
        using var child = Process.GetProcessById(started["processId"]!.GetValue<int>());
        using var monitor = Process.GetProcessById(started["monitorId"]!.GetValue<int>());
        var blocked = Path.Combine(cli.Root, "logs", session + ".stdout.log");
        try
        {
            await ReadUntilAsync(cli, session, "synthetic-console-error");
            await File.WriteAllTextAsync(blocked, "existing snapshot", TestContext.CancellationToken);
            File.SetAttributes(blocked, FileAttributes.ReadOnly);
            await SignalExitAsync(java.Home, "9", TestContext.CancellationToken);
            await child.WaitForExitAsync(TestContext.CancellationToken);
            await monitor.WaitForExitAsync(TestContext.CancellationToken);
            var state = (await cli.RunAsync("process", "show", session)).ReadEnvelope(0)["data"]!;
            Assert.AreEqual("exited", state["status"]!.ToString());
            Assert.AreEqual(9, state["exitCode"]!.GetValue<int>());
            Assert.IsFalse(string.IsNullOrWhiteSpace(state["stdoutLogWarning"]?.ToString()));
            Assert.AreEqual("existing snapshot", await File.ReadAllTextAsync(blocked, TestContext.CancellationToken));
            var logs = (await cli.RunAsync("logs", session)).ReadEnvelope(0)["data"]!;
            Assert.AreEqual("saved", logs["streams"]!["stderrLog"]!["source"]!.ToString());
            Assert.AreEqual("unavailable", logs["streams"]!["stdoutLog"]!["source"]!.ToString());
            Assert.Contains("synthetic-console-error", logs.ToJsonString());
        }
        finally
        {
            if (File.Exists(blocked)) File.SetAttributes(blocked, FileAttributes.Normal);
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await child.WaitForExitAsync(cleanup.Token);
            await monitor.WaitForExitAsync(cleanup.Token);
        }
    }

    private static void AssertNoCaptures(CliSandbox cli)
    {
        var directory = Path.Combine(cli.Root, "logs");
        var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            // Small startup timings are independent evidence, never a retained console stream.
            .Where(file => !file.EndsWith(".startup.json", StringComparison.Ordinal))
            .Where(file => !Path.GetRelativePath(directory, file).StartsWith("launcher" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.IsEmpty(files, "Normal output must not produce log or capture-status files.");
    }

    private static async Task SignalExitAsync(string home, string code, CancellationToken ct)
    {
        // The producer observes existence; publishing an empty in-progress write can falsely simulate a crash.
        var file = Path.Combine(home, "console-exit");
        await File.WriteAllTextAsync(file + ".partial", code, ct);
        File.Move(file + ".partial", file);
    }

    private static async Task<string> CreateInstanceAsync(CliSandbox cli)
    {
        var instance = (await cli.RunAsync("instance", "create", "console-fixture", "--version", "synthetic")).ReadEnvelope(0)["data"]!;
        var directory = Path.Combine(instance["minecraftDirectory"]!.GetValue<string>(), "versions", "synthetic");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "synthetic.jar"), "synthetic process fixture");
        File.WriteAllText(Path.Combine(directory, "synthetic.json"), new JsonObject
        {
            ["id"] = "synthetic",
            ["mainClass"] = "Synthetic.Console",
            ["javaVersion"] = new JsonObject { ["majorVersion"] = 25 },
            ["libraries"] = new JsonArray(),
            ["arguments"] = new JsonObject { ["jvm"] = new JsonArray(), ["game"] = new JsonArray() }
        }.ToJsonString());
        return instance["id"]!.GetValue<string>();
    }

    private async Task<JsonNode> ReadUntilAsync(CliSandbox cli, string session, string marker)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var logs = (await cli.RunAsync("logs", session)).ReadEnvelope(0)["data"]!;
            var text = logs.ToJsonString();
            if (text.Contains(marker, StringComparison.Ordinal) && text.Contains("console-start", StringComparison.Ordinal)) return logs;
            await Task.Delay(20, deadline.Token);
        }
    }

    private async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (!predicate()) await Task.Delay(20, deadline.Token);
    }

    /// <summary>Gets the runner deadline used by owned processes and console barriers.</summary>
    public TestContext TestContext { get; set; } = null!;
}