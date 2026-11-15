// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Loaders;
using Rodel.MinecraftLauncher.Core.Packs;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects operation progress across real file boundaries without depending on public downloads or animation timing.</summary>
[TestClass]
public sealed class PreparationProgressTests
{
    /// <summary>
    /// Author review: existing transfer recovery tests protect bytes, not a batch's progress. Two real
    /// transfers followed by an override must not finish the batch at either file's 100% callback.
    /// Only the HTTP transport is substituted; archive inspection, verification and publication are real.
    /// </summary>
    [TestMethod]
    public async Task Pack_TwoDownloadsAndOverride_ReportOneMonotonicBatch() => await ReportStorage.RunAsync(async paths =>
    {
        byte[] payload = [1, 2, 3, 4];
        var files = new JsonArray();
        foreach (var name in ArchiveFileNames)
        {
            files.Add(new JsonObject
            {
                ["path"] = "mods/" + name,
                ["hashes"] = new JsonObject { ["sha1"] = Convert.ToHexStringLower(SHA1.HashData(payload)) },
                ["fileSize"] = payload.Length,
                ["downloads"] = new JsonArray("https://fixture.test/" + name)
            });
        }

        var archive = Path.Combine(paths.Root, "progress.mrpack");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open()))
            {
                writer.Write(new JsonObject
                {
                    ["formatVersion"] = 1,
                    ["game"] = "minecraft",
                    ["name"] = "Progress fixture",
                    ["versionId"] = "one",
                    ["dependencies"] = new JsonObject { ["minecraft"] = "1.20.1" },
                    ["files"] = files
                }.ToJsonString());
            }

            using var settings = new StreamWriter(zip.CreateEntry("overrides/config/fixture.txt").Open());
            settings.Write("local override");
        }

        using var transport = new SourceTransport(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        using var http = new HttpClient(transport);
        var events = new Recorder<DownloadProgress>();
        var result = await new PackService(paths, http, progress: events).ImportAsync(archive, cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(result.Complete);
        Assert.HasCount(2, transport.Addresses);
        var transfers = events.Values.Where(value => value.Stage is null && value.TotalBytes == payload.Length && value.Bytes == payload.Length).ToArray();
        Assert.HasCount(2, transfers);
        Assert.IsTrue(transfers[0].OverallPercent is > 0 and < 100, "Measured bytes must advance the batch before the containing file is recorded.");
        Assert.IsTrue(transfers[1].OverallPercent is > 0 and < 100, "Changing files must preserve completed work and leave the override pending.");
        AssertMonotonic(events.Values.Select(value => value.OverallPercent));
        Assert.AreEqual(100d, events.Values[^1].OverallPercent);
        Assert.AreEqual("local override", await File.ReadAllTextAsync(Path.Combine(result.Instance!.GameDirectory!, "config", "fixture.txt"), TestContext.CancellationToken));
    });

    /// <summary>
    /// Author review: a loader's base installer is a nested batch; forwarding its 100% would finish
    /// the outer operation too early. Real base reuse, two Maven libraries and profile publication
    /// must stay ordered. The supplied transport replaces only profile/library server responses.
    /// </summary>
    [TestMethod]
    public async Task Fabric_BaseAndLibraries_CompleteOnlyAfterProfilePublication() => await ReportStorage.RunAsync(async paths =>
    {
        var minecraft = Path.Combine(paths.Root, "minecraft");
        await WriteBaseAsync(minecraft, TestContext.CancellationToken);
        var libraries = new JsonArray();
        foreach (var id in LibraryNames)
            libraries.Add(new JsonObject { ["name"] = $"fixture:{id}:1", ["url"] = "https://fixture.test/" });
        var profile = new JsonObject
        {
            ["id"] = "fabric",
            ["inheritsFrom"] = "1.20.1",
            ["mainClass"] = "fixture.Main",
            ["libraries"] = libraries,
            ["arguments"] = new JsonObject { ["game"] = new JsonArray(), ["jvm"] = new JsonArray() }
        };
        using var transport = new SourceTransport(request => new(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsolutePath.EndsWith(".jar", StringComparison.Ordinal)
                ? new ByteArrayContent(LibraryBody) : new StringContent(profile.ToJsonString())
        });
        using var http = new HttpClient(transport);
        var events = new Recorder<LoaderProgress>();
        var result = await new LoaderService(paths, http).InstallAsync(minecraft, "1.20.1", "fabric", "0.16.9", progress: events, cancellationToken: TestContext.CancellationToken);
        AssertMonotonic(events.Values.Select(value => value.OverallPercent));
        Assert.IsTrue(events.Values.Where(value => value.Stage != "completed").All(value => value.OverallPercent is >= 0 and < 100));
        Assert.IsGreaterThanOrEqualTo(2, events.Values.Count(value => value.Stage == "library"));
        Assert.AreEqual(100d, events.Values[^1].OverallPercent);
        Assert.IsTrue(File.Exists(Path.Combine(minecraft, "versions", result.VersionId, result.VersionId + ".json")));
    });

    internal static async Task WriteBaseAsync(string minecraft, CancellationToken cancellationToken)
    {
        await JsonFiles.WriteAsync(Path.Combine(minecraft, "versions", "1.20.1", "1.20.1.json"), new JsonObject
        {
            ["id"] = "1.20.1",
            ["mainClass"] = "fixture.Main",
            ["libraries"] = new JsonArray(),
            ["arguments"] = new JsonObject { ["game"] = new JsonArray(), ["jvm"] = new JsonArray() },
            ["downloads"] = new JsonObject { ["client"] = new JsonObject() }
        }, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(minecraft, "versions", "1.20.1", "1.20.1.jar"), [1, 2, 3], cancellationToken);
    }

    private static void AssertMonotonic(IEnumerable<double?> values)
    {
        var previous = 0d;
        foreach (var value in values)
        {
            Assert.IsNotNull(value);
            Assert.IsTrue(value >= previous && value <= 100, $"Batch progress moved from {previous} to {value}.");
            previous = value.Value;
        }
    }

    /// <summary>Gets or sets the runner deadline used by real preparation operations.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static readonly string[] ArchiveFileNames = ["first.jar", "second.jar"];
    private static readonly string[] LibraryNames = ["first", "second"];
    private static readonly byte[] LibraryBody = [1, 2, 3];

    private sealed class Recorder<T> : IProgress<T>
    {
        internal List<T> Values { get; } = [];
        /// <inheritdoc/>
        public void Report(T value) => Values.Add(value);
    }
}