using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects server-token priority and advisory preflight; character-compaction boundaries are covered separately.</summary>
[TestClass]
public sealed class TokenBudgetTests
{
    /// <summary>
    /// Base64 is not image-token usage: counting it as text can trigger destructive or needless compaction.
    /// Compare different image byte sizes with identical text; without a provider count the incomplete estimate
    /// must be explicit and must not claim a known near-limit decision.
    /// </summary>
    [TestMethod]
    public async Task ImageBudget_ExcludesBinarySize_AndDoesNotPretendToKnowImageTokens()
    {
        var provider = new AiProviderContext("openai-chat", 1, "fixture", "fixture", null, null, null);
        var small = new ChatMessage(ChatRole.User, [new TextContent("Use this reference."), new DataContent(new byte[10], "image/png")]);
        var large = new ChatMessage(ChatRole.User, [new TextContent("Use this reference."), new DataContent(new byte[100000], "image/png")]);
        Assert.AreEqual(AiTokenCounter.Estimate(provider, [small], [], null), AiTokenCounter.Estimate(provider, [large], [], null));
        using var counter = new AiTokenCounter(provider, null);
        var measurement = await counter.MeasureAsync([large], [], null, 1, TestContext.CancellationToken);
        Assert.IsNotNull(measurement.EstimatedInputTokens);
        Assert.AreEqual("tiktoken_text_only_estimate", measurement.CountSource);
        Assert.IsFalse(measurement.ImageTokensIncluded);
        Assert.IsFalse((measurement with { ContextTokens = 1 }).NearLimit(1));
    }

    /// <summary>The count API must see the actual tools/input, and an input-only ceiling must not deduct reserved output twice.</summary>
    [TestMethod]
    [DataRow(AiProviderProtocols.OpenAiResponses)]
    [DataRow(AiProviderProtocols.AnthropicMessages)]
    [DataRow(AiProviderProtocols.Gemini)]
    public async Task ProviderCount_UsesWireInputAndOverridesLocalEstimate(string protocol)
    {
        JsonObject? counted = null;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            if (context.Request.Method == "GET")
            {
                await context.Response.WriteAsync("""{"inputTokenLimit":1000}""", context.RequestAborted);
            }
            else
            {
                counted = await ModelEndpoint.ReadRequestAsync(context);
                await context.Response.WriteAsync(protocol == AiProviderProtocols.Gemini ? """{"totalTokens":321}""" : """{"input_tokens":321}""", context.RequestAborted);
            }
        }, TestContext.CancellationToken, ["GET", "POST"], tokenApis: true);
        var provider = new AiProviderContext(protocol, 1, "model-id", "fixture", null, null, 1024);
        JsonArray history = [new JsonObject { ["role"] = "user", ["text"] = "中文问题，以工具的真实资料为准。" }];
        AIFunctionDeclaration[] tools = [AiToolDeclarations.Create("inspect", "Read the actual configuration.", new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() })];
        using var counter = new AiTokenCounter(provider, endpoint.Endpoint);
        var result = await counter.MeasureAsync(AiChatHistory.Read(history), tools, 1024, 1, TestContext.CancellationToken);
        Assert.AreEqual(321L, result.InputTokens);
        Assert.AreEqual("provider_token_count", result.CountSource);
        Assert.IsFalse(result.NearLimit(10000), "Provider inputTokenLimit is independent of reserved output.");
        Assert.IsNotNull(counted);
        var input = protocol == AiProviderProtocols.Gemini ? counted["generateContentRequest"]! : counted;
        Assert.IsNotNull(input["tools"]);
        Assert.Contains("中文问题", input.ToJsonString(new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Assert.IsTrue(new AiTokenMeasurement(321, null, 2000, null, "estimate", "fixture").NearLimit(1900));
    }

    /// <summary>An advisory selector must neither consume the last generation slot nor turn a selector 503 into a failed user request.</summary>
    [TestMethod]
    [DataRow(1)]
    [DataRow(4)]
    public async Task AdvisoryPreflight_ReservesGenerationAndDefersSelectorFailure(int maximumCalls)
    {
        using var sandbox = new AiSandbox();
        var generationCalls = 0;
        var selectorCalls = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            if (context.Request.Method == "GET")
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("""{"context_length":100}""", context.RequestAborted);
                return;
            }

            var body = await ModelEndpoint.ReadRequestAsync(context);
            if (body["messages"]!.AsArray().Any(item => item!["content"]?.ToString().StartsWith("EXCERPT ", StringComparison.Ordinal) == true))
            {
                selectorCalls++;
                context.Response.StatusCode = 503;
                await context.Response.WriteAsync("""{"error":{"message":"Selector unavailable."}}""", context.RequestAborted);
                return;
            }

            generationCalls++;
            await ReplyAsync(context);
        }, TestContext.CancellationToken, ["GET", "POST"]);
        await ConfigureAsync(sandbox, endpoint.Endpoint);
        await using var session = new ChatSession(sandbox);
        var created = await session.InvokeAsync("chat.create", [], TestContext.CancellationToken);
        var conversation = sandbox.Store.GetDocument(created["id"]!.ToString());
        var prior = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        prior["status"] = "completed";
        prior["wireHistory"] = new JsonArray(new JsonObject { ["role"] = "user", ["text"] = "Only read; retain the original data." },
            new JsonObject { ["role"] = "assistant", ["text"] = string.Concat(Enumerable.Repeat("Ordinary prior background. ", 1000)) });
        await sandbox.Store.SaveTurnAsync(prior, TestContext.CancellationToken);
        var receipt = await session.SendAsync(new AiChatSend
        {
            ConversationId = conversation["id"]!.ToString(),
            ExpectedRevision = conversation["revision"]!.GetValue<long>(),
            ClientKey = Guid.NewGuid().ToString("N"),
            Prompt = "Continue read-only.",
            UseTools = false,
            MaximumModelCalls = maximumCalls
        }, TestContext.CancellationToken);
        var result = await session.FinishAsync(receipt, TestContext.CancellationToken);
        Assert.AreEqual("completed", result["status"]!.ToString(), result.ToJsonString());
        Assert.AreEqual(1, generationCalls);
        Assert.AreEqual(maximumCalls == 1 ? 0 : 1, selectorCalls);
    }

    /// <summary>A provider-accepted request above the former 64K character ceiling must complete and numeric usage must calibrate the next request.</summary>
    [TestMethod]
    public async Task UnknownCapacity_LargeAcceptedRequestAndUsageCalibrationContinue()
    {
        using var sandbox = new AiSandbox();
        var calls = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context => { calls++; await ReplyAsync(context); }, TestContext.CancellationToken);
        await ConfigureAsync(sandbox, endpoint.Endpoint);
        await using var session = new ChatSession(sandbox);
        var conversation = await session.InvokeAsync("chat.create", [], TestContext.CancellationToken);
        var prior = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        prior["status"] = "completed";
        prior["wireHistory"] = new JsonArray(new JsonObject { ["role"] = "user", ["text"] = "Read-only background." },
            new JsonObject { ["role"] = "assistant", ["text"] = string.Concat(Enumerable.Repeat("这是先前的普通背景。\n", 6600)) });
        await sandbox.Store.SaveTurnAsync(prior, TestContext.CancellationToken);
        for (var index = 0; index < 2; index++)
        {
            var id = conversation["id"]!.ToString();
            conversation = sandbox.Store.GetDocument(id);
            var receipt = await session.SendAsync(new AiChatSend
            {
                ConversationId = id,
                ExpectedRevision = conversation["revision"]!.GetValue<long>(),
                ClientKey = Guid.NewGuid().ToString("N"),
                Prompt = "继续解释，不执行操作。",
                UseTools = false
            }, TestContext.CancellationToken);
            var result = await session.FinishAsync(receipt, TestContext.CancellationToken);
            Assert.AreEqual("completed", result["status"]!.ToString(), result.ToJsonString());
            var saved = sandbox.Store.GetTurnDocument(id, receipt["id"]!.ToString(), true);
            Assert.AreEqual(index == 0 ? "tiktoken_o200k_estimate" : "tiktoken_o200k_calibrated_estimate", saved["contextBudget"]!["countSource"]!.ToString());
        }

        Assert.AreEqual(2, calls);
        var profile = JsonNode.Parse(await File.ReadAllTextAsync(Directory.GetFiles(Path.Combine(sandbox.Root, "ai", "token-calibration")).Single(), TestContext.CancellationToken))!;
        Assert.AreEqual(2, profile["samples"]!.GetValue<int>());
        Assert.DoesNotContain("中", profile.ToJsonString());
    }

    /// <summary>
    /// A hidden 4096 default truncated real skin tools. Exercise the full Core/provider wire with no configured limit:
    /// exact-host catalog capacity is usable, native metadata supersedes it, and another deployment's value is not guessed.
    /// Existing input-count cases cannot detect outgoing output limits or distinguish metadata sources.
    /// </summary>
    [TestMethod]
    [DataRow("catalog", 120000)]
    [DataRow("provider", 98304)]
    [DataRow("unknown", 0)]
    public async Task UnconfiguredOutput_UsesObservedCapacity_OrOmitsTheLimit(string source, int expected)
    {
        using var sandbox = new AiSandbox();
        JsonObject? sent = null;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            if (context.Request.Method == "GET")
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(source == "provider"
                    ? """{"context_length":1000000,"max_output_tokens":98304}""" : "{}", context.RequestAborted);
                return;
            }

            sent = await ModelEndpoint.ReadRequestAsync(context);
            if (sent["stream"]?.GetValue<bool>() == true)
            {
                await ReplyAsync(context);
            }
            else
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("""{"choices":[{"message":{"content":"Accepted."},"finish_reason":"stop"}]}""", context.RequestAborted);
            }
        }, TestContext.CancellationToken, ["GET", "POST"]);
        var catalogDirectory = Path.Combine(sandbox.Paths.Root, "ai", "model-catalog");
        Directory.CreateDirectory(catalogDirectory);
        var catalog = new JsonObject
        {
            ["providers"] = new JsonArray(new JsonObject
            {
                ["id"] = "fixture",
                ["api"] = source == "unknown" ? "https://another-deployment.invalid/" : endpoint.Endpoint.AbsoluteUri,
                ["models"] = new JsonArray(new JsonObject { ["id"] = "fixture", ["context"] = 1000000, ["output"] = 120000 })
            })
        };
        await File.WriteAllTextAsync(Path.Combine(catalogDirectory, "snapshot.json"), catalog.ToJsonString(), TestContext.CancellationToken);
        await ConfigureAsync(sandbox, endpoint.Endpoint);
        await using var session = new ChatSession(sandbox);
        var created = await session.InvokeAsync("chat.create", [], TestContext.CancellationToken);
        var conversation = sandbox.Store.GetDocument(created["id"]!.ToString());
        var receipt = await session.SendAsync(new AiChatSend
        {
            ConversationId = conversation["id"]!.ToString(),
            ExpectedRevision = conversation["revision"]!.GetValue<long>(),
            ClientKey = Guid.NewGuid().ToString("N"),
            Prompt = "Continue without inventing an output cap.",
            UseTools = false,
        }, TestContext.CancellationToken);
        var result = await session.FinishAsync(receipt, TestContext.CancellationToken);
        Assert.AreEqual("completed", result["status"]!.ToString());
        Assert.IsNotNull(sent);
        if (expected == 0) Assert.IsFalse(sent.ContainsKey("max_completion_tokens"));
        else Assert.AreEqual(expected, sent["max_completion_tokens"]!.GetValue<int>());
        await new AssistantService(sandbox.Paths).GenerateAsync(new() { Prompt = "Use the same model capacity without persistent chat." }, ct: TestContext.CancellationToken);
        if (expected == 0) Assert.IsFalse(sent.ContainsKey("max_completion_tokens"));
        else Assert.AreEqual(expected, sent["max_completion_tokens"]!.GetValue<int>());
    }

    private Task<AiServiceConfiguration> ConfigureAsync(AiSandbox sandbox, Uri endpoint) => new AiServiceStore(sandbox.Paths).SaveServiceAsync(new AiServiceUpdate
    { Protocol = AiProtocols.OpenAiChat, BaseUrl = endpoint.AbsoluteUri, Models = [new AiModelUpdate { Model = "fixture" }] }, ct: TestContext.CancellationToken);
    private static Task ReplyAsync(HttpContext context) => ModelEndpoint.SseAsync(context,
    ["""{"choices":[{"index":0,"delta":{"content":"Accepted."},"finish_reason":"stop"}],"usage":{"prompt_tokens":1200,"completion_tokens":3}}""", "[DONE]"]);
    /// <summary>Gets or sets cancellation for the isolated transport and Core session.</summary>
    public TestContext TestContext { get; set; }
}