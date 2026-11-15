// Copyright (c) Richasy. All rights reserved.

using System.IO.Compression;
using System.Security.Cryptography;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Checks the launcher-only export command and current wire metadata through actual CLI processes.</summary>
[TestClass]
public sealed class LauncherLogWorkflowTests
{
    /// <summary>Review: Core tests cannot detect a parent logs-session argument blocking the new command, or missing AOT metadata. A root with no game must export a real ZIP, then a second process must reject the existing path without changing its bytes or leaving staging files.</summary>
    [TestMethod]
    public async Task LauncherLogs_WithoutGameSession_ExportsAndPreservesExistingDestination()
    {
        using var cli = new CliSandbox();
        var destination = Path.Combine(cli.Root, "launcher-only.zip");
        var result = (await cli.RunAsync("logs", "launcher", "export", destination)).ReadEnvelope(0);
        Assert.AreEqual(destination, result["data"]!["path"]!.ToString());
        Assert.AreEqual(7, result["data"]!["manifest"]!["retentionDays"]!.GetValue<int>());
        using (var archive = ZipFile.OpenRead(destination))
        {
            Assert.IsNotNull(archive.GetEntry("manifest.json"));
            Assert.IsNull(archive.GetEntry("session.json"));
        }

        var original = SHA256.HashData(await File.ReadAllBytesAsync(destination, TestContext.CancellationToken));
        (await cli.RunAsync("logs", "launcher", "export", destination)).ReadEnvelope(1);
        CollectionAssert.AreEqual(original, SHA256.HashData(await File.ReadAllBytesAsync(destination, TestContext.CancellationToken)));
        Assert.IsEmpty(Directory.GetFiles(cli.Root, "launcher-only.zip.*.tmp"));
    }

    /// <summary>Gets cancellation for the test's archive reads.</summary>
    public TestContext TestContext { get; set; } = null!;
}