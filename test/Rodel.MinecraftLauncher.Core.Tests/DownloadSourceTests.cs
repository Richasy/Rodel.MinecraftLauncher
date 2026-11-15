using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects public download routing and verified recovery through the real Core HTTP pipeline and file store.</summary>
[TestClass]
public sealed class DownloadSourceTests
{
    // Author review: existing installation concurrency tests use local URLs and cannot detect a missing
    // source mapping. Literal upstream/mirror URLs below independently specify distinct resource families.
    // The transport alone is substituted; routing, redirects, buffering and file publication are production code.
    /// <summary>Opting into BMCLAPI must route supported metadata and payloads without changing their paths or queries.</summary>
    [TestMethod]
    [DataRow("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", "/mc/game/version_manifest_v2.json")]
    [DataRow("https://piston-data.mojang.com/v1/objects/abc/client.jar", "/v1/objects/abc/client.jar")]
    [DataRow("https://resources.download.minecraft.net/ab/abcdef", "/assets/ab/abcdef")]
    [DataRow("https://libraries.minecraft.net/org/example/library.jar", "/maven/org/example/library.jar")]
    [DataRow("https://launchermeta.mojang.com/v1/products/java-runtime/all.json", "/v1/products/java-runtime/all.json")]
    [DataRow("https://meta.fabricmc.net/v2/versions/loader/1.21.1", "/fabric-meta/v2/versions/loader/1.21.1")]
    [DataRow("http://files.minecraftforge.net/maven/net/minecraftforge/forge.jar", "/maven/net/minecraftforge/forge.jar")]
    [DataRow("https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml", "/maven/net/neoforged/neoforge/maven-metadata.xml")]
    [DataRow("https://maven.quiltmc.org/repository/release/org/quiltmc/loader.jar", "/maven/org/quiltmc/loader.jar")]
    [DataRow("https://authlib-injector.yushi.moe/artifact/latest.json?check=1", "/mirrors/authlib-injector/artifact/latest.json?check=1")]
    public async Task Bmclapi_RoutesSupportedResources(string original, string expected)
    {
        using var transport = new SourceTransport(_ => Ok("verified"));
        using var http = Client("bmclapi", transport);
        using var response = await DownloadRequests.GetBufferedAsync(http, new Uri(original), TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreEqual("verified", await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        CollectionAssert.AreEqual(new[] { "https://bmclapi2.bangbang93.com" + expected }, transport.Addresses);
    }

    /// <summary>Missing or unfamiliar choices keep official defaults; unrelated origins and lookalike hosts must never be rewritten.</summary>
    [TestMethod]
    [DataRow(null, "https://piston-data.mojang.com/file.jar")]
    [DataRow("official", "https://piston-data.mojang.com/file.jar")]
    [DataRow("future-source", "https://piston-data.mojang.com/file.jar")]
    [DataRow("bmclapi", "https://resources.download.minecraft.net.example.invalid/file")]
    [DataRow("bmclapi", "https://api.modrinth.com/v2/search")]
    [DataRow("bmclapi", "https://api.minecraftservices.com/minecraft/profile")]
    [DataRow("bmclapi", "https://api.github.com/repos/example/releases")]
    public async Task DefaultsAndUnrelatedOrigins_KeepOriginalSource(string? source, string address)
    {
        using var transport = new SourceTransport(_ => Ok("original"));
        using var http = Client(source, transport);
        using var response = await http.GetAsync(address, TestContext.CancellationToken);
        CollectionAssert.AreEqual(new[] { address }, transport.Addresses);
    }

    /// <summary>Explicit custom mappings remain usable and sensitive headers stay at the original source, including a forced fallback attempt.</summary>
    [TestMethod]
    public async Task CustomMapping_StripsCredentials_AndOriginalOnlyBypassesIt()
    {
        var settings = new JsonObject { ["source"] = "bmclapi", ["mirrors"] = new JsonObject { ["https://libraries.minecraft.net/"] = "https://custom.invalid/maven/" } };
        using var transport = new SourceTransport(request =>
        {
            var original = request.RequestUri!.Host == "libraries.minecraft.net";
            Assert.AreEqual(original, request.Headers.Authorization is not null);
            Assert.AreEqual(original, request.Headers.Contains("Cookie"));
            Assert.AreEqual(original, request.Headers.Contains("x-api-key"));
            Assert.AreEqual("bytes=3-", request.Headers.Range?.ToString());
            return Ok("content");
        });
        using var http = new HttpClient(new NetworkSettings.MirrorHandler(settings) { InnerHandler = transport });
        foreach (var originalOnly in new[] { false, true })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://libraries.minecraft.net/library.jar");
            request.Options.Set(DownloadRequests.OriginalSourceOnly, originalOnly);
            request.Headers.Authorization = new("Bearer", "synthetic");
            request.Headers.Add("Cookie", "synthetic=1");
            request.Headers.Add("x-api-key", "synthetic");
            request.Headers.Range = new(3, null);
            using var response = await http.SendAsync(request, TestContext.CancellationToken);
        }

        CollectionAssert.AreEqual(ExpectedCustomRoutes, transport.Addresses);
    }

    /// <summary>A missing mirror or failed body after headers must give the original source its own buffered attempt.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MirrorFailure_RetriesOriginalMetadata(bool bodyFailure)
    {
        using var transport = new SourceTransport(request => request.RequestUri!.Host == "meta.quiltmc.org"
            ? Ok("original metadata")
            : bodyFailure ? new(HttpStatusCode.OK) { Content = new FailedBody() } : new(HttpStatusCode.NotFound));
        using var http = Client("bmclapi", transport);
        using var response = await DownloadRequests.GetBufferedAsync(http, new Uri("https://meta.quiltmc.org/v3/versions/loader"), TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.AreEqual("original metadata", await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        CollectionAssert.AreEqual(ExpectedMetadataRoutes, transport.Addresses);
    }

    /// <summary>SHA256, rather than equal size, must reject corrupt mirror bytes, recover from the origin and reuse only verified published bytes.</summary>
    [TestMethod]
    public async Task CorruptMirror_DownloadsVerifiedOriginal_AndReusesIt() => await ReportStorage.RunAsync(async paths =>
    {
        var payload = "original"u8.ToArray();
        using var transport = new SourceTransport(request => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(request.RequestUri!.Host == "bmclapi2.bangbang93.com" ? "tampered"u8.ToArray() : payload)
        });
        using var http = Client("bmclapi", transport);
        var downloads = new DownloadService(paths, http);
        var target = Path.Combine(paths.Root, "artifact.jar");
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        var uri = new Uri("https://authlib-injector.yushi.moe/artifact/56/agent.jar");
        Assert.IsTrue(await downloads.DownloadAsync(uri, target, size: payload.Length, sha256: hash, cancellationToken: TestContext.CancellationToken));
        CollectionAssert.AreEqual(payload, await File.ReadAllBytesAsync(target, TestContext.CancellationToken));
        CollectionAssert.AreEqual(new[] { "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/56/agent.jar", uri.AbsoluteUri }, transport.Addresses);
        Assert.IsFalse(await downloads.DownloadAsync(uri, target, size: payload.Length, sha256: hash, cancellationToken: TestContext.CancellationToken));
        Assert.HasCount(2, transport.Addresses);
        Assert.IsFalse(File.Exists(target + ".rodel.partial"));
    });

    /// <summary>
    /// Review: the corrupt-mirror case cannot detect dropping a secondary digest while combining reads.
    /// Correct SHA-1 and SHA-256 with a deliberately different SHA-512 must still reject publication and
    /// retain an existing target. Only HTTP is substituted; retries, hashing and file publication remain real.
    /// </summary>
    [TestMethod]
    public async Task CombinedDigests_MismatchedSha512_PreservesExistingTarget() => await ReportStorage.RunAsync(async paths =>
    {
        var payload = "verified payload"u8.ToArray();
        using var transport = new SourceTransport(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        using var http = Client("official", transport);
        var target = Path.Combine(paths.InstancesDirectory, "retained.jar");
        await File.WriteAllTextAsync(target, "player bytes", TestContext.CancellationToken);
        var downloads = new DownloadService(paths, http);
        await Assert.ThrowsAsync<InvalidDataException>(() => downloads.DownloadAsync(new Uri("https://source.invalid/mod.jar"), target,
            sha1: Convert.ToHexStringLower(SHA1.HashData(payload)), size: payload.Length,
            sha256: Convert.ToHexStringLower(SHA256.HashData(payload)), sha512: Convert.ToHexStringLower(SHA512.HashData("different"u8)),
            cancellationToken: TestContext.CancellationToken));
        Assert.AreEqual("player bytes", await File.ReadAllTextAsync(target, TestContext.CancellationToken));
        Assert.IsFalse(File.Exists(target + ".rodel.partial"));
    });

    private static HttpClient Client(string? source, HttpMessageHandler transport) => new(new NetworkSettings.MirrorHandler(new JsonObject { ["source"] = source }) { InnerHandler = transport });
    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };

    private sealed class FailedBody : HttpContent
    {
        /// <inheritdoc/>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.FromException(new IOException("Mirror body failed after headers."));
        /// <inheritdoc/>
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    /// <summary>Gets or sets the runner deadline for requests and isolated storage operations.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static readonly string[] ExpectedCustomRoutes = ["https://custom.invalid/maven/library.jar", "https://libraries.minecraft.net/library.jar"];
    private static readonly string[] ExpectedMetadataRoutes = ["https://bmclapi2.bangbang93.com/quilt-meta/v3/versions/loader", "https://meta.quiltmc.org/v3/versions/loader"];
}