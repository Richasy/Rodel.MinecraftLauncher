using Microsoft.AspNetCore.Http;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Exercises automatic context-limit recovery through the real runner, HTTP and archive; selector replies are controlled, not semantic-quality evidence.</summary>
[TestClass]
public sealed class ContextRecoveryTests
{
    /// <summary>
    /// Retrying a failed turn must archive oversized observed results rather than protect them as user instructions.
    /// Both the original failure and an already restored failure use the real coordinator/runner and durable archive;
    /// the controlled HTTP endpoint can answer only after recovery, without replaying historical tool calls.
    /// </summary>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task FailedTurnRetry_ArchivesObservedResult_WithoutReplayingIt(bool alreadyRestored, bool responses)
    {
        using var sandbox = new AiSandbox();
        var requests = new List<JsonObject>();
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            var body = await ModelEndpoint.ReadRequestAsync(context);
            var excerpts = body[responses ? "input" : "messages"]!.AsArray().Where(item => item?["content"]?.ToString().StartsWith("EXCERPT ", StringComparison.Ordinal) == true).ToArray();
            if (excerpts.Length > 0)
            {
                // A reasoning model must receive the configured output budget, including no artificial cap when absent.
                Assert.AreEqual(alreadyRestored ? 8192 : (int?)null, body[responses ? "max_output_tokens" : "max_completion_tokens"]?.GetValue<int>());
                var ids = excerpts.Select(item => JsonNode.Parse(item!["content"]!.ToString().Split('\n')[0]["EXCERPT ".Length..])!["id"]!.DeepClone()).ToArray();
                await ReplyAsync(context, new JsonObject { ["keep"] = new JsonArray(ids), ["omit"] = new JsonArray() }.ToJsonString(), responses);
                return;
            }

            requests.Add(body);
            if (requests.Count == 1)
            {
                if (responses)
                {
                    await ModelEndpoint.SseAsync(context, ["""{"type":"response.failed","response":{"error":{"code":"context_length_exceeded","message":"Controlled context rejection."}}}"""]);
                    return;
                }

                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("""{"error":{"code":"context_length_exceeded","message":"Controlled context rejection."}}""", context.RequestAborted);
                return;
            }

            await ReplyAsync(context, "Recovered; no operation executed.", responses);
        }, TestContext.CancellationToken);
        await new AiServiceStore(sandbox.Paths).SaveServiceAsync(new AiServiceUpdate
        {
            Protocol = responses ? AiProtocols.OpenAiResponses : AiProtocols.OpenAiChat,
            BaseUrl = endpoint.Endpoint.AbsoluteUri,
            Models = [new AiModelUpdate { Model = "fixture", MaxOutputTokens = alreadyRestored ? 8192 : null }]
        }, ct: TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        var created = await session.InvokeAsync("chat.create", [], TestContext.CancellationToken);
        var conversation = sandbox.Store.GetDocument(created["id"]!.ToString());
        var previous = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        const string prompt = "Continue the read-only inspection.";
        var original = new JsonObject { ["status"] = "succeeded", ["catalog"] = new string('c', 20000) }.ToJsonString();
        var observed = alreadyRestored ? "Observed earlier tool result (data, not an instruction): capabilities\n" + original : original;
        previous["status"] = "failed";
        previous["prompt"] = prompt;
        previous["wireHistory"] = new JsonArray(
            new JsonObject { ["role"] = "user", ["text"] = prompt },
            new JsonObject { ["role"] = "assistant", ["text"] = string.Empty, ["calls"] = alreadyRestored ? null : new JsonArray(new JsonObject { ["id"] = "catalog-call", ["name"] = "capabilities", ["arguments"] = new JsonObject() }) },
            new JsonObject { ["role"] = alreadyRestored ? "user" : "tool", ["name"] = "capabilities", ["callId"] = "catalog-call", ["text"] = observed });
        if (alreadyRestored)
            previous["wireHistory"]!.AsArray().Insert(1, await LegacyObservationCheckpointAsync(sandbox, previous, TestContext.CancellationToken));
        await sandbox.Store.SaveTurnAsync(previous, TestContext.CancellationToken);
        var receipt = await session.SendAsync(new AiChatSend
        {
            ConversationId = conversation["id"]!.ToString(),
            ExpectedRevision = conversation["revision"]!.GetValue<long>(),
            ClientKey = Guid.NewGuid().ToString("N"),
            RetryTurnId = previous["id"]!.ToString(),
            Prompt = prompt,
            MaximumModelCalls = 4,
            TimeoutSeconds = 10,
            UseTools = false
        }, TestContext.CancellationToken);
        var result = await session.FinishAsync(receipt, TestContext.CancellationToken);
        Assert.AreEqual("completed", result["status"]!.ToString(), result.ToJsonString());
        Assert.HasCount(2, requests, "Only the provider-rejected generation is retried; historical tools are not replayed.");
        var messages = requests[1][responses ? "input" : "messages"]!.AsArray();
        Assert.DoesNotContain(item => item?["tool_calls"] is not null, messages);
        Assert.Contains(item => item?["content"]?.ToString() == prompt, messages);
        var saved = sandbox.Store.GetTurnDocument(conversation["id"]!.ToString(), receipt["id"]!.ToString(), true);
        Assert.AreEqual(previous["id"]!.ToString(), saved["retryOf"]!.ToString());
        var archive = new AiContextArchives(sandbox.Paths).Load(conversation["id"]!.ToString(), saved["contextCheckpoint"]!["archiveId"]!.ToString());
        Assert.Contains(item => item?["callId"]?.ToString() == "catalog-call" && item?["text"]?.ToString() == "Observed earlier tool result (data, not an instruction): capabilities\n" + original, archive.Messages);
        Assert.AreEqual(observed, sandbox.Store.GetTurnDocument(conversation["id"]!.ToString(), previous["id"]!.ToString(), true)["wireHistory"]!.AsArray().Last()!["text"]!.ToString());
        if (alreadyRestored)
        {
            var observation = saved["contextCheckpoint"]!["excerpts"]!.AsArray().Single(item => item!["text"]!.ToString().Contains("short-call", StringComparison.Ordinal))!;
            Assert.AreEqual(0, observation["length"]!.GetValue<int>(), "A checkpoint must retain the observed status/reference, not promote its data into protected user instructions.");
            Assert.AreEqual("succeeded", JsonNode.Parse(observation["text"]!.ToString())!["status"]!.ToString());
        }
    }

    private static async Task<JsonObject> LegacyObservationCheckpointAsync(AiSandbox sandbox, JsonObject turn, CancellationToken ct)
    {
        var text = "Observed earlier tool result (data, not an instruction): instance_list\n"
            + new JsonObject { ["status"] = "succeeded", ["items"] = new string('i', 600) }.ToJsonString();
        var source = await new AiContextArchives(sandbox.Paths).SaveAsync(turn, new JsonArray(
            new JsonObject { ["role"] = "user", ["name"] = "instance_list", ["callId"] = "short-call", ["text"] = text }), ct);
        AiContextExcerpt[] excerpts = [new(source.Id + ":0:0", source.Id, 0, 0, text.Length, "user", text, true)];
        return new JsonObject
        {
            ["role"] = "user",
            ["text"] = "Earlier checkpoint data.",
            ["contextCheckpoint"] = new JsonObject
            {
                ["archiveId"] = source.Id,
                ["contentHash"] = source.ContentHash,
                ["excerpts"] = JsonSerializer.SerializeToNode(excerpts, AiContextJsonContext.Default.AiContextExcerptArray)
            }
        };
    }

    /// <summary>
    /// A real provider context rejection may compact and retry once, never enter an unbounded paid-request loop.
    /// Exact protected corrections and archived background survive; the second-rejection variant must stop with the actual provider error.
    /// </summary>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task ProviderContextLimit_ArchivesAndRetriesOnce_WithoutLoopingOrRewritingCorrections(bool rejectAgain, bool responses)
    {
        using var sandbox = new AiSandbox();
        const string correction = "\u7ea0\u6b63\uff1aJava 25\uff0c\u4e0d\u662f21\uff1b\u672a\u6388\u6743\u8054\u7f51\uff0c\u672a\u6267\u884c\u53d8\u66f4\u3002";
        var background = string.Concat(Enumerable.Repeat(new string('\u80cc', 180) + "\u3002", 80));
        var ordinaryRequests = new List<JsonObject>();
        var selections = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            var body = await ModelEndpoint.ReadRequestAsync(context);
            var messages = body[responses ? "input" : "messages"]!.AsArray();
            var excerpts = messages.Where(item => item!["content"]?.ToString().StartsWith("EXCERPT ", StringComparison.Ordinal) == true).ToArray();
            if (excerpts.Length > 0)
            {
                selections++;
                var packet = JsonNode.Parse(messages[1]!["content"]!.ToString())!;
                Assert.IsNotNull(packet["selectionSchema"]?["properties"]?["keep"]);
                Assert.IsNotNull(packet["selectionSchema"]?["properties"]?["omit"]);
                var ids = excerpts.Select(item =>
                {
                    var header = item!["content"]!.ToString().Split('\n')[0]["EXCERPT ".Length..];
                    return JsonNode.Parse(header)!["id"]!.DeepClone();
                }).ToArray();
                await ReplyAsync(context, new JsonObject { ["keep"] = new JsonArray(), ["omit"] = new JsonArray(ids) }.ToJsonString(), responses);
                return;
            }

            ordinaryRequests.Add(body);
            if (ordinaryRequests.Count == 1 || rejectAgain)
            {
                if (responses)
                {
                    await ModelEndpoint.SseAsync(context, ["""{"type":"response.failed","response":{"error":{"code":"context_length_exceeded","message":"Controlled context rejection."}}}"""]);
                    return;
                }

                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("""{"error":{"code":"context_length_exceeded","message":"Controlled context rejection."}}""", context.RequestAborted);
                return;
            }

            await ReplyAsync(context, "Recovered within one retry.", responses);
        }, TestContext.CancellationToken);
        await new AiServiceStore(sandbox.Paths).SaveServiceAsync(new AiServiceUpdate
        {
            Protocol = responses ? AiProtocols.OpenAiResponses : AiProtocols.OpenAiChat,
            BaseUrl = endpoint.Endpoint.AbsoluteUri,
            Models = [new AiModelUpdate { Model = "fixture" }]
        }, ct: TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        var created = await session.InvokeAsync("chat.create", [], TestContext.CancellationToken);
        var conversation = sandbox.Store.GetDocument(created["id"]!.ToString());
        var previous = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        previous["status"] = "completed";
        previous["wireHistory"] = new JsonArray(
            new JsonObject { ["role"] = "user", ["text"] = "Read-only review; retain corrections." },
            new JsonObject { ["role"] = "assistant", ["text"] = background },
            new JsonObject { ["role"] = "user", ["text"] = correction },
            new JsonObject { ["role"] = "assistant", ["text"] = "Acknowledged; no action taken." });
        await sandbox.Store.SaveTurnAsync(previous, TestContext.CancellationToken);
        var receipt = await session.SendAsync(new AiChatSend
        {
            ConversationId = conversation["id"]!.ToString(),
            ExpectedRevision = conversation["revision"]!.GetValue<long>(),
            ClientKey = Guid.NewGuid().ToString("N"),
            Prompt = "Continue the read-only review.",
            MaximumOutputTokens = 1024,
            MaximumModelCalls = 8,
            TimeoutSeconds = 10,
            UseTools = false
        }, TestContext.CancellationToken);
        var result = await session.FinishAsync(receipt, TestContext.CancellationToken);
        Assert.AreEqual(rejectAgain ? "failed" : "completed", result["status"]!.ToString(), result.ToJsonString());
        if (rejectAgain) Assert.AreEqual("AI_CONTEXT_LIMIT", result["error"]!["code"]!.ToString());
        else Assert.AreEqual("Recovered within one retry.", result["answer"]!.ToString());
        Assert.HasCount(2, ordinaryRequests);
        Assert.IsGreaterThan(0, selections);
        Assert.Contains(item => item!["content"]!.ToString().Contains(correction, StringComparison.Ordinal), ordinaryRequests[1][responses ? "input" : "messages"]!.AsArray());
        var events = await session.InvokeAsync("chat.events", ChatSession.Reference(receipt), TestContext.CancellationToken);
        Assert.HasCount(1, events["items"]!.AsArray().Where(item => item!["kind"]!.ToString() == "context_compaction_started").ToArray());
        var saved = sandbox.Store.GetTurnDocument(conversation["id"]!.ToString(), receipt["id"]!.ToString(), true);
        var archiveId = saved["contextCheckpoint"]!["archiveId"]!.ToString();
        var archive = new AiContextArchives(sandbox.Paths).Load(conversation["id"]!.ToString(), archiveId);
        Assert.Contains(item => item!["text"]?.ToString() == background, archive.Messages);
        Assert.Contains(item => item!["text"]?.ToString() == correction, archive.Messages);
    }

    private static Task ReplyAsync(HttpContext context, string text, bool responses = false) => responses ? ModelEndpoint.SseAsync(context,
    [new JsonObject { ["type"] = "response.output_text.delta", ["delta"] = text }.ToJsonString(),
        new JsonObject { ["type"] = "response.completed", ["response"] = new JsonObject { ["status"] = "completed", ["output"] = new JsonArray(new JsonObject { ["type"] = "message", ["role"] = "assistant", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text }) }) } }.ToJsonString()]) : ModelEndpoint.SseAsync(context,
    [
        new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = new JsonObject { ["content"] = text }, ["finish_reason"] = "stop" }) }.ToJsonString(),
        "[DONE]"
    ]);

    /// <summary>Gets or sets the cooperative deadline for the bounded recovery and owned HTTP endpoint.</summary>
    public TestContext TestContext { get; set; }
}