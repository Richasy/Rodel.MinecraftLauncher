using Microsoft.Extensions.AI;
using Rodel.Inference;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>
/// Protects Core's local-engine mode, borrowing and resource ownership. The fake replaces only the SDK/native engine;
/// these cases do not claim GGUF execution, which needs separate published-artifact/model evidence.
/// </summary>
[TestClass]
public sealed class LocalInferenceLifetimeTests
{
    /// <summary>
    /// Prevents the former Client-only integration and per-request reload: two requests share an Embedded engine,
    /// releasing a request leaves it resident, and explicit unload owns exactly that resource.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task RepeatedRequests_ReuseEmbeddedEngine_AndUnloadAllowsFreshCreation()
    {
        var created = new List<EngineStub>();
        await using var runtime = new AiLocalInferenceRuntime((options, _) =>
        {
            Assert.AreEqual(InferenceMode.Embedded, options.Mode);
            Assert.IsNull(options.Endpoint, "Local mode must not probe or require an independently hosted endpoint.");
            var engine = new EngineStub();
            created.Add(engine);
            return Task.FromResult<IAiLocalInferenceEngine>(engine);
        });

        await using (var request = await runtime.AcquireAsync(TestContext.CancellationToken))
            CollectionAssert.AreEqual(expected, (await request.Engine.GetModelsAsync(TestContext.CancellationToken)).ToArray());
        await using (var next = await runtime.AcquireAsync(TestContext.CancellationToken))
            Assert.AreSame(created[0], next.Engine);
        Assert.HasCount(1, created, "Releasing the first model/tool request must not reload the model on the next one.");
        Assert.AreEqual(0, created[0].Disposals);

        await runtime.UnloadAsync(TestContext.CancellationToken);
        Assert.AreEqual(1, created[0].Disposals);
        await using var reloaded = await runtime.AcquireAsync(TestContext.CancellationToken);
        Assert.HasCount(2, created);
        Assert.AreNotSame(created[0], reloaded.Engine);
    }

    /// <summary>
    /// Unloading while a real ownership lease is held must wait, not dispose an engine in use.
    /// Calling UnloadAsync inline reaches its first blocked await; no task scheduling or arbitrary delay establishes the boundary.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task ActiveRequestLease_DefersUnloadUntilRelease()
    {
        var engine = new EngineStub();
        await using var runtime = new AiLocalInferenceRuntime((_, _) => Task.FromResult<IAiLocalInferenceEngine>(engine));
        var request = await runtime.AcquireAsync(TestContext.CancellationToken);
        try
        {
            var unload = runtime.UnloadAsync(TestContext.CancellationToken);
            Assert.IsFalse(unload.IsCompleted);
            Assert.AreEqual(0, engine.Disposals);
            await request.DisposeAsync();
            await unload.WaitAsync(TestContext.CancellationToken);
            Assert.AreEqual(1, engine.Disposals);
        }
        finally { await request.DisposeAsync(); }
    }

    /// <summary>
    /// An initialization canceled at the factory boundary must release the owner gate and not cache a failed task,
    /// otherwise all later local requests would remain broken until restarting the launcher.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task CanceledInitialization_ReleasesGate_AndLaterRequestCanLoad()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        await using var runtime = new AiLocalInferenceRuntime(async (_, ct) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }

            return new EngineStub();
        });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var pending = runtime.AcquireAsync(cancellation.Token);
        await entered.Task.WaitAsync(TestContext.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);

        await using var retry = await runtime.AcquireAsync(TestContext.CancellationToken);
        CollectionAssert.AreEqual(expected, (await retry.Engine.GetModelsAsync(TestContext.CancellationToken)).ToArray());
        Assert.AreEqual(2, attempts);
    }

    /// <summary>
    /// A factory that unexpectedly attaches to a daemon must be rejected and disposed, never accepted as successful local loading.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task UnexpectedClientMode_DisposesWrongResource_WithoutCachingIt()
    {
        var engine = new EngineStub { Mode = InferenceMode.Client };
        await using var runtime = new AiLocalInferenceRuntime((_, _) => Task.FromResult<IAiLocalInferenceEngine>(engine));
        var error = await Assert.ThrowsAsync<AiProtocolException>(async () => await runtime.AcquireAsync(TestContext.CancellationToken));
        Assert.AreEqual("AI_LOCAL_INFERENCE_MODE_INVALID", error.Code);
        Assert.AreEqual(1, engine.Disposals);
        Assert.IsFalse((await runtime.StatusAsync(TestContext.CancellationToken)).Initialized);
    }

    private sealed class EngineStub : IAiLocalInferenceEngine
    {
        /// <inheritdoc />
        public InferenceMode Mode { get; init; } = InferenceMode.Embedded;
        /// <inheritdoc />
        public Uri? Endpoint => Mode == InferenceMode.Client ? new Uri("http://127.0.0.1:1/") : null;
        /// <inheritdoc />
        public string Explanation => "Synthetic native-engine boundary; no model files or daemon.";
        /// <summary>Gets how often this owned native-resource substitute was released.</summary>
        public int Disposals { get; private set; }
        /// <inheritdoc />
        public Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Disposals > 0, this);
            return Task.FromResult<IReadOnlyList<string>>(["local-fixture"]);
        }
        /// <inheritdoc />
        public Task<IChatClient> GetChatClientAsync(string model, CancellationToken ct, int? contextLength = null)
            => throw new NotSupportedException("These ownership cases do not substitute model generation.");
        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Gets or sets MSTest's bounded cancellation context.</summary>
    public TestContext TestContext { get; set; }

    private static readonly string[] expected = ["local-fixture"];
}