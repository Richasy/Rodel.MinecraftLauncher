using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Tests real MEAI suspension and durable response binding with only the remote model replaced.</summary>
[TestClass]
public sealed class UserPromptWorkflowTests
{
    /// <summary>Value/design review: parser or mocked callback tests cannot detect premature model continuation,
    /// counting human thinking time as model timeout, duplicated answers, or lost permission boundaries.
    /// These variants cross the real coordinator, tool callback, storage and HTTP continuation, then check
    /// response correlation and final state. They do not claim native card interaction has been exercised.</summary>
    [TestMethod]
    [DataRow("answered", false)]
    [DataRow("answered", true)]
    [DataRow("skipped", false)]
    [DataRow("canceled", false)]
    [Timeout(20000, CooperativeCancellation = true)]
    public async Task Question_WaitsForExplicitResponse_ThenContinuesOrCancels(string decision, bool multiple)
    {
        // Allow cold lazy-tool discovery under parallel runs; the later human wait still exceeds this active budget.
        const int activeBudgetSeconds = 5;
        using var sandbox = new AiSandbox();
        var calls = 0;
        JsonObject? continued = null;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            var input = await ModelEndpoint.ReadRequestAsync(context);
            var step = Interlocked.Increment(ref calls);
            if (step == 1)
            {
                Assert.Contains("tools_discover", input["tools"]!.ToJsonString());
                await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"finish_reason":"tool_calls","delta":{"tool_calls":[{"index":0,"id":"discover-question","type":"function","function":{"name":"tools_discover","arguments":"{\"group\":\"conversation\",\"names\":[\"ask_user\"]}"}}]}}]}""", "[DONE]"]);
                return;
            }

            if (step == 2)
            {
                Assert.Contains("ask_user", input["tools"]!.ToJsonString());
                // A declaration alone previously left the model asking in prose: the ordinary chat system message must also route questions.
                Assert.Contains("ask_user", input["messages"]![0]!["content"]!.ToString());
                var question = new JsonObject
                {
                    ["question"] = "选择风格？",
                    ["multiple"] = multiple,
                    ["options"] = new JsonArray(new JsonObject { ["id"] = "simple", ["label"] = "简洁", ["description"] = "清晰的轮廓" },
                        new JsonObject { ["id"] = "bright", ["label"] = "明亮" })
                };
                var frame = new JsonObject
                {
                    ["choices"] = new JsonArray(new JsonObject
                    {
                        ["index"] = 0,
                        ["finish_reason"] = "tool_calls",
                        ["delta"] = new JsonObject
                        {
                            ["tool_calls"] = new JsonArray(new JsonObject
                            {
                                ["index"] = 0,
                                ["id"] = "question-call",
                                ["type"] = "function",
                                ["function"] = new JsonObject { ["name"] = "ask_user", ["arguments"] = question.ToJsonString() }
                            })
                        }
                    })
                };
                await ModelEndpoint.SseAsync(context, [frame.ToJsonString(), "[DONE]"]);
            }
            else
            {
                continued = input;
                await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"delta":{"content":"继续处理"},"finish_reason":"stop"}]}""", "[DONE]"]);
            }
        }, TestContext.CancellationToken);
        await new AiServiceStore(sandbox.Paths).SaveServiceAsync(new()
        {
            Protocol = AiProtocols.OpenAiChat,
            BaseUrl = endpoint.Endpoint.AbsoluteUri,
            Models = [new() { Model = "fixture" }]
        }, ct: TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        var created = await session.InvokeAsync("chat.create", [], TestContext.CancellationToken);
        var receipt = await session.SendAsync(new()
        {
            ConversationId = created["id"]!.ToString(),
            ClientKey = Guid.NewGuid().ToString("N"),
            ExpectedRevision = 1,
            Prompt = "先问我的偏好",
            TimeoutSeconds = activeBudgetSeconds
        }, TestContext.CancellationToken);
        await session.EventAsync(receipt, "user_prompt_required", TestContext.CancellationToken);
        var waiting = await session.InvokeAsync("chat.turn", ChatSession.Reference(receipt), TestContext.CancellationToken);
        Assert.AreEqual("awaiting_user", waiting["status"]!.ToString());
        var response = new AiUserResponse
        {
            ConversationId = created["id"]!.ToString(),
            TurnId = receipt["id"]!.ToString(),
            PromptId = waiting["pendingUserPrompt"]!["id"]!.ToString(),
            OptionIds = decision == "answered" ? multiple ? ["bright", "simple"] : ["simple"] : [],
            Text = decision == "answered" ? "保持原有颜色" : string.Empty,
            Skip = decision == "skipped"
        };
        await Assert.ThrowsAsync<ArgumentException>(() => RespondAsync(response with { OptionIds = ["not-offered"] }));
        if (!multiple) await Assert.ThrowsAsync<ArgumentException>(() => RespondAsync(response with { OptionIds = ["simple", "bright"], Skip = false }));
        // This exceeds the configured execution timeout, but must not consume model time while the user thinks.
        await Task.Delay(TimeSpan.FromSeconds(activeBudgetSeconds) + TimeSpan.FromMilliseconds(200), TestContext.CancellationToken);
        Assert.AreEqual(2, calls, "No further model request may precede the user's explicit response.");
        if (decision == "canceled")
        {
            await session.InvokeAsync("chat.cancel", ChatSession.Reference(receipt), TestContext.CancellationToken);
            await Assert.ThrowsAsync<LauncherOperationException>(() => RespondAsync(response with { Text = "after stop" }));
        }
        else
        {
            var accepted = await RespondAsync(response);
            Assert.AreEqual(decision, accepted["status"]!.ToString());
            Assert.IsTrue(JsonNode.DeepEquals(accepted, await RespondAsync(multiple ? response with { OptionIds = ["simple", "bright"] } : response)), "A transport retry must return the same receipt.");
            await Assert.ThrowsAsync<LauncherOperationException>(() => RespondAsync(response with { Text = "changed", Skip = false }));
        }

        var finished = await session.FinishAsync(receipt, TestContext.CancellationToken);
        Assert.AreEqual(decision == "canceled" ? "canceled" : "completed", finished["status"]!.ToString(), finished.ToJsonString());
        Assert.IsNull(finished["pendingUserPrompt"]);
        Assert.AreEqual("read_only", sandbox.Store.GetDocument(created["id"]!.ToString())["mode"]!.ToString());
        if (decision == "canceled")
        {
            await Assert.ThrowsAsync<LauncherOperationException>(() => RespondAsync(response with { Text = "too late" }));
            Assert.AreEqual(2, calls);
        }
        else
        {
            Assert.AreEqual(3, calls);
            var returned = continued!["messages"]!.AsArray().Single(message => message?["tool_call_id"]?.ToString() == "question-call");
            Assert.AreEqual("question-call", returned!["tool_call_id"]!.ToString());
            Assert.Contains(decision, returned.ToJsonString());
            if (decision == "answered") Assert.AreEqual("保持原有颜色", JsonNode.Parse(returned["content"]!.ToString())!["data"]!["text"]!.ToString());
        }

        Task<JsonObject> RespondAsync(AiUserResponse answer) => session.InvokeAsync("chat.respond",
            JsonSerializer.SerializeToNode(answer, AiUserPromptJsonContext.Default.AiUserResponse)!.AsObject(), TestContext.CancellationToken);
    }

    /// <summary>Recovery cannot resume an abandoned question or accept a stale response. Unlike normal cancel,
    /// this starts from a durable waiting record with no living owner and exercises the real recovery scan.</summary>
    [TestMethod]
    public async Task AbandonedQuestion_RecoversAsInterrupted_WithoutRetainingAnAnswerablePrompt()
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        var pid = Guid.NewGuid().ToString("N");
        var prompt = new AiUserPrompt(pid, new("What style?", []));
        var pending = JsonSerializer.SerializeToNode(prompt, AiUserPromptJsonContext.Default.AiUserPrompt)!;
        turn["status"] = "awaiting_user";
        turn["pendingUserPrompt"] = pending.DeepClone();
        await sandbox.Store.SaveTurnAsync(turn, TestContext.CancellationToken);
        var file = Path.Combine(sandbox.Root, "ai", "conversations", conversation["id"]!.ToString(), "questions", turn["id"]!.ToString(), pid + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, new JsonObject { ["prompt"] = pending, ["state"] = "pending" }.ToJsonString(), TestContext.CancellationToken);
        await sandbox.Store.RecoverInterruptedAsync(TestContext.CancellationToken);
        var recovered = sandbox.Store.GetTurnDocument(conversation["id"]!.ToString(), turn["id"]!.ToString());
        Assert.AreEqual("interrupted", recovered["status"]!.ToString());
        Assert.IsNull(recovered["pendingUserPrompt"]);
        var error = await Assert.ThrowsAsync<LauncherOperationException>(() => new AiUserPrompts(sandbox.Paths).RespondAsync(new()
        {
            ConversationId = conversation["id"]!.ToString(),
            TurnId = turn["id"]!.ToString(),
            PromptId = pid,
            Text = "too late"
        }, TestContext.CancellationToken));
        Assert.AreEqual("AI_USER_PROMPT_INACTIVE", error.Code);
        Assert.IsNull(JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.CancellationToken))!["response"]);
    }

    /// <summary>Gets or sets cancellation for the isolated model server and actual Core turn.</summary>
    public TestContext TestContext { get; set; }
}