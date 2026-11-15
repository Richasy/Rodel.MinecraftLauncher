using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects the chat presentation boundary; provider parsing and durable event storage are tested separately.</summary>
[TestClass]
public sealed class TranscriptContentTests
{
    /// <summary>
    /// The previous raw-event dump exposed lifecycle metadata while hiding actual reasoning. A mixed real-contract sequence
    /// must retain only player content in order, join deltas and pair tool arguments/results without copying event envelopes.
    /// Linking the production projection checks this boundary without a model account or native window fixture.
    /// </summary>
    [TestMethod]
    public void MixedEvents_KeepContentInOrder_AndExcludeLifecyclePayloads()
    {
        var events = new[]
        {
            Event(1, "turn_started", string.Empty, """{"modelId":"PRIVATE-LIFECYCLE"}"""),
            Event(2, "skill_started", "core", """{"skillId":"PRIVATE-SKILL"}"""),
            Event(3, "reasoning_started", "model:reasoning", "{}"),
            Event(4, "reasoning_delta", "model:reasoning", """{"text":"先检查"}"""),
            Event(5, "reasoning_delta", "model:reasoning", """{"text":"实际日志。"}"""),
            Event(6, "text_delta", "model:text", """{"text":"正在读取。"}"""),
            Event(7, "tool_started", "invocation", """{"name":"read_file_lines","arguments":{"path":"launch.log"},"source":"PRIVATE-SOURCE","toolCallId":"call"}"""),
            Event(8, "tool_finished", "invocation", """{"name":"read_file_lines","status":"succeeded","result":{"lines":["Observed log"]},"parentStepId":"PRIVATE-PARENT"}"""),
            Event(9, "text_delta", "next:text", """{"text":"已读取"}"""),
            Event(10, "text_delta", "next:text", """{"text":"真实结果。"}"""),
            Event(11, "usage", "next", """{"opaque":"PRIVATE-USAGE"}"""),
            Event(12, "future_event", "next", """{"text":"PRIVATE-UNKNOWN"}"""),
        };
        var parts = AiTranscriptContent.Read(events, "已读取真实结果。", true);
        CollectionAssert.AreEqual(new[] { AiTranscriptPartKind.Thinking, AiTranscriptPartKind.Response, AiTranscriptPartKind.Tool, AiTranscriptPartKind.Response }, parts.Select(part => part.Kind).ToArray());
        Assert.AreEqual("先检查实际日志。", parts[0].Text);
        Assert.AreEqual("正在读取。", parts[1].Text);
        Assert.AreEqual("read_file_lines", parts[2].Name);
        Assert.AreEqual("launch.log", JsonNode.Parse(parts[2].Arguments)!["path"]!.ToString());
        Assert.AreEqual("Observed log", JsonNode.Parse(parts[2].Result)!["lines"]![0]!.ToString());
        Assert.AreEqual("已读取真实结果。", parts[3].Text);
        var visible = string.Join("\n", parts.Select(part => part.Text + part.Name + part.Arguments + part.Result + part.Status));
        Assert.DoesNotContain("PRIVATE-", visible);
    }

    /// <summary>
    /// Reused provider call IDs in different invocations must not overwrite each other. An unavailable/canceled result
    /// remains visibly unavailable, and opaque reasoning evidence must never become fabricated thinking text.
    /// </summary>
    [TestMethod]
    public void IndependentInvocations_KeepTheirResults_AndNeverInventUnavailableContent()
    {
        var parts = AiTranscriptContent.Read([
            Event(1, "reasoning_started", "thinking", """{"contentAvailability":"not_returned"}"""),
            Event(2, "reasoning_progress", "thinking", """{"reasoningTokens":17,"opaque":"HIDDEN"}"""),
            Event(3, "tool_started", "first", """{"name":"read","toolCallId":"same","arguments":{"path":"a"}}"""),
            Event(4, "tool_started", "second", """{"name":"read","toolCallId":"same","arguments":{"path":"b"}}"""),
            Event(5, "tool_finished", "second", """{"name":"read","status":"canceled","resultUnavailable":true}"""),
            Event(6, "tool_finished", "first", """{"name":"read","status":"succeeded","result":{"text":"first result"}}"""),
        ], string.Empty, true);
        Assert.HasCount(2, parts);
        Assert.DoesNotContain(part => part.Kind == AiTranscriptPartKind.Thinking, parts);
        Assert.AreEqual("first result", JsonNode.Parse(parts[0].Result)!["text"]!.ToString());
        Assert.AreEqual("b", JsonNode.Parse(parts[1].Arguments)!["path"]!.ToString());
        Assert.AreEqual("canceled", parts[1].Status);
        Assert.AreEqual(string.Empty, parts[1].Result);
        Assert.IsTrue(parts[1].Complete);
    }

    /// <summary>Unavailable deltas keep the saved answer; repeated component warnings coalesce while distinct warnings remain visible.</summary>
    [TestMethod]
    public void MissingDeltas_UsePersistedAnswer_WhileWarningsExcludeDiagnosticEnvelopes()
    {
        var parts = AiTranscriptContent.Read([
            Event(1, "tool_catalog_error", string.Empty, """{"error":{"code":"PRIVATE-CODE","message":"工具暂时不可用"},"ordinaryConversationAvailable":true}"""),
            Event(2, "managed_mcp_unavailable", "files", """{"id":"files","error":{"code":"PRIVATE-CODE","message":"工具暂时不可用"}}"""),
            Event(3, "managed_mcp_unavailable", "web", """{"id":"web","error":{"code":"PRIVATE-CODE","message":"工具暂时不可用"}}"""),
            Event(4, "context_compaction_warning", "context", """{"message":"原文仍然保留"}"""),
        ], "已保存的回答", true);
        Assert.HasCount(3, parts);
        Assert.AreEqual(AiTranscriptPartKind.Notice, parts[0].Kind);
        Assert.AreEqual("工具暂时不可用", parts[0].Text);
        Assert.AreEqual(AiTranscriptPartKind.Notice, parts[1].Kind);
        Assert.AreEqual("原文仍然保留", parts[1].Text);
        Assert.AreEqual(AiTranscriptPartKind.Response, parts[2].Kind);
        Assert.AreEqual("已保存的回答", parts[2].Text);
    }

    private static readonly long[] expected = [0L, 2L, 4L];
    private static readonly string[] expectedArray = ["Ready to execute.", "Done."];
    private static readonly string[] questionIds = ["first", "second"];

    /// <summary>
    /// Completing a turn between the event read and status read used to lose the final tool result and answer.
    /// Unlike pure projection cases, this runs the production poll: the typed service boundary publishes the tail
    /// only after returning an immutable initial page, then paginates it. No timer or model response establishes the race.
    /// </summary>
    [TestMethod]
    public async Task CompletionBetweenReads_DrainsLateToolResultAndFinalMessage()
    {
        var published = new List<AiChatEvent>
        {
            Event(1, "text_delta", "before", """{"text":"Ready to execute."}"""),
            Event(2, "tool_started", "write", """{"name":"execute","arguments":{"key":"minMemoryMb"}}""")
        };
        var cursors = new List<long>();
        Task<AiEventPage> ReadEvents(long after)
        {
            cursors.Add(after);
            var remaining = published.Where(item => item.Sequence > after).ToArray();
            var page = remaining.Take(2).ToArray();
            return Task.FromResult(new AiEventPage
            {
                Items = page,
                NextSequence = page.LastOrDefault()?.Sequence ?? after,
                HasMore = remaining.Length > page.Length
            });
        }

        Task<AiChatRecord> ReadTurn()
        {
            Assert.HasCount(1, cursors, "Completion must happen after the initial event page was returned.");
            published.AddRange([
                Event(3, "tool_finished", "write", """{"name":"execute","status":"succeeded","result":{"stored":true}}"""),
                Event(4, "text_delta", "after", """{"text":"Done."}"""),
                Event(5, "message_finished", "after", "{}"),
                Event(6, "turn_finished", string.Empty, """{"status":"completed"}""")
            ]);
            return Task.FromResult(new AiChatRecord { Status = "completed", Answer = "Done." });
        }

        var (Turn, Events, NextSequence) = await AiTurnObservation.ReadAsync(0, ReadEvents, ReadTurn);
        CollectionAssert.AreEqual(expected, cursors);
        Assert.AreEqual(6, NextSequence);
        var parts = AiTranscriptContent.Read(Events, Turn.Answer!, true);
        var tool = parts.Single(part => part.Kind == AiTranscriptPartKind.Tool);
        Assert.AreEqual("succeeded", tool.Status);
        Assert.IsTrue(JsonNode.Parse(tool.Result)!["stored"]!.GetValue<bool>());
        CollectionAssert.AreEqual(expectedArray,
            parts.Where(part => part.Kind == AiTranscriptPartKind.Response).Select(part => part.Text).ToArray());
    }

    /// <summary>Uses explicit event timestamps as the oracle: later usage cannot extend completed steps, and missing
    /// clocks must not manufacture elapsed time. This extends the content projection boundary without a native timer.</summary>
    [TestMethod]
    public void StepElapsed_UsesItsOwnEventBoundaries_AndHidesMissingClocks()
    {
        var parts = AiTranscriptContent.Read([
            Event(1, "reasoning_started", "reason", "{}", 0),
            Event(2, "reasoning_delta", "reason", """{"text":"Checking."}""", 1),
            Event(3, "reasoning_finished", "reason", "{}", 2.5),
            Event(4, "tool_started", "tool", """{"name":"read"}""", 3),
            Event(5, "tool_finished", "tool", """{"status":"succeeded"}""", 7),
            Event(6, "tool_started", "missing-clock", """{"name":"other"}"""),
            Event(7, "turn_finished", string.Empty, "{}", 50),
        ], string.Empty, true);
        Assert.AreEqual(TimeSpan.FromSeconds(2.5), parts[0].Elapsed);
        Assert.AreEqual(TimeSpan.FromSeconds(4), parts[1].Elapsed);
        Assert.IsNull(parts[2].Elapsed);
    }

    /// <summary>
    /// One question must remain one card through waiting, answering and tool completion. Unlike ordinary tool pairing,
    /// this protects the join across prompt and tool events, including reused provider IDs and identical questions.
    /// Explicit receipts independently identify each answer; elapsed wait time and duplicate tools must not leak.
    /// </summary>
    [TestMethod]
    public void Questions_JoinExactInvocations_AndKeepAnsweredAndSkippedReceiptsWithoutTiming()
    {
        var events = new List<AiChatEvent>
        {
            Event(1, "tool_started", "first", """{"name":"ask_user","toolCallId":"reused","arguments":{"question":"Which?"}}""", 0),
            Event(2, "user_prompt_required", "first", """{"id":"first","question":{"question":"Which?"}}""", 1),
            Event(3, "tool_started", "second", """{"name":"ask_user","toolCallId":"reused","arguments":{"question":"Which?"}}""", 2),
            Event(4, "user_prompt_required", "second", """{"id":"second","question":{"question":"Which?"}}""", 3),
        };
        var waiting = AiTranscriptContent.Read(events, string.Empty, false);
        Assert.HasCount(2, waiting);
        CollectionAssert.AreEquivalent(questionIds, waiting.Select(part => part.PromptId).ToArray());
        Assert.IsTrue(waiting.All(part => part.Kind == AiTranscriptPartKind.Question && !part.Complete && part.Elapsed is null));
        events.AddRange([
            Event(5, "user_prompt_answered", "second", """{"promptId":"second","question":"Which?","status":"skipped","choices":[],"text":""}""", 20),
            Event(6, "tool_finished", "second", """{"name":"ask_user","status":"succeeded"}""", 21),
            Event(7, "user_prompt_answered", "first", """{"promptId":"first","question":"Which?","status":"answered","choices":[{"id":"java","label":"Java"}],"text":"Check version"}""", 30),
            Event(8, "tool_finished", "first", """{"name":"ask_user","status":"succeeded"}""", 31),
        ]);
        var parts = AiTranscriptContent.Read(events, string.Empty, true);
        Assert.HasCount(2, parts, "Tool completion must update the question rather than append another card.");
        Assert.AreEqual("Java · Check version", parts.Single(part => part.PromptId == "first").Answer);
        Assert.AreEqual("answered", parts.Single(part => part.PromptId == "first").AnswerStatus);
        Assert.AreEqual("skipped", parts.Single(part => part.PromptId == "second").AnswerStatus);
        Assert.IsTrue(parts.All(part => part.Kind == AiTranscriptPartKind.Question && part.Complete && part.Elapsed is null));
    }

    /// <summary>Special question presentation must retain cancellation and invalid-input errors without inventing
    /// a receipt. This adds the missing terminal boundary to the answered/skipped association case.</summary>
    [TestMethod]
    [DataRow("canceled", "")]
    [DataRow("failed", "Supply a nonempty question.")]
    public void UnansweredQuestion_KeepsItsTerminalOutcome(string status, string message)
    {
        var parts = AiTranscriptContent.Read([
            Event(1, "tool_started", "question", """{"name":"ask_user","arguments":{"question":"Which?"}}""", 0),
            Event(2, "tool_finished", "question", new JsonObject
            {
                ["name"] = "ask_user", ["status"] = status,
                ["result"] = new JsonObject { ["error"] = new JsonObject { ["message"] = message } }
            }.ToJsonString(), 30)
        ], string.Empty, true);
        Assert.HasCount(1, parts);
        Assert.AreEqual(AiTranscriptPartKind.Question, parts[0].Kind);
        Assert.AreEqual(status, parts[0].Status);
        Assert.AreEqual(message, parts[0].Error);
        Assert.AreEqual(string.Empty, parts[0].AnswerStatus);
        Assert.IsNull(parts[0].Elapsed);
    }

    private static AiChatEvent Event(long sequence, string kind, string step, string data, double? seconds = null) => new()
    {
        Sequence = sequence,
        Kind = kind,
        Data = JsonNode.Parse(data)!.AsObject(),
        AdditionalData = seconds is { } time
            ? new() { ["stepId"] = JsonSerializer.SerializeToElement(step), ["timeUtc"] = JsonSerializer.SerializeToElement(DateTimeOffset.UnixEpoch.AddSeconds(time).ToString("O")) }
            : new() { ["stepId"] = JsonSerializer.SerializeToElement(step) }
    };
}