using System.Net;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects last-good metadata/artwork across bad updates and distinguishes proxy reference data from deployment limits.</summary>
[TestClass]
public sealed class PublicModelCatalogTests
{
    /// <summary>An invalid refresh must preserve the committed snapshot; a cached logo works offline and public HTTP never inherits a model credential.</summary>
    [TestMethod]
    public async Task InvalidUpdate_PreservesOfflineSnapshotAndAnonymousLogo()
    {
        using var sandbox = new AiSandbox();
        var requests = 0;
        var broken = false;
        using var http = new HttpClient(new CatalogHandler(request =>
        {
            requests++;
            Assert.AreEqual("models.dev", request.RequestUri!.Host);
            Assert.IsNull(request.Headers.Authorization);
            return request.RequestUri.AbsolutePath.EndsWith(".svg", StringComparison.Ordinal)
                ? """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path fill="currentColor" d="M0 0h24v24H0z"/></svg>"""
                : broken ? "{}" : """{"fixture-provider":{"api":"https://provider.example/v1","models":{"fixture":{"limit":{"context":100000,"output":4096},"canonical_model_id":"fixture-lab/fixture"}}}}""";
        }));
        await File.WriteAllTextAsync(Path.Combine(sandbox.Root, "snapshot.json"), "null", TestContext.CancellationToken);
        var catalog = new AiPublicModelCatalog(sandbox.Root, http);
        await catalog.RefreshAsync();
        var (Provider, Lab, Svg) = await catalog.IdentityAsync("fixture", new Uri("https://provider.example/v1/"), TestContext.CancellationToken);
        Assert.AreEqual("fixture-provider", Provider);
        Assert.IsNotNull(Svg);
        broken = true;
        await catalog.RefreshAsync();
        Assert.AreEqual(3, requests);
        var reopened = new AiPublicModelCatalog(sandbox.Root, http);
        var local = reopened.Read();
        Assert.AreEqual(100000L, AiPublicModelCatalog.Find("fixture", new Uri("https://provider.example/"), local).Context);
        Assert.IsFalse(AiPublicModelCatalog.Find("fixture", new Uri("https://unknown-proxy.example/"), local).MatchedProvider);
        var offline = await reopened.IdentityAsync("fixture", new Uri("https://provider.example/"), TestContext.CancellationToken);
        Assert.AreEqual(Svg, offline.Svg);
        Assert.AreEqual(3, requests, "Neither reopening a fresh snapshot nor reading its cached logo should make an HTTP request.");
    }

    private sealed class CatalogHandler(Func<HttpRequestMessage, string> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply(request)) });
    }

    /// <summary>Gets or sets cancellation for this isolated metadata/artwork workflow.</summary>
    public TestContext TestContext { get; set; }
}