using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Loaders;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects loader catalogs and the public login artifact where source-specific data crosses a consumer boundary.</summary>
[TestClass]
public sealed class DownloadConsumerTests
{
    /// <summary>
    /// Author review: URL-routing cases cannot detect feeding Maven XML into the old JSON parser or
    /// losing legacy NeoForge version semantics. Independent mixed-version XML must yield only the
    /// requested game's descriptor, through both configured source choices and the real loader cache.
    /// </summary>
    [TestMethod]
    [DataRow("official", "1.21.1", "21.1.2", "neoforge")]
    [DataRow("bmclapi", "1.21.1", "21.1.2", "neoforge")]
    [DataRow("bmclapi", "1.20.1", "47.1.1", "forge")]
    public async Task NeoForge_MavenCatalog_PreservesGameFilteringAndInstallDescriptor(string source, string game, string expected, string artifact) => await ReportStorage.RunAsync(async paths =>
    {
        using var transport = new SourceTransport(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent("<metadata><versioning><versions><version>21.1.2</version><version>21.2.1-beta</version><version>1.20.1-47.1.1</version></versions></versioning></metadata>")
        });
        using var http = new HttpClient(new NetworkSettings.MirrorHandler(new JsonObject { ["source"] = source }) { InnerHandler = transport });
        var loaders = new LoaderService(paths, http);
        var catalog = await loaders.ListAsync("neoforge", game, cancellationToken: TestContext.CancellationToken);
        Assert.HasCount(1, catalog.Versions);
        Assert.AreEqual(expected, catalog.Versions[0]["version"]?.ToString());
        var fullVersion = artifact == "forge" ? "1.20.1-47.1.1" : expected;
        Assert.AreEqual($"https://maven.neoforged.net/releases/net/neoforged/{artifact}/{fullVersion}/{artifact}-{fullVersion}-installer.jar", catalog.Versions[0]["installerUrl"]?.ToString());
        var root = source == "official" ? "https://maven.neoforged.net/releases/" : "https://bmclapi2.bangbang93.com/maven/";
        CollectionAssert.AreEqual(new[] { $"{root}net/neoforged/{artifact}/maven-metadata.xml" }, transport.Addresses);
        var offline = await loaders.ListAsync("neoforge", game, offline: true, cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual(expected, offline.Versions[0]["version"]?.ToString());
        Assert.HasCount(1, transport.Addresses);
    });

    /// <summary>
    /// Author review: ordinary metadata routing does not exercise the authentication transport's deliberate
    /// mirror bypass. Public artifact preparation must use the selected source, canonicalize the mirror's
    /// own download URL for verified fallback, and retain the SHA256/JAR checks without involving login.
    /// </summary>
    [TestMethod]
    public async Task LoginComponent_UsesPublicMirror_AndRecoversCanonicalOriginal() => await ReportStorage.RunAsync(async paths =>
    {
        var jar = LoginJar();
        var hash = Convert.ToHexStringLower(SHA256.HashData(jar));
        using var transport = new SourceTransport(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("latest.json", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = new StringContent($$$"""{"download_url":"https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/56/agent.jar","checksums":{"sha256":"{{{hash}}}"}}""") };
            var payload = (byte[])jar.Clone();
            if (request.RequestUri.Host == "bmclapi2.bangbang93.com") payload[^1] ^= 1;
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        });
        using var http = new HttpClient(new NetworkSettings.MirrorHandler(new JsonObject { ["source"] = "bmclapi" }) { InnerHandler = transport });
        var identities = new IdentityService(paths, http);
        var result = await identities.PrepareExternalLoginComponentAsync(ct: TestContext.CancellationToken);
        Assert.AreEqual(hash, result.Sha256);
        CollectionAssert.AreEqual(jar, await File.ReadAllBytesAsync(result.Path, TestContext.CancellationToken));
        CollectionAssert.AreEqual(ExpectedComponentRoutes, transport.Addresses);
        Assert.AreEqual(result, identities.GetExternalLoginComponent());
        Assert.IsFalse(Directory.EnumerateFiles(Path.GetDirectoryName(result.Path)!, "*.partial*").Any());
    });

    private static byte[] LoginJar()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("META-INF/MANIFEST.MF").Open()))
                writer.WriteLine("Premain-Class: moe.yushi.authlibinjector.Premain");
            zip.CreateEntry("moe/yushi/authlibinjector/Premain.class");
        }

        return stream.ToArray();
    }

    /// <summary>Gets or sets the runner deadline used by catalog and artifact preparation.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static readonly string[] ExpectedComponentRoutes =
        [
            "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json",
            "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/56/agent.jar",
            "https://authlib-injector.yushi.moe/artifact/56/agent.jar"
        ];
}