using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Tests;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
#if RODEL_LOCAL_INFERENCE
using System.Runtime.CompilerServices;
#endif
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Checks real model spans/metrics and private-data exclusion without configuring any external exporter.</summary>
[TestClass]
public sealed class AiTelemetryTests
{
    private const string PrivatePrompt = "PRIVATE-SAVE-DATA";
    private const string PrivateAnswer = "PRIVATE-ANSWER-DATA";
    private const string PrivateArgument = "PRIVATE-TOOL-ARG";
    private const string PrivateResult = "PRIVATE-RESULT-DATA";
    private const string PrivateDeclaration = "PRIVATE-DECLARATION-DATA";
    private const string Credential = "synthetic-not-a-real-key-2468";
    private const string PrivateQuery = "PRIVATE-ENDPOINT-QUERY";

    /// <summary>
    /// Merely adding a package or leaving SDK defaults is insufficient: actual requests must emit usage and error spans,
    /// while upstream error bodies stay in explicit diagnostics rather than span status. A synthetic-only unsafe control
    /// enables body capture to prove the leak oracle is capable of detecting a broken privacy configuration.
    /// </summary>
    [TestMethod]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task Pipeline_RecordsUsageAndFailures_WithoutCapturingPrivateBodies()
    {
        var prefix = "telemetry-" + Guid.NewGuid().ToString("N");
        using var capture = new TraceCapture(prefix);
        var requests = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            _ = await ModelEndpoint.ReadRequestAsync(context);
            var number = Interlocked.Increment(ref requests);
            if (number == 2)
            {
                await ModelEndpoint.SseAsync(context,
                [
                    new JsonObject { ["error"] = new JsonObject { ["message"] = PrivatePrompt + " " + Credential } }.ToJsonString()
                ]);
            }
            else
            {
                await ModelEndpoint.SseAsync(context, SuccessFrames());
            }
        }, TestContext.CancellationToken);
        var address = new UriBuilder(endpoint.Endpoint) { Query = "token=" + PrivateQuery }.Uri;
        var snapshot = new AiProviderContext("openai-chat", 1, "binding", prefix + "-model", Credential, 0, 64);
        var options = new ChatOptions
        {
            Tools = [AiToolDeclarations.Create("read_note", PrivateDeclaration, new JsonObject { ["type"] = "object" })]
        };
        using var client = AiChatClients.Create(snapshot, address);
        var response = await client.GetResponseAsync(Input(), options, TestContext.CancellationToken);
        Assert.AreEqual(18L, response.Usage!.TotalTokenCount);
        var error = await Assert.ThrowsAsync<AiProtocolException>(async () =>
            await client.GetResponseAsync(Input(), options, TestContext.CancellationToken));
        Assert.AreEqual("AI_PROVIDER_ERROR", error.Code);
        Assert.Contains(PrivatePrompt, error.Details!["providerMessage"]!.ToString());
        Assert.DoesNotContain(Credential, error.Details.ToJsonString());

        using var unsafeControl = AiChatClients.Create(snapshot with { Model = prefix + "-control" }, address);
        unsafeControl.GetService<OpenTelemetryChatClient>()!.EnableSensitiveData = true;
        await unsafeControl.GetResponseAsync(Input(), options, TestContext.CancellationToken);
        Assert.AreEqual(3, requests);
        var safe = capture.Activities.Where(activity => Model(activity) == snapshot.Model).ToArray();
        Assert.HasCount(2, safe);
        Assert.Contains(activity => activity.Status == ActivityStatusCode.Error, safe);
        var recorded = string.Join("\n", safe.Select(Contents));
        foreach (var value in new[] { PrivatePrompt, PrivateAnswer, PrivateArgument, PrivateResult, PrivateDeclaration, Credential, PrivateQuery })
            Assert.DoesNotContain(value, recorded);
        var control = capture.Activities.Single(activity => Model(activity) == prefix + "-control");
        Assert.Contains(PrivatePrompt, Contents(control), "The negative-control oracle must actually observe SDK body capture.");
        var counts = capture.Counts.Where(item => item.Model == snapshot.Model).Select(item => item.Value).ToArray();
        Assert.Contains(11, counts);
        Assert.Contains(7, counts);
    }

    /// <summary>
    /// A file path can be a valid local or remote-compatible model reference, but MEAI records model IDs even with
    /// sensitive-body capture disabled. Both streamed SDK metadata and explicit HTTP ChatOptions must use a diagnostic
    /// alias while the actual request/caller identity stay unchanged; no model files are opened.
    /// </summary>
    [TestMethod]
#if RODEL_LOCAL_INFERENCE
    [DataRow("rodel-inference")]
#endif
    [DataRow("openai-chat")]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task ModelFileReference_StaysOutOfTelemetry_WithoutChangingActualIdentity(string protocol)
    {
        var binding = "file-model-" + Guid.NewGuid().ToString("N");
        var model = Path.Combine(Path.GetTempPath(), "PRIVATE-MODEL-DIRECTORY", "selected.gguf");
        using var capture = new TraceCapture(binding);
#if RODEL_LOCAL_INFERENCE
        await using var runtime = new AiLocalInferenceRuntime((_, _) =>
            Task.FromResult<IAiLocalInferenceEngine>(new MetadataEngine()));
#else
        await using var runtime = new AiLocalInferenceRuntime();
#endif
        await using var endpoint = protocol == "rodel-inference" ? null : await ModelEndpoint.StartAsync(async context =>
        {
            var request = await ModelEndpoint.ReadRequestAsync(context);
            Assert.AreEqual(model, request["model"]!.GetValue<string>());
            await ModelEndpoint.SseAsync(context, SuccessFrames());
        }, TestContext.CancellationToken);
        var snapshot = new AiProviderContext(protocol, 1, binding, model, null, 0, 32, runtime);
        ChatResponse response;
        if (protocol == "rodel-inference")
        {
            response = await AiModelStream.RunAsync(snapshot, null, [new ChatMessage(ChatRole.User, "Read-only fixture.")],
                [], 32, (_, _) => Task.CompletedTask, _ => Task.CompletedTask, TestContext.CancellationToken);
        }
        else
        {
            using var client = AiChatClients.Create(snapshot, endpoint!.Endpoint);
            var options = new ChatOptions { ModelId = model };
            response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Read-only fixture.")], options, TestContext.CancellationToken);
            Assert.AreEqual(model, options.ModelId);
        }

        Assert.AreEqual(model, response.ModelId);
        var activity = capture.Activities.Single(item => Model(item) == binding);
        Assert.DoesNotContain("PRIVATE-MODEL-DIRECTORY", Contents(activity));
        Assert.DoesNotContain(model, Contents(activity));
    }

#if RODEL_LOCAL_INFERENCE
    private sealed class MetadataEngine : IAiLocalInferenceEngine
    {
        /// <inheritdoc />
        public Rodel.Inference.InferenceMode Mode => Rodel.Inference.InferenceMode.Embedded;
        /// <inheritdoc />
        public Uri? Endpoint => null;
        /// <inheritdoc />
        public string Explanation => "Synthetic metadata only; no GGUF file is opened.";
        /// <inheritdoc />
        public Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken ct) => throw new NotSupportedException();
        /// <inheritdoc />
        public Task<IChatClient> GetChatClientAsync(string model, CancellationToken ct, int? contextLength = null)
            => Task.FromResult<IChatClient>(new MetadataClient(model));
        /// <inheritdoc />
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MetadataClient(string model) : IChatClient
    {
        /// <inheritdoc />
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        /// <inheritdoc />
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new()
            {
                Role = ChatRole.Assistant,
                ModelId = model,
                FinishReason = ChatFinishReason.Stop,
                Contents = [new TextContent("Read-only metadata fixture.")]
            };
        }
        /// <inheritdoc />
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        /// <inheritdoc />
        public void Dispose() { }
    }

#endif

    private static ChatMessage[] Input() =>
    [
        new(ChatRole.Assistant, [new FunctionCallContent("prior", "read_note", new Dictionary<string, object?> { ["path"] = PrivateArgument })]),
        new(ChatRole.Tool, [new FunctionResultContent("prior", PrivateResult)]),
        new(ChatRole.User, PrivatePrompt)
    ];

    private static string[] SuccessFrames() =>
    [
        new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject
            {
                ["index"] = 0, ["delta"] = new JsonObject { ["content"] = PrivateAnswer }, ["finish_reason"] = "stop"
            }),
            ["usage"] = new JsonObject { ["prompt_tokens"] = 11, ["completion_tokens"] = 7, ["total_tokens"] = 18 }
        }.ToJsonString(),
        "[DONE]"
    ];

    private static string? Model(Activity activity) => activity.GetTagItem("gen_ai.request.model") as string;
    private static string Contents(Activity activity) => activity.DisplayName + "\n" + activity.StatusDescription + "\n"
        + string.Join("\n", activity.TagObjects.Select(tag => tag.Key + "=" + tag.Value))
        + string.Join("\n", activity.Events.SelectMany(item => item.Tags).Select(tag => tag.Key + "=" + tag.Value));

    private sealed class TraceCapture : IDisposable
    {
        private readonly ActivityListener _activities;
        private readonly MeterListener _metrics;
        /// <summary>Gets spans produced only by this case's uniquely named model.</summary>
        public ConcurrentQueue<Activity> Activities { get; } = new();
        /// <summary>Gets actual token measurements emitted by the MEAI pipeline.</summary>
        public ConcurrentQueue<(string Model, int Value)> Counts { get; } = new();
        /// <summary>Starts in-process listeners; no collector, logger or exporter is installed.</summary>
        public TraceCapture(string prefix)
        {
            _activities = new()
            {
                ShouldListenTo = source => source.Name == AiChatClients.TelemetrySource,
                Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (Model(activity)?.StartsWith(prefix, StringComparison.Ordinal) == true) Activities.Enqueue(activity);
                }
            };
            ActivitySource.AddActivityListener(_activities);
            _metrics = new()
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == AiChatClients.TelemetrySource) listener.EnableMeasurementEvents(instrument);
                }
            };
            _metrics.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (tag.Key == "gen_ai.request.model" && tag.Value is string model && model.StartsWith(prefix, StringComparison.Ordinal))
                        Counts.Enqueue((model, value));
                }
            });
            _metrics.Start();
        }
        /// <inheritdoc />
        public void Dispose()
        {
            _metrics.Dispose();
            _activities.Dispose();
        }
    }

    /// <summary>Gets or sets the deadline for real loopback requests.</summary>
    public TestContext TestContext { get; set; }
}