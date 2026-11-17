// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Updates;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects the release trust boundary and real download publication; HTTP replaces only GitHub.</summary>
[TestClass]
public sealed class GitHubUpdateTests
{
    /// <summary>
    /// Review: existing Store tests never discover an EXE update. Selecting the wrong architecture or conflating
    /// Chinese scripts would offer an unusable installer; an exact skip must suppress only that version.
    /// </summary>
    [TestMethod]
    [DataRow("x64")]
    [DataRow("arm64")]
    public async Task Discovery_SelectsExactArchitectureLocalizedNotesAndSkip(string architecture)
    {
        using var fixture = new UpdateFeed();
        using var http = new HttpClient(fixture);
        var client = new GitHubUpdateClient(http);
        var update = await client.CheckAsync(new(0, 2611, 4, 0), string.Empty, architecture, TestContext.CancellationToken);
        Assert.IsNotNull(update);
        Assert.AreEqual($"RMCL-0.2611.5.0-win-{architecture}-setup.exe", update.Installer.FileName);
        Assert.AreEqual("繁體更新", update.Notes("zh-HK"));
        Assert.AreEqual("简体更新", update.Notes("zh-Hans"));
        Assert.AreEqual("English notes", update.Notes("fr"));
        Assert.IsNull(await client.CheckAsync(new(0, 2611, 4, 0), "0.2611.5.0", architecture, TestContext.CancellationToken));
        Assert.IsNotNull(await client.CheckAsync(new(0, 2611, 4, 0), "0.2611.3.0", architecture, TestContext.CancellationToken));
        Assert.IsNull(await client.CheckAsync(new(0, 2611, 5, 0), string.Empty, architecture, TestContext.CancellationToken));
    }

    /// <summary>Review: unpublished, mismatched and malformed remote state must be silent and never become executable update offers.</summary>
    [TestMethod]
    [DataRow("prerelease")]
    [DataRow("draft")]
    [DataRow("wrong-version")]
    [DataRow("foreign-url")]
    [DataRow("missing-installer")]
    [DataRow("bad-hash")]
    [DataRow("rate-limit")]
    [DataRow("malformed")]
    public async Task Discovery_InvalidRelease_IsSilentlyUnavailable(string fault)
    {
        using var fixture = new UpdateFeed();
        switch (fault)
        {
            case "prerelease": fixture.Release["prerelease"] = true; break;
            case "draft": fixture.Release["draft"] = true; break;
            case "wrong-version": fixture.Manifest["version"] = "0.2611.6.0"; break;
            case "foreign-url": fixture.Release["assets"]![0]!["browser_download_url"] = "https://example.com/release-note.json"; break;
            case "missing-installer": fixture.Release["assets"]!.AsArray().RemoveAt(1); break;
            case "bad-hash": fixture.Manifest["installers"]!["x64"]!["sha256"] = "../bad"; break;
            case "rate-limit": fixture.Status = HttpStatusCode.Forbidden; break;
            case "malformed": fixture.Malformed = true; break;
        }

        using var http = new HttpClient(fixture);
        Assert.IsNull(await new GitHubUpdateClient(http).CheckAsync(new(0, 2611, 4, 0), string.Empty, "x64", TestContext.CancellationToken));
    }

    /// <summary>Review: byte progress must precede completion; corrupted or truncated bytes must not replace a previously verified installer.</summary>
    [TestMethod]
    [DataRow("valid")]
    [DataRow("corrupt")]
    [DataRow("truncated")]
    [DataRow("oversized")]
    public async Task Download_VerifiesActualBytesBeforePublishing(string fault)
    {
        using var fixture = new UpdateFeed();
        using var http = new HttpClient(fixture);
        var client = new GitHubUpdateClient(http);
        var update = await client.CheckAsync(new(0, 2611, 4, 0), string.Empty, "x64", TestContext.CancellationToken);
        Assert.IsNotNull(update);
        var original = fixture.Payload.ToArray();
        switch (fault)
        {
            case "corrupt": fixture.Payload[42] ^= 0xff; break;
            case "truncated": fixture.Payload = fixture.Payload[..^1]; break;
            case "oversized": fixture.Payload = [.. fixture.Payload, 1]; break;
        }

        var directory = Path.Combine(Path.GetTempPath(), "rmcl-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, update.Installer.FileName);
        File.WriteAllText(target, "previous complete installer");
        var progress = new List<long>();
        try
        {
            if (fault == "valid")
            {
                var path = await client.DownloadAsync(update, directory, new ByteProgress(progress.Add), TestContext.CancellationToken);
                CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path, TestContext.CancellationToken));
                Assert.Contains(value => value > 0 && value < original.Length, progress, "Progress must change before the file completes.");
                Assert.AreEqual(original.Length, progress[^1]);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(update, directory, new ByteProgress(progress.Add), TestContext.CancellationToken));
                Assert.AreEqual("previous complete installer", File.ReadAllText(target));
            }

            Assert.HasCount(1, Directory.GetFiles(directory), "Only the published file may survive; no partial installer is reusable.");
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>Review: cancellation occurs after the first real staging write, not before download starts; the old complete file and unrelated data survive.</summary>
    [TestMethod]
    public async Task Download_CancelAfterWrittenProgress_RemovesOnlyPartialFile()
    {
        using var fixture = new UpdateFeed();
        using var http = new HttpClient(fixture);
        var client = new GitHubUpdateClient(http);
        var update = await client.CheckAsync(new(0, 2611, 4, 0), string.Empty, "x64", TestContext.CancellationToken);
        Assert.IsNotNull(update);
        var directory = Path.Combine(Path.GetTempPath(), "rmcl-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sentinel = Path.Combine(directory, "unrelated.txt");
        File.WriteAllText(sentinel, "keep");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var written = false;
        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => client.DownloadAsync(update, directory, new ByteProgress(_ =>
            {
                written = Directory.GetFiles(directory, "*.part").Any(file => new FileInfo(file).Length > 0);
                cancellation.Cancel();
            }), cancellation.Token));
            Assert.IsTrue(written, "The cancellation boundary must follow an actual partial-file write.");
            CollectionAssert.AreEqual(new[] { sentinel }, Directory.GetFiles(directory));
            Assert.AreEqual("keep", File.ReadAllText(sentinel));
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>Gets the test deadline for remote and file operations.</summary>
    public TestContext TestContext { get; set; }

    private sealed class ByteProgress(Action<long> report) : IProgress<long>
    {
        /// <inheritdoc/>
        public void Report(long value) => report(value);
    }

    private sealed class UpdateFeed : HttpMessageHandler
    {
        internal byte[] Payload = [.. Enumerable.Range(0, 524288).Select(index => (byte)(index % 251))];
        internal JsonObject Manifest { get; }
        internal JsonObject Release { get; }
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal bool Malformed;

        internal UpdateFeed()
        {
            Manifest = JsonNode.Parse($$"""
                {
                  "schemaVersion": 1, "version": "0.2611.5.0", "sourceCommit": "1111111111111111111111111111111111111111",
                  "notes": { "en": "English notes", "zh-CN": "简体更新", "zh-TW": "繁體更新" },
                  "installers": {}
                }
                """)!.AsObject();
            Release = new() { ["tag_name"] = "v0.2611.5.0", ["draft"] = false, ["prerelease"] = false, ["assets"] = new JsonArray() };
            AddAsset("release-note.json", 1024);
            foreach (var architecture in new[] { "x64", "arm64" })
            {
                var name = $"RMCL-0.2611.5.0-win-{architecture}-setup.exe";
                Manifest["installers"]![architecture] = new JsonObject
                {
                    ["fileName"] = name,
                    ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(Payload)),
                    ["size"] = Payload.Length,
                };
                AddAsset(name, Payload.Length);
            }
        }

        private void AddAsset(string name, int size) => Release["assets"]!.AsArray().Add(new JsonObject
        {
            ["name"] = name,
            ["size"] = size,
            ["browser_download_url"] = GitHubUpdateClient.Repository + "/releases/download/v0.2611.5.0/" + name,
        });

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpContent content = request.RequestUri == GitHubUpdateClient.Latest
                ? new StringContent(Malformed ? "{" : Release.ToJsonString(), Encoding.UTF8, "application/json")
                : request.RequestUri!.AbsolutePath.EndsWith("release-note.json", StringComparison.Ordinal)
                ? new StringContent(Manifest.ToJsonString(), Encoding.UTF8, "application/json")
                : new StreamContent(new MemoryStream(Payload));
            return Task.FromResult(new HttpResponseMessage(Status) { Content = content });
        }
    }
}