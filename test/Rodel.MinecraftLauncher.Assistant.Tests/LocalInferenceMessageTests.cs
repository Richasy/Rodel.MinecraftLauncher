using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using InferenceMode = Rodel.Inference.InferenceMode;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Exercises the real local MEAI pipeline with only the SDK/native generation boundary substituted.</summary>
[TestClass]
public sealed class LocalInferenceMessageTests
{
    /// <summary>
    /// Local continuation must retain standard protected reasoning, function results and declaration-only tools;
    /// reconstructed text-only messages would lose that context. Returned contents and accounting must survive
    /// aggregation without converting large argument numbers, booleans or explicit nulls into strings.
    /// HTTP fixtures and engine-lifetime tests do not exercise this SDK boundary; no model weights are loaded.
    /// Tool images must follow the whole completed result batch as observed user media: the consumed SDK otherwise
    /// splits a sibling image into an orphan tool message without a call ID.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task StandardContents_ReachLocalClient_AndSurviveStreamingAggregation()
    {
        var client = new FixtureClient();
        await using var runtime = new AiLocalInferenceRuntime((_, _) =>
            Task.FromResult<IAiLocalInferenceEngine>(new FixtureEngine(client)));
        var context = new AiProviderContext("rodel-inference", 4, "local-binding", "fixture", null, 0, 128, runtime);
        var previous = new ChatMessage(ChatRole.Assistant,
        [
            new TextReasoningContent("先前推理") { ProtectedData = "prior-protected-state" },
            new FunctionCallContent("prior-call", "read_note", new Dictionary<string, object?> { ["path"] = "原文.txt" }),
            new FunctionCallContent("second-call", "read_note", new Dictionary<string, object?> { ["path"] = "next.txt" })
        ]);
        AiChatMessages.Bind(previous, new("rodel-inference", "local-binding", 4, new JsonObject()));
        var result = new ChatMessage(ChatRole.Tool,
            [new FunctionResultContent("prior-call", """{"observed":true}"""), new DataContent(new byte[] { 1, 2, 3 }, "image/png")]);
        var second = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("second-call", """{"observed":true}""")]);
        var tool = AiToolDeclarations.Create("read_note", "Read a selected note.", JsonNode.Parse("""{"type":"object","properties":{}}""")!.AsObject());
        var visible = new StringBuilder();
        var response = await AiModelStream.RunAsync(context, null,
            [previous, result, second, new ChatMessage(ChatRole.User, "继续")], [tool], 32,
            (_, text) => { visible.Append(text); return Task.CompletedTask; }, _ => Task.CompletedTask, TestContext.CancellationToken);

        Assert.AreEqual("prior-protected-state", client.Messages[0].Contents.OfType<TextReasoningContent>().Single().ProtectedData);
        Assert.AreEqual("prior-call", client.Messages[1].Contents.OfType<FunctionResultContent>().Single().CallId);
        Assert.AreEqual("""{"observed":true}""", client.Messages[1].Contents.OfType<FunctionResultContent>().Single().Result);
        Assert.AreEqual("second-call", client.Messages[2].Contents.OfType<FunctionResultContent>().Single().CallId);
        Assert.IsTrue(client.Messages.Where(message => message.Role == ChatRole.Tool).All(message => !message.Contents.OfType<DataContent>().Any()));
        Assert.AreEqual(ChatRole.User, client.Messages[3].Role);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, client.Messages[3].Contents.OfType<DataContent>().Single().Data.ToArray());
        Assert.Contains("prior-call", client.Messages[3].Text);
        Assert.AreEqual("继续", client.Messages[4].Text);
        Assert.AreEqual(32, client.Options!.MaxOutputTokens);
        Assert.IsInstanceOfType<AIFunctionDeclaration>(client.Options.Tools!.Single());
        Assert.AreEqual("completed", AiChatMessages.Status(response));
        Assert.AreEqual("已答😀", response.Text);
        Assert.AreEqual("next-protected-state", response.Messages.Single().Contents.OfType<TextReasoningContent>().Single().ProtectedData);
        Assert.IsNotNull(response.Usage);
        Assert.AreEqual(18L, response.Usage.TotalTokenCount);
        Assert.AreEqual(5L, response.Usage.ReasoningTokenCount);
        var call = response.Messages.Single().Contents.OfType<FunctionCallContent>().Single();
        var arguments = AiChatMessages.Arguments(call);
        Assert.AreEqual(9223372036854775806L, arguments["large"]!.GetValue<long>());
        Assert.IsTrue(arguments["flag"]!.GetValue<bool>());
        Assert.IsTrue(arguments.ContainsKey("nullable"));
        Assert.IsNull(arguments["nullable"]);
        Assert.Contains("已答😀", visible.ToString());
        Assert.DoesNotContain("protected-state", visible.ToString());
    }

    private sealed class FixtureEngine(IChatClient client) : IAiLocalInferenceEngine
    {
        /// <inheritdoc />
        public InferenceMode Mode => InferenceMode.Embedded;
        /// <inheritdoc />
        public Uri? Endpoint => null;
        /// <inheritdoc />
        public string Explanation => "Controlled MEAI client, not native model generation.";
        /// <inheritdoc />
        public Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken ct) => throw new NotSupportedException();
        /// <inheritdoc />
        public Task<IChatClient> GetChatClientAsync(string model, CancellationToken ct, int? contextLength = null) => Task.FromResult(client);
        /// <inheritdoc />
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixtureClient : IChatClient
    {
        /// <summary>Gets the actual messages supplied to the SDK boundary.</summary>
        public ChatMessage[] Messages { get; private set; } = [];
        /// <summary>Gets the actual generation options supplied to the SDK boundary.</summary>
        public ChatOptions? Options { get; private set; }
        /// <inheritdoc />
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        /// <inheritdoc />
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Messages = [.. messages];
            Options = options;
            return Updates(cancellationToken);
        }
        /// <inheritdoc />
        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType == typeof(IChatClient) ? this : null;
        /// <inheritdoc />
        public void Dispose() { }

        private static async IAsyncEnumerable<ChatResponseUpdate> Updates([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield return new() { MessageId = "reply", Role = ChatRole.Assistant, Contents = [new TextReasoningContent("本次推理") { ProtectedData = "next-protected-state" }] };
            yield return new() { MessageId = "reply", Contents = [new TextContent("已答😀")] };
            yield return new()
            {
                MessageId = "reply",
                Contents = [new FunctionCallContent("next-call", "read_note", new Dictionary<string, object?>
                {
                    ["large"] = 9223372036854775806L, ["nullable"] = null, ["flag"] = true
                })]
            };
            yield return new()
            {
                Contents = [new UsageContent(new() { InputTokenCount = 11, OutputTokenCount = 7, TotalTokenCount = 18, ReasoningTokenCount = 5 })],
                FinishReason = ChatFinishReason.ToolCalls
            };
        }
    }

    /// <summary>Gets or sets the bounded generation cancellation context.</summary>
    public TestContext TestContext { get; set; }
}