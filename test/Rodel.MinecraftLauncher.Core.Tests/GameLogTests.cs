using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects console cursors, retained startup evidence and ownership of automatic log expiration.</summary>
[TestClass]
public sealed class GameLogTests
{
    /// <summary>
    /// A stream larger than the declared one-MiB window must retain exactly its tail, not grow or reuse
    /// relative cursors. This adds eviction evidence absent from the existing saved-log diagnosis tests.
    /// </summary>
    [TestMethod]
    public async Task Capture_Overflow_PreservesTailAndAbsoluteCursors()
    {
        var buffer = new GameLogBuffer();
        var input = Enumerable.Range(0, (1024 * 1024) + 37).Select(index => (byte)(index % 251)).ToArray();
        using var stream = new MemoryStream(input);
        await buffer.CaptureAsync(stream);
        var snapshot = buffer.Read(0, 1024 * 1024);
        Assert.AreEqual(37L, snapshot.FirstOffset);
        Assert.AreEqual(37L, snapshot.Offset);
        Assert.AreEqual((long)input.Length, snapshot.TotalBytes);
        CollectionAssert.AreEqual(input[37..], snapshot.Bytes);
        Assert.IsFalse(snapshot.Capture!.Complete);
        Assert.IsFalse(snapshot.Capture.Capturing);
        Assert.AreEqual(1024 * 1024L, snapshot.Capture.RetainedBytes);
        var next = buffer.Read(snapshot.TotalBytes - 13, 13);
        CollectionAssert.AreEqual(input[^13..], next.Bytes);
        Assert.IsEmpty(buffer.Read(snapshot.TotalBytes, 8).Bytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Read(snapshot.TotalBytes + 1, 8));
    }

    /// <summary>
    /// Real current-user framing must preserve raw byte offsets and decode a wrapped UTF-8 tail.
    /// The server buffers are substituted for Java; separate CLI process tests cover actual monitor wiring.
    /// </summary>
    [TestMethod]
    public async Task Read_LiveWindow_ReportsEvictionWithoutWritingFiles()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            await using var host = await GameLogHost.StartAsync(paths);
            var payload = Encoding.UTF8.GetBytes(new string('x', (1024 * 1024) - 2) + "中文\nlast-line\n");
            using var output = new MemoryStream(payload);
            await host.Stdout.CaptureAsync(output);
            using var process = Process.GetCurrentProcess();
            var id = Guid.NewGuid().ToString("N");
            await JsonFiles.WriteAsync(Path.Combine(paths.SessionsDirectory, id + ".json"), new JsonObject
            {
                ["id"] = id,
                ["status"] = "running",
                ["processId"] = process.Id,
                ["processStartUtc"] = process.StartTime.ToUniversalTime().ToString("O"),
                ["logPipe"] = host.PipeName,
                ["stdoutEncoding"] = "utf-8"
            }, TestContext.CancellationToken);
            var preview = await new LogService(paths).ReadAsync(id, byteBudget: 32, ct: TestContext.CancellationToken);
            Assert.AreEqual("memory", preview.Streams["stdoutLog"].Source, preview.Streams["stdoutLog"].Warning);
            Assert.IsTrue(preview.Streams["stdoutLog"].Truncated);
            Assert.IsGreaterThan(0, preview.Streams["stdoutLog"].FirstOffset);
            Assert.Contains("last-line", preview.StdoutLog);
            Assert.DoesNotContain("\uFFFD", string.Join('\n', preview.StdoutLog));
            Assert.IsEmpty(Directory.GetFiles(paths.LogsDirectory, "*", SearchOption.AllDirectories));
            await Assert.ThrowsAsync<ArgumentException>(() => new LogService(paths).ReadAsync(id,
                stdoutOffset: payload.Length + 1L, ct: TestContext.CancellationToken));
        });
    }

    /// <summary>
    /// Expiration must remove only old owned captures, retain active/unreadable sessions and arbitrary player
    /// files, and expose a real deletion failure without preventing another expired capture from being removed.
    /// </summary>
    [TestMethod]
    public async Task Retention_ExpiredCaptures_PreservesPlayerActiveAndRecentFiles()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            string Capture(string name, int days)
            {
                var path = Path.Combine(paths.LogsDirectory, name);
                File.WriteAllText(path, "retained evidence");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-days));
                return path;
            }

            var expired = Capture(Guid.NewGuid().ToString("N") + ".stdout.log", 8);
            var part = Capture(Guid.NewGuid().ToString("N") + ".stderr.log.part000001", 8);
            var startup = Capture(Guid.NewGuid().ToString("N") + ".startup.json", 8);
            var recent = Capture(Guid.NewGuid().ToString("N") + ".stderr.log", 6);
            var player = Capture("latest.log", 30);
            var userExport = Capture("my-diagnostic.zip", 30);
            var busyId = Guid.NewGuid().ToString("N");
            var busy = Capture(busyId + ".stdout.log", 8);
            using var process = Process.GetCurrentProcess();
            await JsonFiles.WriteAsync(Path.Combine(paths.SessionsDirectory, busyId + ".json"), new JsonObject
            {
                ["id"] = busyId,
                ["status"] = "running",
                ["processId"] = process.Id,
                ["processStartUtc"] = process.StartTime.ToUniversalTime().ToString("O")
            }, TestContext.CancellationToken);
            var brokenId = Guid.NewGuid().ToString("N");
            var broken = Capture(brokenId + ".stdout.log", 8);
            File.WriteAllText(Path.Combine(paths.SessionsDirectory, brokenId + ".json"), "{broken");
            var locked = Capture(Guid.NewGuid().ToString("N") + ".stderr.log", 8);
            File.SetAttributes(locked, FileAttributes.ReadOnly);
            try
            {
                var warnings = GameLogRetention.Prune(paths);
                Assert.IsFalse(File.Exists(expired));
                Assert.IsFalse(File.Exists(part));
                Assert.IsFalse(File.Exists(startup));
                foreach (var retained in new[] { recent, player, userExport, busy, broken, locked })
                    Assert.AreEqual("retained evidence", File.ReadAllText(retained));
                Assert.IsGreaterThanOrEqualTo(2, warnings.Count, "Unreadable activity and a real delete failure must both remain visible.");
            }
            finally { File.SetAttributes(locked, FileAttributes.Normal); }
        });
    }

    /// <summary>
    /// Review: current console tests release normal output on exit. This real monitor/subprocess case must retain
    /// its small timing report while keeping arbitrary console text private. A persisted first-phase marker is
    /// the barrier before releasing phase two; duplicated markers must not reset the observed interval. Printed
    /// window/sound markers alone must never establish an actual visible window or successful initialization.
    /// </summary>
    [TestMethod]
    public async Task Startup_MonitorExit_RetainsMilestonesWithoutPersistingConsoleOrInventingWindow()
    {
        using var java = new JavaProbeFixture();
        java.Paths.EnsureDirectories();
        var game = Path.Combine(java.Root, "game");
        var plan = new LaunchPlan("synthetic", java.Executable, ["Synthetic.Startup"], game,
            Path.Combine(game, "natives"), [], [], "Synthetic.Startup", "synthetic-offline");
        var sessions = new SessionService(java.Paths);
        var id = await sessions.CreateAsync(plan, "synthetic", ct: TestContext.CancellationToken);
        var monitor = sessions.RunMonitorAsync(id, TestContext.CancellationToken);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            while (sessions.Get(id).Startup?.Milestones.Any(item => item.Stage == "mixins_started") != true)
                await Task.Delay(20, deadline.Token);
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(java.Home, "startup-continue"), string.Empty, CancellationToken.None);
            await monitor;
        }

        var report = sessions.Get(id).Startup!;
        Assert.AreEqual("process_exited", report.Status);
        Assert.HasCount(1, report.Milestones.Where(item => item.Stage == "mixins_started").ToArray());
        Assert.Contains(item => item.Stage == "resource_reload" && item.Source == "stdout", report.Milestones);
        Assert.Contains(item => item.Stage == "textures_ready", report.Milestones);
        Assert.DoesNotContain(item => item.Stage == "window_visible", report.Milestones);
        Assert.IsGreaterThan(0L, report.Milestones.Single(item => item.Stage == "window_created_log").SincePreviousMilliseconds);
        Assert.AreEqual(report.Milestones[report.Milestones.Count - 1].ElapsedMilliseconds, report.Milestones.Sum(item => item.SincePreviousMilliseconds));
        var preview = await new LogService(java.Paths).ReadAsync(id, ct: TestContext.CancellationToken);
        Assert.IsNotNull(preview.Startup);
        Assert.AreEqual("unavailable", preview.Streams["stdoutLog"].Source);
        foreach (var file in Directory.GetFiles(java.Paths.LogsDirectory, "*", SearchOption.AllDirectories))
            Assert.DoesNotContain("private-payload", await File.ReadAllTextAsync(file, TestContext.CancellationToken));

        var export = Path.Combine(java.Root, "logs.zip");
        await new MaintenanceService(java.Paths).ExportLogsAsync(id, export, TestContext.CancellationToken);
        using var archive = System.IO.Compression.ZipFile.OpenRead(export);
        using var reader = new StreamReader(archive.GetEntry("session.json")!.Open());
        Assert.Contains("mixins_started", await reader.ReadToEndAsync(TestContext.CancellationToken));
    }

    /// <summary>
    /// Review: an OS handle denying diagnostic replacement must not fail an otherwise successful launch or
    /// overwrite the existing file. Console crash-snapshot failures exercise another writer and lifecycle;
    /// this case holds the actual startup destination throughout preparation, process exit and observer disposal.
    /// </summary>
    [TestMethod]
    public async Task Startup_LockedDiagnosticFile_PreservesGameExitAndPreviousEvidence()
    {
        using var java = new JavaProbeFixture();
        java.Paths.EnsureDirectories();
        await File.WriteAllTextAsync(Path.Combine(java.Home, "startup-continue"), string.Empty, TestContext.CancellationToken);
        var game = Path.Combine(java.Root, "game");
        var plan = new LaunchPlan("synthetic", java.Executable, ["Synthetic.Startup"], game,
            Path.Combine(game, "natives"), [], [], "Synthetic.Startup", "synthetic-offline");
        var sessions = new SessionService(java.Paths);
        var id = await sessions.CreateAsync(plan, "synthetic", ct: TestContext.CancellationToken);
        var file = Path.Combine(java.Paths.LogsDirectory, id + ".startup.json");
        await File.WriteAllTextAsync(file, "previous diagnostic evidence", TestContext.CancellationToken);
        using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            await sessions.RunMonitorAsync(id, TestContext.CancellationToken);
        var ended = sessions.Get(id);
        Assert.AreEqual("exited", ended.Status, ended.Error);
        Assert.AreEqual(0, ended.ExitCode);
        Assert.AreEqual("previous diagnostic evidence", await File.ReadAllTextAsync(file, TestContext.CancellationToken));
        Assert.AreEqual("unavailable", ended.Startup!.Status);
        Assert.IsFalse(string.IsNullOrEmpty(ended.Startup.Warning));
    }

    /// <summary>Gets the runner deadline for owned streams and isolated files.</summary>
    public TestContext TestContext { get; set; } = null!;
}