using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Packs;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects actual archive extraction progress, integrity records and cancellation before publication.</summary>
[TestClass]
public sealed class PackAcquisitionProgressTests
{
    /// <summary>
    /// Review: completed-file counters cannot prove that a large embedded file is advancing. Observe real
    /// extraction before its final byte, require monotonic aggregate progress and verify independently hashed
    /// published bytes. The canceled variant reaches that same boundary and must leave no partial instance.
    /// An author's recovery-looking override also distinguishes precise transfer cleanup from deleting by suffix.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EmbeddedFile_ReportsMeasuredBytes_AndCancellationRemovesPartialImport(bool cancel) => await ReportStorage.RunAsync(async paths =>
    {
        var archive = Path.Combine(paths.Root, "embedded.mrpack");
        var payload = new byte[1024 * 1024];
        RandomNumberGenerator.Fill(payload);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open()))
                writer.Write("""{"formatVersion":1,"game":"minecraft","name":"Embedded fixture","versionId":"one","dependencies":{"minecraft":"1.20.1"},"files":[]}""");
            using (var output = zip.CreateEntry("overrides/config/data.bin", CompressionLevel.NoCompression).Open())
                output.Write(payload);
            using var notes = new StreamWriter(zip.CreateEntry("overrides/config/notes.rodel.partial").Open());
            notes.Write("pack override");
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var measured = false;
        var previous = 0d;
        var verified = false;
        var packs = new PackService(paths, progress: new Callback(value =>
        {
            var percent = value.OverallPercent ?? throw new AssertFailedException("Known archive work must report aggregate progress.");
            Assert.IsGreaterThanOrEqualTo(previous, percent, "Concurrent activity must not move total progress backwards.");
            previous = percent;
            if (value.Stage == "pack-verify") verified = true;
            if (value.Stage != "pack-extract" || !value.Path.EndsWith("data.bin", StringComparison.Ordinal) || value.Bytes <= 0 || value.Bytes >= payload.Length) return;
            measured = true;
            Assert.AreEqual((long)payload.Length, value.TotalBytes);
            Assert.IsTrue(value.OverallPercent is > 0d and < 100d);
            if (cancel) cancellation.Cancel();
        }));
        if (cancel)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => packs.ImportAsync(archive, download: false, cancellationToken: cancellation.Token));
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(paths.InstancesDirectory));
        }
        else
        {
            var result = await packs.ImportAsync(archive, download: false, cancellationToken: cancellation.Token);
            Assert.IsTrue(result.Complete);
            Assert.IsTrue(verified);
            Assert.AreEqual(100d, previous);
            var record = result.Pack!.Files.Single(item => item.Path == "config/data.bin");
            Assert.AreEqual(Convert.ToHexStringLower(SHA1.HashData(payload)), record.Sha1);
            Assert.AreEqual(Convert.ToHexStringLower(SHA512.HashData(payload)), record.Sha512);
            CollectionAssert.AreEqual(payload, await File.ReadAllBytesAsync(Path.Combine(result.Instance!.GameDirectory!, "config", "data.bin"), TestContext.CancellationToken));
            Assert.AreEqual("pack override", await File.ReadAllTextAsync(Path.Combine(result.Instance.GameDirectory!, "config", "notes.rodel.partial"), TestContext.CancellationToken));
        }

        Assert.IsTrue(measured, "The callback must reach an actual partial extraction, not just task creation.");
        Assert.IsTrue(File.Exists(archive));
    });

    /// <summary>
    /// Review: archive cancellation and post-content preparation do not reach concurrent HTTP bodies.
    /// Observe two real partial files before cancellation, then require all workers to stop before the
    /// failed instance is removed. Only the remote server is controlled; acquisition and cleanup are real.
    /// </summary>
    [TestMethod]
    public async Task ConcurrentBodies_CancellationStopsAllWorkersBeforeRemovingInstance() => await ReportStorage.RunAsync(async paths =>
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        await using var server = new ContentDownloadServer();
        var payload = new byte[128 * 1024];
        RandomNumberGenerator.Fill(payload);
        // Exceed the production buffer even when TCP splits the response into smaller reads.
        var first = server.Add("first.jar", payload, prefixLength: 96 * 1024);
        var second = server.Add("second.jar", payload, prefixLength: 96 * 1024);
        var declarations = new JsonArray();
        foreach (var name in stringArray)
        {
            declarations.Add(new JsonObject
            {
                ["path"] = "mods/" + name,
                ["fileSize"] = payload.Length,
                ["hashes"] = new JsonObject { ["sha1"] = Convert.ToHexStringLower(SHA1.HashData(payload)) },
                ["downloads"] = new JsonArray(new Uri(server.Endpoint, name).AbsoluteUri)
            });
        }

        var archive = Path.Combine(paths.Root, "cancel.mrpack");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open()))
        {
            writer.Write(new JsonObject
            {
                ["formatVersion"] = 1,
                ["game"] = "minecraft",
                ["name"] = "Cancellation fixture",
                ["versionId"] = "one",
                ["dependencies"] = new JsonObject { ["minecraft"] = "1.20.1" },
                ["files"] = declarations
            }.ToJsonString());
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var importing = new PackService(paths).ImportAsync(archive, cancellationToken: cancellation.Token);
        try
        {
            await Task.WhenAll(first.Entered.Task, second.Entered.Task).WaitAsync(deadline.Token);
            while (Directory.EnumerateFiles(paths.InstancesDirectory, "*.rodel.partial", SearchOption.AllDirectories)
                .Count(file => new FileInfo(file).Length > 0) < 2)
            {
                await Task.Delay(10, deadline.Token);
            }

            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => importing.WaitAsync(deadline.Token));
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(paths.InstancesDirectory));
            Assert.IsTrue(File.Exists(archive));
        }
        finally
        {
            cancellation.Cancel();
            first.Release.TrySetResult();
            second.Release.TrySetResult();
            try { await importing; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
    });

    private sealed class Callback(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        /// <inheritdoc/>
        public void Report(DownloadProgress value) => report(value);
    }

    /// <summary>Gets or sets the runner cancellation signal for isolated archive operations.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static readonly string[] stringArray = ["first.jar", "second.jar"];
}