using Microsoft.AspNetCore.Http;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects the settings editor's atomic model selection and unsaved discovery boundaries.</summary>
[TestClass]
public sealed class ModelSelectionTests
{
    /// <summary>
    /// The official bare origin must reach the documented versioned routes for discovery and generation.
    /// Existing API prefixes, queries and third-party roots must retain their exact meaning; a blanket /v1 append
    /// would fix OpenAI discovery while breaking compatible services. This tests routing, without a private account.
    /// </summary>
    [TestMethod]
    [DataRow("https://api.openai.com", "models", "https://api.openai.com/v1/models")]
    [DataRow("https://api.openai.com/", "responses", "https://api.openai.com/v1/responses")]
    [DataRow("https://api.openai.com", "chat/completions", "https://api.openai.com/v1/chat/completions")]
    [DataRow("https://api.openai.com/v1/", "models", "https://api.openai.com/v1/models")]
    [DataRow("https://api.openai.com/v1/models", "models", "https://api.openai.com/v1/models")]
    [DataRow("https://api.openai.com/?project=fixture", "models", "https://api.openai.com/v1/models?project=fixture")]
    [DataRow("https://compatible.invalid/", "models", "https://compatible.invalid/models")]
    [DataRow("https://compatible.invalid/proxy/v2/?api-version=fixture", "models", "https://compatible.invalid/proxy/v2/models?api-version=fixture")]
    [DataRow("https://api.openai.com.compatible.invalid/", "models", "https://api.openai.com.compatible.invalid/models")]
    public void ProviderRoute_ResolvesOfficialRoot_AndPreservesExplicitCompatiblePaths(string root, string route, string expected)
        => Assert.AreEqual(expected, AiHttpTransport.Route(new Uri(root), route).AbsoluteUri);

    /// <summary>
    /// A bulk selection must retain the identity, parameters and unknown fields of retained models; validation and stale
    /// revisions must leave the complete file unchanged. This differs from single-model editing and connection patches.
    /// </summary>
    [TestMethod]
    public async Task Selection_RetainsBindingData_AndRejectsInvalidTransactionsBeforeWriting()
    {
        using var sandbox = new AiSandbox();
        var store = new AiServiceStore(sandbox.Paths);
        var state = await store.SaveServiceAsync(new()
        {
            Models = [new() { Model = "retained", Temperature = 0.7 }, new() { Model = "removed" }],
        }, ct: TestContext.CancellationToken);
        var service = state.Services.Single();
        var retained = service.Models[0];
        var path = Path.Combine(sandbox.Root, "ai", "services.json");
        var raw = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.CancellationToken))!;
        raw["services"]![0]!["models"]![0]!["future"] = "preserve";
        await File.WriteAllTextAsync(path, raw.ToJsonString(), TestContext.CancellationToken);
        var selected = await store.SelectModelsAsync(service.Id,
            [new() { Id = retained.Id }, new() { Model = "discovered" }], service.Revision, TestContext.CancellationToken);
        var actual = selected.Services.Single();
        Assert.HasCount(2, actual.Models);
        Assert.AreEqual(retained.Id, actual.Models[0].Id);
        Assert.AreEqual(0.7, actual.Models[0].Temperature);
        Assert.AreEqual("preserve", actual.Models[0].AdditionalData!["future"].GetString());
        Assert.AreEqual(retained.Id, selected.DefaultModelId);
        Assert.DoesNotContain(model => model.Model == "removed", actual.Models);
        var before = await File.ReadAllBytesAsync(path, TestContext.CancellationToken);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SelectModelsAsync(service.Id,
            [new() { Id = retained.Id }, new() { Model = "retained" }], actual.Revision, TestContext.CancellationToken));
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path, TestContext.CancellationToken));
        await Assert.ThrowsAsync<LauncherOperationException>(() => store.SelectModelsAsync(service.Id, [], service.Revision, TestContext.CancellationToken));
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path, TestContext.CancellationToken));
        var cleared = await store.SelectModelsAsync(service.Id, [], actual.Revision, TestContext.CancellationToken);
        Assert.IsNull(cleared.DefaultModelId);
        Assert.HasCount(0, cleared.Services.Single().Models);
    }

    /// <summary>
    /// Unsaved discovery must reject unapproved remote HTTP before networking and reach a loopback GET endpoint with
    /// the supplied request credential. The API has no storage owner and returns the provider's actual model identifiers.
    /// Generated public-base metadata must retain HTTP correlation, including null, without inventing saved-service fields.
    /// </summary>
    [TestMethod]
    public async Task UnsavedDiscovery_UsesActualCatalog_AndRequiresRemoteHttpConsent()
    {
        var reached = 0;
        string? authorization = null;
        await using var server = await ModelEndpoint.StartAsync(context =>
        {
            Interlocked.Increment(ref reached);
            authorization = context.Request.Headers.Authorization;
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync("{\"data\":[{\"id\":\"catalog-only\"}]}", context.RequestAborted);
        }, TestContext.CancellationToken, ["GET"]);
        var request = new AiServiceUpdate { Protocol = AiProtocols.OpenAiChat, BaseUrl = server.Endpoint.AbsoluteUri };
        await Assert.ThrowsAsync<LauncherOperationException>(() => AssistantService.DiscoverModelsAsync(request with { BaseUrl = "http://example.invalid/v1/" }, "synthetic-key", TestContext.CancellationToken));
        Assert.AreEqual(0, reached);
        var result = await AssistantService.DiscoverModelsAsync(request with { AllowUnencryptedHttp = true }, "synthetic-key", TestContext.CancellationToken);
        Assert.AreEqual(1, reached);
        Assert.AreEqual("Bearer synthetic-key", authorization);
        Assert.AreEqual("catalog-only", result.Models[0].Id);
        var wire = System.Text.Json.JsonSerializer.SerializeToNode(result, AiJsonContext.Default.AiModelCatalogReport)!.AsObject();
        Assert.IsTrue(wire.ContainsKey("requestId"));
        Assert.IsNull(wire["requestId"]);
        Assert.IsFalse(wire.ContainsKey("service"));
        Assert.IsFalse(wire.ContainsKey("modelBindingId"));
    }

    /// <summary>Gets or sets the test runner's cancellation context.</summary>
    public TestContext TestContext { get; set; }
}