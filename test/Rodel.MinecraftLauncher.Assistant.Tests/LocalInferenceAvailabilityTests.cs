#if !RODEL_LOCAL_INFERENCE
using Rodel.MinecraftLauncher.Core.Application.AI;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects the dependency-free release boundary: retained local bindings cannot execute or silently switch providers.</summary>
[TestClass]
public sealed class LocalInferenceAvailabilityTests
{
    /// <summary>Both discovery and generation must reject before initializing an engine; chat creation has the same boundary.</summary>
    [TestMethod]
    [DataRow(null)]
    [DataRow("Read-only probe")]
    public async Task DisabledBuild_RejectsLocalRequestsWithoutInitializingOrSubstituting(string? prompt)
    {
        await using var runtime = new AiLocalInferenceRuntime();
        var snapshot = new AiProviderContext(AiProtocols.RodelInference, 1, "retained-binding", "retained-model.gguf",
            null, null, null, runtime);
        var error = await Assert.ThrowsAsync<AiProtocolException>(() =>
            AiProviderRequests.ExecuteAsync(snapshot, null, prompt, 5, TestContext.CancellationToken));
        Assert.AreEqual("AI_LOCAL_INFERENCE_UNAVAILABLE", error.Code);
        var chatError = Assert.Throws<AiProtocolException>(() => AiChatClients.Create(snapshot, null));
        Assert.AreEqual(error.Code, chatError.Code);
        var status = await runtime.StatusAsync(TestContext.CancellationToken);
        Assert.IsFalse(status.Available);
        Assert.IsFalse(status.Initialized);
        Assert.AreEqual(0L, status.EngineInstancesCreated);
    }

    /// <summary>Gets cancellation owned by the test runner.</summary>
    public TestContext TestContext { get; set; } = null!;
}
#endif