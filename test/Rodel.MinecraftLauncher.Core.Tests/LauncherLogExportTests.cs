// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects launcher-only export scope and independent source failures through real files and ZIP reads.</summary>
[TestClass]
public sealed class LauncherLogExportTests
{
    /// <summary>Review: game exports require a session and cannot prove this new scope. An empty root must export; real activity and rotated text must then survive a locked/oversized neighbor, while game output, stale and foreign files stay out. ZIP contents are read independently; no I/O or session lookup is substituted.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LauncherOnly_NoGameRequired_RedactsRetainedFilesAndReportsIndependentFailure(bool oversized)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var service = new MaintenanceService(paths);
            var empty = await service.ExportLauncherLogsAsync(Path.Combine(paths.Root, "empty.zip"), TestContext.CancellationToken);
            Assert.IsEmpty(empty.Manifest.Files);
            Assert.Contains("LAUNCHER_LOGS_EMPTY", empty.Manifest.Warnings);
            Assert.IsTrue(await new LauncherActivityLog(paths).WriteAsync("download.completed", bytes: 17, cancellationToken: TestContext.CancellationToken));
            var root = Path.Combine(paths.LogsDirectory, "launcher");
            var today = "launcher-" + DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".jsonl";
            var rotated = Path.Combine(root, today + ".1");
            const string original = "{\"event\":\"fixture\",\"password\":\"synthetic-secret\",\"extra\":\"retained-note\"}\n";
            await File.WriteAllTextAsync(rotated, original, TestContext.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(paths.LogsDirectory, "game.log"), "private-game-marker", TestContext.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, today + ".backup"), "foreign-marker", TestContext.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, "launcher-20000101.jsonl"), "stale-marker", TestContext.CancellationToken);
            var unavailable = Path.Combine(root, today + ".2");
            using (var source = new FileStream(unavailable, FileMode.Create, FileAccess.ReadWrite, oversized ? FileShare.ReadWrite : FileShare.None))
            {
                if (oversized) source.SetLength((16 * 1024 * 1024) + 1);
                var exported = await service.ExportLauncherLogsAsync(Path.Combine(paths.Root, "launcher.zip"), TestContext.CancellationToken);
                Assert.HasCount(2, exported.Manifest.Files);
                Assert.Contains(oversized ? "LAUNCHER_LOG_TOO_LARGE:" + today + ".2" : "LAUNCHER_LOG_UNREADABLE:" + today + ".2", exported.Manifest.Warnings);
                using var archive = ZipFile.OpenRead(exported.Path);
                CollectionAssert.AreEquivalent(new[] { "manifest.json", "launcher/" + today, "launcher/" + today + ".1" }, archive.Entries.Select(entry => entry.FullName).ToArray());
                using var reader = new StreamReader(archive.GetEntry("launcher/" + today + ".1")!.Open());
                var text = await reader.ReadToEndAsync(TestContext.CancellationToken);
                Assert.Contains("retained-note", text);
                Assert.Contains("[REDACTED]", text);
                Assert.DoesNotContain("synthetic-secret", text);
                await using var manifestBytes = archive.GetEntry("manifest.json")!.Open();
                var manifest = await JsonSerializer.DeserializeAsync(manifestBytes, ApplicationJsonContext.Default.LauncherLogManifest, TestContext.CancellationToken);
                CollectionAssert.AreEqual(exported.Manifest.Files.ToArray(), manifest!.Files.ToArray());
                Assert.AreEqual(7, manifest.RetentionDays);
            }

            Assert.AreEqual(original, await File.ReadAllTextAsync(rotated, TestContext.CancellationToken));
            Assert.AreEqual("private-game-marker", await File.ReadAllTextAsync(Path.Combine(paths.LogsDirectory, "game.log"), TestContext.CancellationToken));
        });
    }

    /// <summary>Gets cancellation for file operations owned by this test.</summary>
    public TestContext TestContext { get; set; } = null!;
}