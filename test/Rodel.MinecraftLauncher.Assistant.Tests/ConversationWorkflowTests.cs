using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>
/// Migrates Core chat lifecycle checks from the old CLI polling probe. The coordinator, runner, HTTP and stores are real;
/// synthetic provider replies replace only an account. Launcher/third-party stdio process acceptance is separate.
/// </summary>
[TestClass]
public sealed class ConversationWorkflowTests
{
    /// <summary>
    /// Historical pictures omitted from the live checkpoint must be recoverable through the actual archive-read tool,
    /// not only retained on disk. A real archived standard message and two HTTP turns prove the exact message-index
    /// route supplies image contents; the provider reply is scripted and no image or model account is decoded.
    /// </summary>
    [TestMethod]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task ExactArchiveMessageRead_RestoresImageToTheNextModelRequest()
    {
        using var sandbox = new AiSandbox();
        var calls = 0;
        JsonObject? wire = null;
        string? archiveId = null;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            calls++;
            wire = await ModelEndpoint.ReadRequestAsync(context);
            if (calls == 1)
            {
                var arguments = new JsonObject { ["archiveId"] = archiveId, ["messageIndex"] = 0 };
                await ModelEndpoint.SseAsync(context,
                [
                    new JsonObject
                    {
                        ["choices"] = new JsonArray(new JsonObject
                        {
                            ["index"] = 0, ["finish_reason"] = "tool_calls",
                            ["delta"] = new JsonObject
                            {
                                ["tool_calls"] = new JsonArray(new JsonObject
                                {
                                    ["index"] = 0, ["id"] = "archive-read", ["type"] = "function",
                                    ["function"] = new JsonObject { ["name"] = "conversation_history_read", ["arguments"] = arguments.ToJsonString() }
                                })
                            }
                        })
                    }.ToJsonString(), "[DONE]"
                ]);
            }
            else
            {
                await ModelEndpoint.SseAsync(context, Answer("openai-chat"));
            }
        }, TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        var id = await ConfigureAsync(sandbox, session, "openai-chat", endpoint.Endpoint);
        var conversation = sandbox.Store.GetDocument(id);
        var previous = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        previous["status"] = "completed";
        await sandbox.Store.SaveTurnAsync(previous, TestContext.CancellationToken);
        var archived = new ChatMessage(ChatRole.User, [new TextContent("Archived reference."), new DataContent(new byte[] { 1, 2, 3 }, "image/png")]);
        archiveId = (await new AiContextArchives(sandbox.Paths).SaveAsync(previous,
            new JsonArray(AiChatHistory.Write(archived)), TestContext.CancellationToken)).Id;
        var receipt = await session.SendAsync(Request(id, "Read the original archived picture.", 1) with
        {
            UseTools = true,
            MaximumModelCalls = 2
        }, TestContext.CancellationToken);
        var completed = await session.FinishAsync(receipt, TestContext.CancellationToken);
        Assert.AreEqual("completed", completed["status"]!.ToString(), completed.ToJsonString());
        // The journal must contain both sides of the actual exchange independently of the turn snapshot.
        var journal = await File.ReadAllLinesAsync(Path.Combine(sandbox.Root, "ai", "conversations", id, "events.jsonl"), TestContext.CancellationToken);
        var records = journal.Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        Assert.AreEqual("Read the original archived picture.", records.Single(item => item["kind"]?.ToString() == "user_message")["data"]?["text"]?.ToString());
        Assert.AreEqual(completed["answer"]?.ToString(), records.Single(item => item["kind"]?.ToString() == "turn_finished")["data"]?["answer"]?.ToString());
        Assert.AreEqual(2, calls);
        Assert.IsNotNull(wire);
        var observation = wire["messages"]!.AsArray().Last()!["content"]!.AsArray();
        Assert.Contains("conversation_history_read", observation[0]!["text"]!.ToString());
        Assert.AreEqual("data:image/png;base64,AQID", observation[1]!["image_url"]!["url"]!.ToString());
    }

    /// <summary>
    /// Simulates the durable crash boundary after an image result was journaled, with or without its subsequent
    /// tool-message save. The real next-turn runner/HTTP must restore exactly one actual image as observed data,
    /// never base64 prose, an executable old call, or a second dispatch. No MCP child or model account is substituted
    /// for that recovery decision; the endpoint only supplies the final answer.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task InterruptedImageResult_RecoversFromIntentOrCompleteMessage_WithoutReplay(bool completeMessageSaved)
    {
        using var sandbox = new AiSandbox();
        JsonObject? wire = null;
        var calls = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            calls++;
            wire = await ModelEndpoint.ReadRequestAsync(context);
            await ModelEndpoint.SseAsync(context, Answer("openai-chat"));
        }, TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        var id = await ConfigureAsync(sandbox, session, "openai-chat", endpoint.Endpoint);
        var conversation = sandbox.Store.GetDocument(id);
        var previous = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        AiMcpContent[] images = [new("image") { Data = [1, 2, 3], MediaType = "image/png", ByteLength = 3 }];
        var result = new JsonObject
        {
            ["status"] = "succeeded",
            ["imageContent"] = JsonSerializer.SerializeToNode(images, AssistantJsonContext.Default.AiMcpContentArray)
        };
        previous["status"] = "interrupted";
        previous["wireHistory"] = new JsonArray(
            new JsonObject { ["role"] = "user", ["text"] = "Inspect the preview." },
            new JsonObject
            {
                ["role"] = "assistant",
                ["text"] = string.Empty,
                ["calls"] = new JsonArray(new JsonObject
                { ["id"] = "preview-call", ["name"] = "external_preview", ["arguments"] = new JsonObject() })
            });
        previous["intents"] = new JsonArray(new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString("N"),
            ["callId"] = "preview-call",
            ["name"] = "external_preview",
            ["state"] = "result_recorded",
            ["arguments"] = new JsonObject(),
            ["result"] = result.DeepClone()
        });
        if (completeMessageSaved)
        {
            var recorded = AiChatMessages.ToolResult("preview-call", result);
            AiChatMessages.SetToolName(recorded, "external_preview");
            previous["wireHistory"]!.AsArray().Add((JsonNode)AiChatHistory.Write(AiChatMessages.ToolMessage(recorded)));
        }

        await sandbox.Store.SaveTurnAsync(previous, TestContext.CancellationToken);
        var receipt = await session.SendAsync(Request(id, "Continue from the observed preview.", 1), TestContext.CancellationToken);
        var completed = await session.FinishAsync(receipt, TestContext.CancellationToken);
        Assert.AreEqual("completed", completed["status"]!.ToString(), completed.ToJsonString());
        Assert.AreEqual(1, calls);
        Assert.IsNotNull(wire);
        var messages = wire["messages"]!.AsArray();
        Assert.DoesNotContain(message => message?["tool_calls"] is not null || message?["role"]?.ToString() == "tool", messages);
        var observation = messages.Single(message => message?["content"] is JsonArray)!["content"]!.AsArray();
        Assert.Contains("Observed earlier tool result", observation[0]!["text"]!.ToString());
        Assert.AreEqual("data:image/png;base64,AQID", observation[1]!["image_url"]!["url"]!.ToString());
        var original = sandbox.Store.GetTurnDocument(id, previous["id"]!.ToString(), true);
        Assert.AreEqual("AQID", original["intents"]![0]!["result"]!["imageContent"]![0]!["data"]!.ToString());
    }

    /// <summary>
    /// A second turn must reload exact history and the same provider's opaque continuation without exposing that state publicly.
    /// Repeating the original client key must return its turn, not send another request; this is beyond single-stream parser coverage.
    /// </summary>
    [TestMethod]
    [DataRow("openai-chat")]
    [DataRow("openai-responses")]
    [DataRow("anthropic-messages")]
    [DataRow("gemini")]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task TwoTurns_PreserveContinuationAndPublicHistory_WithoutReplayingClientKey(string protocol)
    {
        using var sandbox = new AiSandbox();
        var requests = new List<JsonObject>();
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            requests.Add(await ModelEndpoint.ReadRequestAsync(context));
            await ModelEndpoint.SseAsync(context, Answer(protocol));
        }, TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        var conversation = await ConfigureAsync(sandbox, session, protocol, endpoint.Endpoint);
        var firstRequest = Request(conversation, "记住原始编号 MIGR-582。", 1);
        var first = await session.SendAsync(firstRequest, TestContext.CancellationToken);
        var completed = await session.FinishAsync(first, TestContext.CancellationToken);
        Assert.AreEqual("completed", completed["status"]!.ToString(), completed.ToJsonString());
        Assert.AreEqual("Retained MIGR-582.", completed["answer"]!.ToString());
        var duplicate = await session.SendAsync(firstRequest, TestContext.CancellationToken);
        Assert.AreEqual(first["id"]!.ToString(), duplicate["id"]!.ToString());
        Assert.HasCount(1, requests);

        var second = await session.SendAsync(Request(conversation, "原始编号是什么？", 2), TestContext.CancellationToken);
        Assert.AreEqual("completed", (await session.FinishAsync(second, TestContext.CancellationToken))["status"]!.ToString());
        Assert.HasCount(2, requests);
        var wire = requests[1].ToJsonString();
        Assert.Contains("MIGR-582", wire);
        if (protocol != "openai-chat") Assert.Contains("opaque-checkpoint", wire);
        var reopened = new AiChatStore(sandbox.Paths).GetTurnDocument(conversation, second["id"]!.ToString(), true);
        Assert.IsNotNull(reopened["wireHistory"]);
        Assert.Contains("MIGR-582", reopened["wireHistory"]!.ToJsonString());
        Assert.IsNull((await session.InvokeAsync("chat.turn", ChatSession.Reference(second), TestContext.CancellationToken))["wireHistory"]);
        var events = await session.InvokeAsync("chat.events", ChatSession.Reference(second), TestContext.CancellationToken);
        Assert.DoesNotContain("opaque-checkpoint", events.ToJsonString());
    }

    /// <summary>
    /// The open HTTP stream and durable delta establish overlap before enqueueing. Canceling one queued message must
    /// prevent its HTTP dispatch. Active-only cancellation starts the next message; cancel-queued atomically discards
    /// the queue first so the Stop action cannot accidentally start a later request. Both retain partial output.
    /// Extends the original admission/cancellation boundary rather than duplicating its endpoint and persistence checks.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task CancelOpenStream_PreservesPartialEvents_AndHonorsQueueCancellation(bool cancelQueued)
    {
        using var sandbox = new AiSandbox();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            if (Interlocked.Increment(ref calls) != 1)
            {
                await ModelEndpoint.SseAsync(context, Answer("openai-chat"));
                return;
            }

            await ModelEndpoint.SseAsync(context,
            [
                new JsonObject { ["choices"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["index"] = 0, ["delta"] = new JsonObject { ["content"] = new string('看', 600) }
                }) }.ToJsonString()
            ]);
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { disconnected.SetResult(); }
        }, TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        var conversation = await ConfigureAsync(sandbox, session, "openai-chat", endpoint.Endpoint);
        var first = await session.SendAsync(Request(conversation, "慢速回答。", 1), TestContext.CancellationToken);
        await entered.Task.WaitAsync(TestContext.CancellationToken);
        await session.EventAsync(first, "text_delta", TestContext.CancellationToken);
        var skipped = await session.SendAsync(Request(conversation, "取消此排队消息。", 2), TestContext.CancellationToken);
        var next = await session.SendAsync(Request(conversation, "继续。", 3), TestContext.CancellationToken);
        Assert.AreEqual("queued", next["status"]!.ToString());
        Assert.AreEqual(1, calls, "The queued turn must not reach the provider while the first stream is open.");
        await session.InvokeAsync("chat.cancel", ChatSession.Reference(skipped), TestContext.CancellationToken);
        Assert.AreEqual("canceled", (await session.InvokeAsync("chat.turn", ChatSession.Reference(skipped), TestContext.CancellationToken))["status"]!.ToString());
        // A terminal selection simulates stale UI: stop-with-queue must still cancel the conversation's actual owner.
        var cancellation = ChatSession.Reference(cancelQueued ? skipped : first);
        cancellation["cancelQueued"] = cancelQueued;
        await session.InvokeAsync("chat.cancel", cancellation, TestContext.CancellationToken);
        var canceled = await session.FinishAsync(first, TestContext.CancellationToken);
        await disconnected.Task.WaitAsync(TestContext.CancellationToken);
        Assert.AreEqual("canceled", canceled["status"]!.ToString());
        var events = await session.InvokeAsync("chat.events", ChatSession.Reference(first), TestContext.CancellationToken);
        Assert.Contains(item => item!["kind"]!.ToString() == "text_delta", events["items"]!.AsArray());
        Assert.AreEqual(cancelQueued ? "canceled" : "completed", (await session.FinishAsync(next, TestContext.CancellationToken))["status"]!.ToString());
        Assert.AreEqual(cancelQueued ? 1 : 2, calls);
    }

    /// <summary>
    /// A provider barrier holds the first complete response until later submissions are durably queued. The next
    /// request must include that final answer, exclude a canceled future prompt and reuse its idempotent receipt.
    /// This protects FIFO history independently of the partial-stream cancellation case; only model output is scripted.
    /// </summary>
    [TestMethod]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task QueuedTurn_UsesCompletedPredecessorHistory_AndDoesNotDuplicateSubmission()
    {
        using var sandbox = new AiSandbox();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<JsonObject>();
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            requests.Add(await ModelEndpoint.ReadRequestAsync(context));
            if (requests.Count == 1)
            {
                entered.SetResult();
                await release.Task.WaitAsync(context.RequestAborted);
            }

            await ModelEndpoint.SseAsync(context, Answer("openai-chat"));
        }, TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        var conversation = await ConfigureAsync(sandbox, session, "openai-chat", endpoint.Endpoint);
        var first = await session.SendAsync(Request(conversation, "Remember MIGR-582.", 1), TestContext.CancellationToken);
        await entered.Task.WaitAsync(TestContext.CancellationToken);
        var request = Request(conversation, "Continue after the first answer.", 2);
        var next = await session.SendAsync(request, TestContext.CancellationToken);
        var skipped = await session.SendAsync(Request(conversation, "FUTURE-MUST-NOT-LEAK", 3), TestContext.CancellationToken);
        await session.InvokeAsync("chat.cancel", ChatSession.Reference(skipped), TestContext.CancellationToken);
        var duplicate = await session.SendAsync(request, TestContext.CancellationToken);
        Assert.AreEqual(next["id"]!.ToString(), duplicate["id"]!.ToString());
        Assert.AreEqual("queued", sandbox.Store.GetTurn(conversation, next["id"]!.ToString()).Status);
        Assert.HasCount(1, requests);
        release.SetResult();
        Assert.AreEqual("completed", (await session.FinishAsync(first, TestContext.CancellationToken))["status"]!.ToString());
        Assert.AreEqual("completed", (await session.FinishAsync(next, TestContext.CancellationToken))["status"]!.ToString());
        Assert.HasCount(2, requests);
        var wire = requests[1].ToJsonString();
        Assert.Contains("Retained MIGR-582.", wire);
        Assert.Contains("Continue after the first answer.", wire);
        Assert.DoesNotContain("FUTURE-MUST-NOT-LEAK", wire);
    }

    /// <summary>
    /// Deletion must cancel the active HTTP stream and its queued successor before removing history. An explicit
    /// provider-entry barrier proves the deletion crosses execution; the second prompt must never reach HTTP and
    /// another conversation must survive. Existing history-only tests do not own a runner or pending credentials.
    /// </summary>
    [TestMethod]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task RemoveActiveConversation_CancelsItsQueue_AndPreservesOtherHistory()
    {
        using var sandbox = new AiSandbox();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        }, TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        var conversation = await ConfigureAsync(sandbox, session, "openai-chat", endpoint.Endpoint);
        var other = await session.InvokeAsync("chat.create", [], TestContext.CancellationToken);
        await session.SendAsync(Request(conversation, "Active request.", 1), TestContext.CancellationToken);
        await entered.Task.WaitAsync(TestContext.CancellationToken);
        await session.SendAsync(Request(conversation, "Must be canceled with its history.", 2), TestContext.CancellationToken);
        var review = await session.InvokeAsync("chat.review_remove", new() { ["conversationId"] = conversation }, TestContext.CancellationToken);
        var result = await session.InvokeAsync("chat.remove", new()
        {
            ["conversationId"] = conversation,
            ["expectedRevision"] = review["revision"]!.DeepClone(),
            ["fingerprint"] = review["fingerprint"]!.DeepClone(),
            ["confirm"] = true
        }, TestContext.CancellationToken);
        Assert.IsTrue(result["removed"]!.GetValue<bool>());
        Assert.AreEqual(1, calls);
        Assert.AreEqual(other["id"]!.ToString(), sandbox.Store.Get(other["id"]!.ToString()).Id);
        Assert.IsFalse(Directory.Exists(Path.Combine(sandbox.Root, "ai", "conversations", conversation)));
    }

    /// <summary>
    /// A real ask_user invocation holds the middle of a three-tool MEAI batch. Queued input must wait for its
    /// explicit answer and the final tool, then reach the very next HTTP request with all results. The foreign-only
    /// variant rejects cross-conversation interruption; existing FIFO/priority cases never cross a tool boundary.
    /// Independent wire call IDs detect truncated/replayed batches, while canceled and later prompts must stay out.
    /// </summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    [Timeout(20000, CooperativeCancellation = true)]
    public async Task QueuedInput_ContinuesAtNextModelBoundary_OnlyForItsOwnConversation(bool sameConversation)
    {
        using var sandbox = new AiSandbox();
        var requests = new List<JsonObject>();
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            requests.Add(await ModelEndpoint.ReadRequestAsync(context));
            await ModelEndpoint.SseAsync(context, requests.Count == 1 ? QueuedInputToolBatch() : Answer("openai-chat"));
        }, TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        var conversation = await ConfigureAsync(sandbox, session, "openai-chat", endpoint.Endpoint);
        var other = (await session.InvokeAsync("chat.create", [], TestContext.CancellationToken))["id"]!.ToString();
        var first = await session.SendAsync(Request(conversation, "ORIGINAL-TASK", 1) with { UseTools = true }, TestContext.CancellationToken);
        await session.EventAsync(first, "user_prompt_required", TestContext.CancellationToken);
        var foreign = await session.SendAsync(Request(other, "OTHER-CONVERSATION", 1), TestContext.CancellationToken);
        JsonObject? followUp = null;
        JsonObject? later = null;
        if (sameConversation)
        {
            followUp = await session.SendAsync(Request(conversation, "FOLLOW-UP", 2) with { AllowOperations = true }, TestContext.CancellationToken);
            later = await session.SendAsync(Request(conversation, "LATER-QUEUED", 3), TestContext.CancellationToken);
        }

        var canceled = await session.SendAsync(Request(conversation, "CANCELED-INPUT", sameConversation ? 4 : 2), TestContext.CancellationToken);
        await session.InvokeAsync("chat.cancel", ChatSession.Reference(canceled), TestContext.CancellationToken);
        var waiting = await session.InvokeAsync("chat.turn", ChatSession.Reference(first), TestContext.CancellationToken);
        Assert.AreEqual("awaiting_user", waiting["status"]!.ToString(), "Enqueuing input must not cancel a running tool or answer its question.");
        Assert.HasCount(1, requests);
        var response = ChatSession.Reference(first);
        response["promptId"] = waiting["pendingUserPrompt"]!["id"]!.DeepClone();
        response["text"] = "EXPLICIT-TOOL-ANSWER";
        await session.InvokeAsync("chat.respond", response, TestContext.CancellationToken);
        var finished = await session.FinishAsync(first, TestContext.CancellationToken);

        Assert.AreEqual("completed", finished["status"]!.ToString(), finished.ToJsonString());
        Assert.IsNull(finished["error"]);
        var nextMessages = requests[1]["messages"]!.AsArray();
        foreach (var callId in new[] { "discover-input", "wait-input", "read-context" })
        {
            var result = nextMessages.Single(message => message?["tool_call_id"]?.ToString() == callId);
            Assert.AreEqual("succeeded", JsonNode.Parse(result!["content"]!.ToString())!["status"]!.ToString());
        }

        var wire = requests[1].ToJsonString();
        Assert.Contains("EXPLICIT-TOOL-ANSWER", wire);
        Assert.DoesNotContain("OTHER-CONVERSATION", wire);
        Assert.DoesNotContain("CANCELED-INPUT", wire);
        Assert.DoesNotContain("LATER-QUEUED", wire);
        if (sameConversation)
        {
            Assert.Contains("FOLLOW-UP", wire, "The next model request must see the queued clarification, rather than finish the old task first.");
            Assert.AreEqual("allow_operations", sandbox.Store.GetTurn(conversation, followUp!["id"]!.ToString()).Mode);
            Assert.AreEqual("completed", sandbox.Store.GetTurn(conversation, later!["id"]!.ToString()).Status);
            Assert.Contains("LATER-QUEUED", requests[3].ToJsonString());
        }

        var foreignWire = requests[2].ToJsonString();
        Assert.Contains("OTHER-CONVERSATION", foreignWire);
        Assert.DoesNotContain("ORIGINAL-TASK", foreignWire);
        Assert.AreEqual(sameConversation ? "queued_message" : "stop", finished["finishReason"]!.ToString());
        Assert.HasCount(sameConversation ? 4 : 3, requests);
        Assert.AreEqual("completed", sandbox.Store.GetTurn(other, foreign["id"]!.ToString()).Status);
    }

    private static string[] QueuedInputToolBatch() =>
    [
        """{"choices":[{"index":0,"finish_reason":"tool_calls","delta":{"tool_calls":[{"index":0,"id":"discover-input","type":"function","function":{"name":"tools_discover","arguments":"{\"group\":\"conversation\",\"names\":[\"ask_user\",\"conversation_context_read\"]}"}},{"index":1,"id":"wait-input","type":"function","function":{"name":"ask_user","arguments":"{\"question\":\"Which direction?\",\"options\":[]}"}},{"index":2,"id":"read-context","type":"function","function":{"name":"conversation_context_read","arguments":"{}"}}]}}]}""", "[DONE]"
    ];

    private async Task<string> ConfigureAsync(AiSandbox sandbox, ChatSession session, string protocol, Uri endpoint)
    {
        await new AiServiceStore(sandbox.Paths).SaveServiceAsync(new AiServiceUpdate
        {
            Protocol = protocol,
            BaseUrl = endpoint.AbsoluteUri,
            Models = [new AiModelUpdate { Model = "fixture" }]
        }, ct: TestContext.CancellationToken);
        return (await session.InvokeAsync("chat.create", [], TestContext.CancellationToken))["id"]!.ToString();
    }

    private static AiChatSend Request(string conversation, string prompt, long revision) => new()
    {
        ConversationId = conversation,
        Prompt = prompt,
        ClientKey = Guid.NewGuid().ToString("N"),
        ExpectedRevision = revision,
        UseTools = false,
        MaximumOutputTokens = 256,
        TimeoutSeconds = 10
    };

    /// <summary>
    /// A real open provider stream establishes the active turn before three messages queue. Promoting the middle
    /// message must cancel that stream once, preserve the other messages' order, and carry its completed answer
    /// into the older queued message. This fails if priority changes only UI order or history follows creation order.
    /// </summary>
    [TestMethod]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task RunQueuedNow_InterruptsCurrent_PreservesRemainingOrderAndExecutionHistory()
    {
        using var sandbox = new AiSandbox();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var priorityEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePriority = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new System.Collections.Concurrent.ConcurrentQueue<JsonObject>();
        var calls = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            requests.Enqueue(await ModelEndpoint.ReadRequestAsync(context));
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                await ModelEndpoint.SseAsync(context, [new JsonObject { ["choices"] = new JsonArray(new JsonObject
                {
                    ["index"] = 0, ["delta"] = new JsonObject { ["content"] = new string('看', 600) }
                }) }.ToJsonString()]);
                entered.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { disconnected.SetResult(); }

                return;
            }

            if (call == 2)
            {
                priorityEntered.SetResult();
                await releasePriority.Task.WaitAsync(context.RequestAborted);
                await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"delta":{"content":"Priority reply retained."},"finish_reason":"stop"}]}""", "[DONE]"]);
            }
            else
            {
                await ModelEndpoint.SseAsync(context, Answer("openai-chat"));
            }
        }, TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        var conversation = await ConfigureAsync(sandbox, session, "openai-chat", endpoint.Endpoint);
        var first = await session.SendAsync(Request(conversation, "ACTIVE", 1), TestContext.CancellationToken);
        await entered.Task.WaitAsync(TestContext.CancellationToken);
        await session.EventAsync(first, "text_delta", TestContext.CancellationToken);
        var older = await session.SendAsync(Request(conversation, "OLDER-QUEUED", 2), TestContext.CancellationToken);
        var priority = await session.SendAsync(Request(conversation, "PRIORITY", 3), TestContext.CancellationToken);
        var remaining = await session.SendAsync(Request(conversation, "REMAINING-QUEUED", 4), TestContext.CancellationToken);
        Assert.AreEqual("queued", priority["status"]!.ToString());
        await session.InvokeAsync("chat.run_queued", ChatSession.Reference(priority), TestContext.CancellationToken);
        await disconnected.Task.WaitAsync(TestContext.CancellationToken);
        await priorityEntered.Task.WaitAsync(TestContext.CancellationToken);
        var duplicate = await session.InvokeAsync("chat.run_queued", ChatSession.Reference(priority), TestContext.CancellationToken);
        Assert.AreEqual(priority["id"]!.ToString(), duplicate["id"]!.ToString());
        Assert.AreEqual(2, calls, "Repeating a priority action must not cancel or resubmit its now-active message.");
        releasePriority.SetResult();
        Assert.AreEqual("completed", (await session.FinishAsync(remaining, TestContext.CancellationToken))["status"]!.ToString());
        Assert.AreEqual("canceled", sandbox.Store.GetTurn(conversation, first["id"]!.ToString()).Status);
        Assert.AreEqual("completed", sandbox.Store.GetTurn(conversation, older["id"]!.ToString()).Status);
        Assert.AreEqual("completed", sandbox.Store.GetTurn(conversation, priority["id"]!.ToString()).Status);
        var sent = requests.ToArray();
        Assert.HasCount(4, sent);
        Assert.Contains("PRIORITY", sent[1].ToJsonString());
        Assert.DoesNotContain("OLDER-QUEUED", sent[1].ToJsonString());
        Assert.DoesNotContain("REMAINING-QUEUED", sent[1].ToJsonString());
        Assert.Contains("OLDER-QUEUED", sent[2].ToJsonString());
        Assert.Contains("Priority reply retained.", sent[2].ToJsonString());
        Assert.DoesNotContain("REMAINING-QUEUED", sent[2].ToJsonString());
        Assert.Contains("REMAINING-QUEUED", sent[3].ToJsonString());
        Assert.Contains("OLDER-QUEUED", sent[3].ToJsonString());
    }

    private static string[] Answer(string protocol) => protocol switch
    {
        "openai-chat" =>
        [
            """{"choices":[{"index":0,"delta":{"content":"Retained MIGR-582."},"finish_reason":"stop"}]}""", "[DONE]"
        ],
        "openai-responses" =>
        [
            """{"type":"response.output_text.delta","delta":"Retained MIGR-582."}""",
            """{"type":"response.completed","response":{"status":"completed","output":[{"type":"reasoning","id":"reason-1","encrypted_content":"opaque-checkpoint","summary":[]},{"type":"message","role":"assistant","content":[{"type":"output_text","text":"Retained MIGR-582."}]}]}}"""
        ],
        "anthropic-messages" =>
        [
            """{"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":"opaque-checkpoint"}}""",
            """{"type":"content_block_start","index":1,"content_block":{"type":"text","text":""}}""",
            """{"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"Retained MIGR-582."}}""",
            """{"type":"message_delta","delta":{"stop_reason":"end_turn"}}""", """{"type":"message_stop"}"""
        ],
        "gemini" =>
        [
            """{"candidates":[{"content":{"role":"model","parts":[{"text":"Retained MIGR-582.","thoughtSignature":"opaque-checkpoint"}]},"finishReason":"STOP"}]}"""
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(protocol))
    };

    /// <summary>Gets or sets the cancellation deadline for real background work and owned endpoint cleanup.</summary>
    public TestContext TestContext { get; set; }
}