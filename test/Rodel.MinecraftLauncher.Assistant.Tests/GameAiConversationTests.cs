// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.GameBridge;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Tests;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Exercises real game-monitor ingress and temporary per-game conversations; only Minecraft and model answers are substituted.</summary>
[TestClass]
public sealed class GameAiConversationTests
{
    /// <summary>Review: existing chat and bridge cases do not protect save-slot ownership. This workflow checks provider history, reopening a separate facade, independent save context, exact reset and unchanged game bytes; a shared/global slot or merely hiding old messages fails those observations.</summary>
    [TestMethod]
    [Timeout(45000, CooperativeCancellation = true)]
    public async Task WithinGame_WorldSlotsShareFollowups_ResetOnlySelectedHistory_AndPreserveGameFiles()
    {
        using var sandbox = new AiSandbox();
        var instance = await new InstanceStore(sandbox.Paths).CreateAsync("Synthetic game", "26.3", TestContext.CancellationToken);
        var save = Path.Combine(instance.GameDirectory, "saves", "A");
        Directory.CreateDirectory(save);
        var sentinel = Path.Combine(save, "level.dat");
        byte[] original = [3, 1, 4, 1, 5];
        await File.WriteAllBytesAsync(sentinel, original, TestContext.CancellationToken);
        var wires = new ConcurrentQueue<JsonObject>();
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            wires.Enqueue(await ModelEndpoint.ReadRequestAsync(context));
            await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"delta":{"content":"Synthetic answer"},"finish_reason":"stop"}]}""", "[DONE]"]);
        }, TestContext.CancellationToken);
        await ConfigureAsync(sandbox, endpoint);
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken, sandbox.Root, instance.Id);
        await using var peer = await fixture.ConnectAsync(request => fixture.Response(request), TestContext.CancellationToken);
        var snapshot = await fixture.Service.SnapshotAsync(fixture.Session, TestContext.CancellationToken);
        var firstRequest = Request(snapshot, "send", "A", "First question");
        var first = await FinishAsync(fixture, firstRequest);
        Assert.AreEqual("Synthetic answer", first.Turns.Single().Answer);
        var replay = await fixture.Service.RequestAiAsync(fixture.Session, firstRequest, TestContext.CancellationToken);
        Assert.AreEqual(first.ConversationId, replay.ConversationId);
        Assert.HasCount(1, replay.Turns);
        Assert.HasCount(1, wires, "Observing the same committed submission must not run the model again.");

        var restored = await new GameBridgeService(sandbox.Paths).RequestAiAsync(fixture.Session, Request(snapshot, "open", "A"), TestContext.CancellationToken);
        Assert.AreEqual(first.ConversationId, restored.ConversationId);
        var second = await FinishAsync(fixture, Request(snapshot, "send", "A", "Follow up"));
        Assert.HasCount(2, second.Turns);
        var secondWire = wires.ToArray()[1].ToJsonString();
        Assert.Contains("First question", secondWire);
        Assert.Contains("Synthetic answer", secondWire);

        var other = await FinishAsync(fixture, Request(snapshot, "send", "B", "Independent save"));
        Assert.AreNotEqual(first.ConversationId, other.ConversationId);
        Assert.DoesNotContain("First question", wires.ToArray()[2].ToJsonString());
        var resetRequest = Request(snapshot, "new", "A");
        var reset = await fixture.Service.RequestAiAsync(fixture.Session, resetRequest, TestContext.CancellationToken);
        Assert.IsNull(reset.ConversationId);
        Assert.IsEmpty(reset.Turns);
        var removed = await Assert.ThrowsExactlyAsync<LauncherOperationException>(() => new AssistantService(sandbox.Paths).GetConversationAsync(first.ConversationId!, ct: TestContext.CancellationToken));
        Assert.AreEqual("AI_CONVERSATION_NOT_FOUND", removed.Code);
        var after = await FinishAsync(fixture, Request(snapshot, "send", "A", "Fresh conversation"));
        Assert.AreNotEqual(first.ConversationId, after.ConversationId);
        Assert.DoesNotContain("First question", wires.ToArray()[3].ToJsonString());
        var replayReset = await fixture.Service.RequestAiAsync(fixture.Session, resetRequest, TestContext.CancellationToken);
        Assert.AreEqual(after.ConversationId, replayReset.ConversationId, "A lost reset response must not erase a later conversation when the same reset is observed again.");
        var retainedOther = await fixture.Service.RequestAiAsync(fixture.Session, Request(snapshot, "open", "B"), TestContext.CancellationToken);
        Assert.AreEqual(other.ConversationId, retainedOther.ConversationId);
        Assert.HasCount(1, retainedOther.Turns);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(sentinel, TestContext.CancellationToken));
    }

    /// <summary>
    /// Review: world reset coverage cannot detect game-close leaks, deletion of launcher history, or interference with
    /// another live game. Dispose the actual bridge owner while ask_user is waiting, then reopen the same world in a
    /// new game and inspect model context. Only the native peer and model are substituted; all history and cleanup are real.
    /// </summary>
    [TestMethod]
    [Timeout(45000, CooperativeCancellation = true)]
    public async Task GameExit_RemovesItsWaitingHistory_PreservesOtherOwners_AndNextLaunchStartsEmpty()
    {
        using var sandbox = new AiSandbox();
        var instance = await new InstanceStore(sandbox.Paths).CreateAsync("Synthetic game", "26.3", TestContext.CancellationToken);
        var waitingMode = false;
        JsonObject? latest = null;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            latest = await ModelEndpoint.ReadRequestAsync(context);
            if (!waitingMode)
                await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"delta":{"content":"Remembered answer"},"finish_reason":"stop"}]}""", "[DONE]"]);
            else if (!latest["tools"]!.AsArray().Any(tool => tool?["function"]?["name"]?.ToString() == "ask_user"))
                await ModelEndpoint.SseAsync(context, [QuestionToolChunk("tools_discover", new() { ["group"] = "conversation", ["names"] = new JsonArray("ask_user") }), "[DONE]"]);
            else
                await ModelEndpoint.SseAsync(context, [QuestionToolChunk("ask_user", new() { ["question"] = "A question that must close with the game", ["options"] = new JsonArray() }), "[DONE]"]);
        }, TestContext.CancellationToken);
        await ConfigureAsync(sandbox, endpoint);
        var launcher = await new AssistantService(sandbox.Paths).CreateConversationAsync(new() { InstanceId = instance.Id, Title = "A" }, ct: TestContext.CancellationToken);
        var launcherDocument = sandbox.Store.GetDocument(launcher.Id!);
        var launcherTurn = await sandbox.TurnAsync(launcherDocument, false, TestContext.CancellationToken);
        launcherTurn["status"] = "completed";
        launcherTurn["prompt"] = "Persistent launcher prompt";
        launcherTurn["answer"] = "Persistent launcher answer";
        await sandbox.Store.SaveTurnAsync(launcherTurn, TestContext.CancellationToken);
        await using var other = await BridgeFixture.CreateAsync(TestContext.CancellationToken, sandbox.Root, instance.Id);
        await using var otherPeer = await other.ConnectAsync(request => other.Response(request), TestContext.CancellationToken);
        var otherSnapshot = await other.Service.SnapshotAsync(other.Session, TestContext.CancellationToken);
        var otherHistory = await FinishAsync(other, Request(otherSnapshot, "send", "A", "Other game only"));
        string closedId;
        string closedSession;
        await using (var game = await BridgeFixture.CreateAsync(TestContext.CancellationToken, sandbox.Root, instance.Id))
        {
            await using var peer = await game.ConnectAsync(request => game.Response(request), TestContext.CancellationToken);
            var snapshot = await game.Service.SnapshotAsync(game.Session, TestContext.CancellationToken);
            waitingMode = true;
            var waiting = await WaitForQuestionAsync(game, Request(snapshot, "send", "A", "Temporary question before exit"));
            closedId = waiting.ConversationId!;
            closedSession = game.Session;
            Assert.AreNotEqual(otherHistory.ConversationId, closedId);
            var catalog = await new AssistantService(sandbox.Paths).ListConversationsAsync(limit: 1, ct: TestContext.CancellationToken);
            Assert.AreEqual(1, catalog.Total, "Temporary game history must not enter launcher pagination or unread history.");
            Assert.AreEqual(launcher.Id, catalog.Items.Single().Id);
        }

        Assert.IsFalse(Directory.Exists(Path.Combine(sandbox.Root, "ai", "conversations", closedId)));
        Assert.IsFalse(Directory.Exists(AiGameHistory.SlotsFor(sandbox.Paths, closedSession)));
        Assert.IsTrue(JsonNode.DeepEquals(launcherDocument, sandbox.Store.GetDocument(launcher.Id!)));
        Assert.AreEqual("Persistent launcher answer", sandbox.Store.GetTurn(launcher.Id!, launcherTurn["id"]!.ToString()).Answer);
        Assert.AreEqual("Remembered answer", sandbox.Store.GetTurn(otherHistory.ConversationId!, otherHistory.Turns.Single().Id).Answer);
        await using var next = await BridgeFixture.CreateAsync(TestContext.CancellationToken, sandbox.Root, instance.Id);
        await using var nextPeer = await next.ConnectAsync(request => next.Response(request), TestContext.CancellationToken);
        var nextSnapshot = await next.Service.SnapshotAsync(next.Session, TestContext.CancellationToken);
        var opened = await next.Service.RequestAiAsync(next.Session, Request(nextSnapshot, "open", "A"), TestContext.CancellationToken);
        Assert.IsNull(opened.ConversationId);
        Assert.IsEmpty(opened.Turns);
        waitingMode = false;
        await FinishAsync(next, Request(nextSnapshot, "send", "A", "A fresh game"));
        Assert.DoesNotContain("Temporary question before exit", latest!.ToJsonString());
        Assert.DoesNotContain("Other game only", latest.ToJsonString());
    }

    /// <summary>
    /// Review: a graceful owner shutdown cannot cover an abandoned monitor. Listing must hide live game history and
    /// prune a previous process incarnation, while retaining an ordinary same-title/instance conversation. A mismatched
    /// recorded start time represents PID reuse; no real process is killed and no model or player files are needed.
    /// </summary>
    [TestMethod]
    public async Task LauncherHistory_PrunesEndedGameIncarnations_AndRetainsLiveGamesAndPersistentChats()
    {
        using var sandbox = new AiSandbox();
        using var process = Process.GetCurrentProcess();
        var launcher = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var live = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var stale = await sandbox.ConversationAsync(TestContext.CancellationToken);
        foreach (var document in new[] { live, stale })
        {
            document["gameSessionId"] = Guid.NewGuid().ToString("N");
            document["gameProcessId"] = process.Id;
            document["gameProcessStartUtc"] = (ReferenceEquals(document, live) ? process.StartTime.ToUniversalTime() : process.StartTime.ToUniversalTime().AddDays(-1)).ToString("O");
            await sandbox.Store.SaveConversationAsync(document, TestContext.CancellationToken);
        }

        var slots = AiGameHistory.SlotsFor(sandbox.Paths, stale["gameSessionId"]!.ToString());
        Directory.CreateDirectory(slots);
        await File.WriteAllTextAsync(Path.Combine(slots, "receipt.json"), "{}", TestContext.CancellationToken);
        var catalog = await new AssistantService(sandbox.Paths).ListConversationsAsync(limit: 1, ct: TestContext.CancellationToken);
        Assert.AreEqual(1, catalog.Total);
        Assert.AreEqual(launcher["id"]!.ToString(), catalog.Items.Single().Id);
        Assert.IsFalse(catalog.HasMore);
        Assert.IsTrue(JsonNode.DeepEquals(launcher, sandbox.Store.GetDocument(launcher["id"]!.ToString())));
        Assert.AreEqual(live["gameSessionId"]!.ToString(), sandbox.Store.Get(live["id"]!.ToString()).GameSessionId);
        Assert.IsFalse(Directory.Exists(Path.Combine(sandbox.Root, "ai", "conversations", stale["id"]!.ToString())));
        Assert.IsFalse(Directory.Exists(slots));
    }

    /// <summary>Review: console follow-ups must reach the provider with previous answers, while observers of overlapping submissions cannot read each other's response. A durable text event holds the first real Core turn in flight; panel sharing and source-specific final-only output are checked at that boundary.</summary>
    [TestMethod]
    [Timeout(45000, CooperativeCancellation = true)]
    public async Task ConsoleQuestions_ShareContext_AndObserveTheirExactSubmission()
    {
        using var sandbox = new AiSandbox();
        var instance = await new InstanceStore(sandbox.Paths).CreateAsync("Synthetic game", "26.3", TestContext.CancellationToken);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var partial = new string('x', 600);
        var wires = new ConcurrentQueue<JsonObject>();
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            var wire = await ModelEndpoint.ReadRequestAsync(context);
            wires.Enqueue(wire);
            var chunk = new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = new JsonObject { ["content"] = partial }, ["finish_reason"] = null }) };
            await ModelEndpoint.SseAsync(context, [chunk.ToJsonString()]);
            await release.Task.WaitAsync(context.RequestAborted);
            await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"delta":{"content":" complete"},"finish_reason":"stop"}]}""", "[DONE]"]);
        }, TestContext.CancellationToken);
        await ConfigureAsync(sandbox, endpoint);
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken, sandbox.Root, instance.Id);
        await using var peer = await fixture.ConnectAsync(request => fixture.Response(request), TestContext.CancellationToken);
        var snapshot = await fixture.Service.SnapshotAsync(fixture.Session, TestContext.CancellationToken);
        var quickRequest = Request(snapshot, "quick", "A", "Quick question one");
        var queued = await fixture.Service.RequestAiAsync(fixture.Session, quickRequest, TestContext.CancellationToken);
        var secondRequest = Request(snapshot, "quick", "A", "Follow up on question one");
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
            while (!(await sandbox.Store.EventsAsync(queued.ConversationId!, queued.Turns.Single().Id, ct: TestContext.CancellationToken)).Items.Any(item => item.Kind == "text_delta"))
                await timer.WaitForNextTickAsync(TestContext.CancellationToken);
            var pending = await fixture.Service.RequestAiAsync(fixture.Session, quickRequest with { Action = "read", QuickId = quickRequest.RequestId }, TestContext.CancellationToken);
            Assert.IsNull(pending.Events);
            Assert.IsNull(pending.Turns.Single().Answer);
            Assert.IsFalse(AiChatStates.IsTerminal(pending.Turns.Single().Status));
            var secondQueued = await fixture.Service.RequestAiAsync(fixture.Session, secondRequest, TestContext.CancellationToken);
            Assert.AreEqual(queued.ConversationId, secondQueued.ConversationId);
            Assert.AreEqual(secondRequest.RequestId, secondQueued.Turns.Single().SubmissionId);
            pending = await fixture.Service.RequestAiAsync(fixture.Session, quickRequest with { Action = "read", QuickId = quickRequest.RequestId }, TestContext.CancellationToken);
            Assert.AreEqual(quickRequest.RequestId, pending.Turns.Single().SubmissionId);
            var panel = await fixture.Service.RequestAiAsync(fixture.Session, Request(snapshot, "open", "A"), TestContext.CancellationToken);
            Assert.AreEqual(queued.ConversationId, panel.ConversationId);
            Assert.HasCount(2, panel.Turns);
        }
        finally { release.TrySetResult(); }

        var first = await FinishAsync(fixture, quickRequest);
        Assert.AreEqual(partial + " complete", first.Turns.Single().Answer);
        var second = await FinishAsync(fixture, secondRequest);
        Assert.AreEqual(first.ConversationId, second.ConversationId);
        Assert.Contains("Quick question one", wires.ToArray()[1].ToJsonString());
        Assert.Contains(partial + " complete", wires.ToArray()[1].ToJsonString());
        var restored = await new GameBridgeService(sandbox.Paths).RequestAiAsync(fixture.Session, Request(snapshot, "open", "A"), TestContext.CancellationToken);
        Assert.AreEqual(first.ConversationId, restored.ConversationId);
        Assert.HasCount(2, restored.Turns);
    }

    /// <summary>Review: checking a stored flag would not prove approval-free execution. Real game ingress, MEAI tool dispatch and a controlled native peer must complete without a human decision under the default safe configuration. The remote variant must refuse before dispatch; neither variant proves Minecraft gameplay.</summary>
    [TestMethod]
    [DataRow("local_owned", 1, "applied")]
    [DataRow("remote_read_only", 0, "rejected")]
    [Timeout(45000, CooperativeCancellation = true)]
    public async Task GameTurn_UsesOwnOperationGrant_WithoutChangingLauncherDefaults(string mode, int expectedWrites, string expectedState)
    {
        using var sandbox = new AiSandbox();
        var instance = await new InstanceStore(sandbox.Paths).CreateAsync("Synthetic game", "26.3", TestContext.CancellationToken);
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken, sandbox.Root, instance.Id);
        var edit = fixture.Edit();
        var writes = 0;
        await using var peer = await fixture.ConnectAsync(request =>
        {
            if (request.Method != "game.edit") return fixture.Response(request, mode);
            Interlocked.Increment(ref writes);
            return new BridgeRpc { Id = request.Id, Result = JsonSerializer.SerializeToElement(new GameOperation { OperationId = edit.OperationId, State = "applied" }, GameBridgeJsonContext.Default.GameOperation) };
        }, TestContext.CancellationToken);
        var calls = 0;
        JsonNode? result = null;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            var wire = await ModelEndpoint.ReadRequestAsync(context);
            if (Interlocked.Increment(ref calls) == 1)
            {
                var arguments = new JsonObject { ["sessionId"] = fixture.Session, ["request"] = JsonSerializer.SerializeToNode(edit, GameBridgeJsonContext.Default.GameEditRequest) };
                var delta = new JsonObject
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
                                ["id"] = "native-edit",
                                ["type"] = "function",
                                ["function"] = new JsonObject { ["name"] = "game_edit", ["arguments"] = arguments.ToJsonString() }
                            })
                        }
                    })
                };
                await ModelEndpoint.SseAsync(context, [delta.ToJsonString(), "[DONE]"]);
            }
            else
            {
                result = JsonNode.Parse(wire["messages"]!.AsArray().Last(item => item?["role"]?.ToString() == "tool")!["content"]!.ToString());
                await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"delta":{"content":"Observed the native receipt."},"finish_reason":"stop"}]}""", "[DONE]"]);
            }
        }, TestContext.CancellationToken);
        await ConfigureAsync(sandbox, endpoint);
        var snapshot = await fixture.Service.SnapshotAsync(fixture.Session, TestContext.CancellationToken);
        var completed = await FinishAsync(fixture, Request(snapshot, "quick", "A", "Set my health"));
        Assert.AreEqual(2, calls);
        Assert.AreEqual(expectedWrites, writes);
        Assert.AreEqual(expectedState, result?["data"]?["state"]?.ToString(), result?.ToJsonString());
        var turn = new AiChatStore(sandbox.Paths).GetTurnDocument(completed.ConversationId!, completed.Turns.Single().Id);
        Assert.AreEqual("explicit_turn", turn["turnAuthorization"]?.ToString());
        var events = await sandbox.Store.EventsAsync(completed.ConversationId!, completed.Turns.Single().Id, ct: TestContext.CancellationToken);
        Assert.DoesNotContain(item => item.Kind == "confirmation_required", events.Items);
        Assert.AreEqual("safe", (await new AssistantService(sandbox.Paths).GetConversationAsync(completed.ConversationId!, ct: TestContext.CancellationToken)).Mode);
        Assert.AreEqual("safe", (await new AiServiceStore(sandbox.Paths).GetAsync(TestContext.CancellationToken)).DefaultOperationMode);
    }

    /// <summary>Review: a valid authenticated game session must still reject an old world before creating a chat or contacting a model. Existing edit guards do not cover game-originated model submissions.</summary>
    [TestMethod]
    public async Task ChangedWorld_RejectsBeforeConversationCreationOrModelSelection()
    {
        using var sandbox = new AiSandbox();
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken, sandbox.Root);
        await using var peer = await fixture.ConnectAsync(request => fixture.Response(request), TestContext.CancellationToken);
        var snapshot = await fixture.Service.SnapshotAsync(fixture.Session, TestContext.CancellationToken);
        var request = Request(snapshot with { WorldEpoch = "2" }, "send", "A", "Stale message");
        var rejected = await Assert.ThrowsExactlyAsync<LauncherOperationException>(() => fixture.Service.RequestAiAsync(fixture.Session, request, TestContext.CancellationToken));
        Assert.AreEqual("AI_GAME_WORLD_CHANGED", rejected.Code);
        Assert.IsEmpty(new AiChatStore(sandbox.Paths).List().Items);
    }

    /// <summary>Review: the no-Markdown instruction is a scene-specific behavior, not a global provider setting. Inspecting actual HTTP requests for a game turn and an ordinary turn catches both missing injection and accidental leakage into the launcher's existing chat.</summary>
    [TestMethod]
    [Timeout(45000, CooperativeCancellation = true)]
    public async Task PlainTextInstruction_IsLimitedToGameOriginatedTurns()
    {
        using var sandbox = new AiSandbox();
        var instance = await new InstanceStore(sandbox.Paths).CreateAsync("Synthetic game", "26.3", TestContext.CancellationToken);
        var wires = new ConcurrentQueue<JsonObject>();
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            wires.Enqueue(await ModelEndpoint.ReadRequestAsync(context));
            await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"delta":{"content":"Answer"},"finish_reason":"stop"}]}""", "[DONE]"]);
        }, TestContext.CancellationToken);
        await ConfigureAsync(sandbox, endpoint);
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken, sandbox.Root, instance.Id);
        await using var peer = await fixture.ConnectAsync(request => fixture.Response(request), TestContext.CancellationToken);
        var snapshot = await fixture.Service.SnapshotAsync(fixture.Session, TestContext.CancellationToken);
        await FinishAsync(fixture, Request(snapshot, "send", "A", "Game message"));
        var assistant = new AssistantService(sandbox.Paths);
        var ordinary = await assistant.CreateConversationAsync(new(), ct: TestContext.CancellationToken);
        await assistant.SendAsync(new() { ConversationId = ordinary.Id!, ClientKey = Guid.NewGuid().ToString("N"), Prompt = "Launcher message", ExpectedRevision = ordinary.Revision!.Value }, ct: TestContext.CancellationToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        while (!(await assistant.GetConversationAsync(ordinary.Id!, ct: TestContext.CancellationToken)).Turns!.All(turn => AiChatStates.IsTerminal(turn.Status))
            || (await assistant.GetHostAsync(ct: TestContext.CancellationToken)).ActiveTextRequest)
        {
            await timer.WaitForNextTickAsync(TestContext.CancellationToken);
        }

        Assert.HasCount(2, wires);
        const string gameFormat = "游戏内回复格式：使用纯文本，不使用 Markdown 语法";
        var systemPrompts = wires.Select(wire => string.Join("\n", wire["messages"]!.AsArray().Where(message => message?["role"]?.ToString() == "system").Select(message => message!["content"]!.ToString()))).ToArray();
        Assert.Contains(gameFormat, systemPrompts[0]);
        Assert.DoesNotContain(gameFormat, systemPrompts[1]);
    }

    /// <summary>
    /// Review: existing prompt tests submit explicit answers, so they miss a stale game view queuing ordinary text behind
    /// a question. Hold the actual model continuation after the durable answer, and check one turn, the provider's answer,
    /// original-message retries and reply retries. The quick variant also protects the console observer's correlation.
    /// Only the remote model and native Minecraft peer are substituted; no player files or live accounts are used.
    /// </summary>
    [TestMethod]
    [DataRow("send")]
    [DataRow("quick")]
    [Timeout(45000, CooperativeCancellation = true)]
    public async Task WaitingQuestion_ConsumesNextMessage_AndRetainsReplyIdentity(string entry)
    {
        using var sandbox = new AiSandbox();
        var instance = await new InstanceStore(sandbox.Paths).CreateAsync("Synthetic game", "26.3", TestContext.CancellationToken);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continued = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            var wire = await ModelEndpoint.ReadRequestAsync(context);
            var step = Interlocked.Increment(ref calls);
            if (step == 1)
            {
                await ModelEndpoint.SseAsync(context, [QuestionToolChunk("tools_discover", new() { ["group"] = "conversation", ["names"] = new JsonArray("ask_user") }), "[DONE]"]);
            }
            else if (step == 2)
            {
                await ModelEndpoint.SseAsync(context, [QuestionToolChunk("ask_user", new() { ["question"] = "按什么模式继续？", ["options"] = new JsonArray(new JsonObject { ["id"] = "survival", ["label"] = "生存" }) }), "[DONE]"]);
            }
            else
            {
                continued.TrySetResult(wire);
                await release.Task.WaitAsync(context.RequestAborted);
                await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"delta":{"content":"按生存模式继续"},"finish_reason":"stop"}]}""", "[DONE]"]);
            }
        }, TestContext.CancellationToken);
        await ConfigureAsync(sandbox, endpoint);
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken, sandbox.Root, instance.Id);
        await using var peer = await fixture.ConnectAsync(request => fixture.Response(request), TestContext.CancellationToken);
        var snapshot = await fixture.Service.SnapshotAsync(fixture.Session, TestContext.CancellationToken);
        var original = Request(snapshot, "send", "A", "帮我规划");
        var waiting = await WaitForQuestionAsync(fixture, original);
        var turn = waiting.Turns.Single();
        var prompt = turn.Question!;
        var originalReplay = await fixture.Service.RequestAiAsync(fixture.Session, original, TestContext.CancellationToken);
        Assert.IsNotNull(originalReplay.Turns.Single().Question, "Retrying the original message must not answer its own new question.");
        Assert.AreEqual(2, calls);
        var reply = Request(snapshot, entry, "A", "生存模式");
        try
        {
            var accepted = await fixture.Service.RequestAiAsync(fixture.Session, reply, TestContext.CancellationToken);
            Assert.AreEqual(turn.Id, accepted.Turns.Single().Id);
            Assert.IsNull(accepted.Turns.Single().Question, "An accepted answer must immediately stop exposing pending choices.");
            var assistant = new AssistantService(sandbox.Paths);
            Assert.HasCount(1, (await assistant.GetConversationAsync(waiting.ConversationId!, ct: TestContext.CancellationToken)).Turns!);
            var questionFile = Path.Combine(sandbox.Paths.Root, "ai", "conversations", waiting.ConversationId!, "questions", turn.Id, prompt.Id + ".json");
            var durable = JsonNode.Parse(await File.ReadAllTextAsync(questionFile, TestContext.CancellationToken))!;
            Assert.AreEqual("answered", durable["state"]!.ToString());
            var wire = await continued.Task.WaitAsync(TestContext.CancellationToken);
            var answer = JsonNode.Parse(wire["messages"]!.AsArray().Last(message => message?["role"]?.ToString() == "tool")!["content"]!.ToString())!;
            Assert.AreEqual("answered", answer["data"]!["status"]!.ToString());
            Assert.AreEqual("生存模式", answer["data"]!["text"]!.ToString());
            var replay = await fixture.Service.RequestAiAsync(fixture.Session, reply, TestContext.CancellationToken);
            Assert.AreEqual(turn.Id, replay.Turns.Single().Id);
            var conflict = await Assert.ThrowsAsync<LauncherOperationException>(() => fixture.Service.RequestAiAsync(fixture.Session, reply with { Message = "另一份回答" }, TestContext.CancellationToken));
            Assert.AreEqual("AI_SUBMISSION_CONFLICT", conflict.Code);
        }
        finally { release.TrySetResult(); }

        var finished = await FinishAsync(fixture, reply);
        Assert.AreEqual(turn.Id, finished.Turns.Single().Id);
        Assert.AreEqual("按生存模式继续", finished.Turns.Single().Answer);
        var replayAfterCompletion = await fixture.Service.RequestAiAsync(fixture.Session, reply, TestContext.CancellationToken);
        Assert.AreEqual(turn.Id, replayAfterCompletion.Turns.Single().Id);
        Assert.AreEqual(3, calls, "Reply retries must not dispatch another model turn after completion.");
    }

    /// <summary>
    /// Review: merely checking the console flag or initial lazy catalog cannot prove ask_user is unavailable. The real
    /// game ingress requests discovery, then the substituted model deliberately calls the unoffered tool. Completion
    /// with a rejection receipt and no prompt event detects both accidental exposure and an execution path that waits.
    /// </summary>
    [TestMethod]
    [Timeout(45000, CooperativeCancellation = true)]
    public async Task ConsoleQuestion_ExcludesInteractiveTool_AndRejectsForcedInvocation()
    {
        using var sandbox = new AiSandbox();
        var instance = await new InstanceStore(sandbox.Paths).CreateAsync("Synthetic game", "26.3", TestContext.CancellationToken);
        var calls = 0;
        string[]? offered = null;
        JsonNode? rejection = null;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            var wire = await ModelEndpoint.ReadRequestAsync(context);
            var step = Interlocked.Increment(ref calls);
            if (step == 1)
            {
                await ModelEndpoint.SseAsync(context, [QuestionToolChunk("tools_discover", new() { ["group"] = "conversation", ["names"] = new JsonArray("ask_user") }), "[DONE]"]);
            }
            else if (step == 2)
            {
                offered = [.. wire["tools"]!.AsArray().Select(tool => tool!["function"]!["name"]!.ToString())];
                await ModelEndpoint.SseAsync(context, [QuestionToolChunk("ask_user", new() { ["question"] = "不能挂起控制台", ["options"] = new JsonArray() }), "[DONE]"]);
            }
            else
            {
                rejection = JsonNode.Parse(wire["messages"]!.AsArray().Last(message => message?["role"]?.ToString() == "tool")!["content"]!.ToString());
                await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"delta":{"content":"请直接说明你希望采用的模式。"},"finish_reason":"stop"}]}""", "[DONE]"]);
            }
        }, TestContext.CancellationToken);
        await ConfigureAsync(sandbox, endpoint);
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken, sandbox.Root, instance.Id);
        await using var peer = await fixture.ConnectAsync(request => fixture.Response(request), TestContext.CancellationToken);
        var snapshot = await fixture.Service.SnapshotAsync(fixture.Session, TestContext.CancellationToken);
        var finished = await FinishAsync(fixture, Request(snapshot, "quick", "A", "询问我的偏好"));
        Assert.IsNotNull(offered);
        Assert.DoesNotContain("ask_user", offered);
        Assert.AreEqual("AI_USER_PROMPT_UNAVAILABLE", rejection?["error"]?["code"]?.ToString(), rejection?.ToJsonString());
        Assert.AreEqual(3, calls);
        Assert.IsNull(finished.Turns.Single().Question);
        var events = await sandbox.Store.EventsAsync(finished.ConversationId!, finished.Turns.Single().Id, ct: TestContext.CancellationToken);
        Assert.DoesNotContain(item => item.Kind == "user_prompt_required", events.Items);
    }

    private async Task<GameAiState> WaitForQuestionAsync(BridgeFixture fixture, GameAiRequest request)
    {
        var state = await fixture.Service.RequestAiAsync(fixture.Session, request, TestContext.CancellationToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        while (!state.Turns.Any(turn => turn.Question is not null))
        {
            Assert.DoesNotContain(turn => AiChatStates.IsTerminal(turn.Status), state.Turns, "The model must reach an actual pending question.");
            await timer.WaitForNextTickAsync(TestContext.CancellationToken);
            state = await fixture.Service.RequestAiAsync(fixture.Session, request with { Action = "read" }, TestContext.CancellationToken);
        }

        return state;
    }

    private static string QuestionToolChunk(string name, JsonObject arguments) => new JsonObject
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
                    ["id"] = "call-" + name,
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = name, ["arguments"] = arguments.ToJsonString() }
                })
            }
        })
    }.ToJsonString();

    private Task<AiServiceConfiguration> ConfigureAsync(AiSandbox sandbox, ModelEndpoint endpoint) => new AiServiceStore(sandbox.Paths).SaveServiceAsync(new()
    {
        Protocol = AiProtocols.OpenAiChat,
        BaseUrl = endpoint.Endpoint.AbsoluteUri,
        DisplayName = "Synthetic provider",
        Models = [new() { Model = "fixture" }]
    }, ct: TestContext.CancellationToken);

    private static GameAiRequest Request(GameSnapshot snapshot, string action, string world, string? message = null) => new()
    {
        Snapshot = snapshot,
        Action = action,
        WorldKey = world,
        WorldName = world,
        Message = message,
        RequestId = Guid.NewGuid().ToString("N")
    };

    private async Task<GameAiState> FinishAsync(BridgeFixture fixture, GameAiRequest request)
    {
        var state = await fixture.Service.RequestAiAsync(fixture.Session, request, TestContext.CancellationToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        var observe = request with { Action = "read", QuickId = request.Action == "quick" ? request.RequestId : null };
        while (state.Turns.Length == 0 || state.Turns.Any(turn => !AiChatStates.IsTerminal(turn.Status))
            || (await new AssistantService(fixture.Paths).GetHostAsync(ct: TestContext.CancellationToken)).ActiveTextRequest)
        {
            await timer.WaitForNextTickAsync(TestContext.CancellationToken);
            state = await fixture.Service.RequestAiAsync(fixture.Session, observe, TestContext.CancellationToken);
        }

        Assert.IsTrue(state.Turns.All(turn => turn.Status == "completed"), JsonSerializer.Serialize(state, GameAiJsonContext.Default.GameAiState));
        return state;
    }

    /// <summary>Gets cancellation for the owned game peer, synthetic HTTP server and Core turns.</summary>
    public TestContext TestContext { get; set; } = null!;
}