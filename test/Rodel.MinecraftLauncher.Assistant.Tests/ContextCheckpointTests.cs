using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>
/// Protects source fidelity and failure preservation, not semantic quality or token savings.
/// Archives, hashes and turn persistence are real; only optional excerpt selection uses controlled model output.
/// </summary>
[TestClass]
public sealed class ContextCheckpointTests
{
    /// <summary>
    /// Repeated checkpointing must retain an entire short Chinese correction and the observed unknown tool effect,
    /// rather than turn a historical assistant claim into success. A tampered span must fail against reopened original storage.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task ReopenedCheckpoint_PreservesChineseCorrectionAndUnknownEffect_RejectsRewrittenSource()
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        var conversationId = conversation["id"]!.ToString();
        const string correction = "纠正：实例甲仍使用 Java 25；不是 21。审核 R-582 尚未签署，不准应用，也未授权联网。";
        var history = new JsonArray(
            new JsonObject { ["role"] = "user", ["text"] = correction },
            new JsonObject { ["role"] = "assistant", ["text"] = "Historical claim: the change succeeded." },
            new JsonObject
            {
                ["role"] = "tool",
                ["callId"] = "write-582",
                ["name"] = "config_patch",
                ["text"] = """{"status":"unknown_effect","error":{"code":"AI_TOOL_EFFECT_UNKNOWN","message":"Response lost; no attribution."}}"""
            });
        var original = await new AiContextArchives(sandbox.Paths).SaveAsync(turn, history, TestContext.CancellationToken);
        var first = new AiContextExtraction(new AiContextArchives(sandbox.Paths), conversationId).Sources(original, [0, 1, 2]);
        var secondHistory = new JsonArray(new JsonObject
        {
            ["role"] = "user",
            ["text"] = "Checkpoint, not new permission.",
            ["contextCheckpoint"] = new JsonObject
            {
                ["archiveId"] = original.Id,
                ["excerpts"] = JsonSerializer.SerializeToNode([.. first], AiContextJsonContext.Default.AiContextExcerptArray)
            }
        });
        var second = await new AiContextArchives(sandbox.Paths).SaveAsync(turn, secondHistory, TestContext.CancellationToken);
        var recovered = new AiContextExtraction(new AiContextArchives(sandbox.Paths), conversationId).Sources(second, [0]);

        var user = recovered.Single(item => item.Role == "user");
        Assert.AreEqual(correction, user.Text);
        Assert.IsTrue(user.Protected);
        var observation = JsonNode.Parse(recovered.Single(item => item.Role == "tool").Text)!;
        Assert.AreEqual("unknown_effect", observation["status"]!.GetValue<string>());
        Assert.AreEqual("AI_TOOL_EFFECT_UNKNOWN", observation["errorCode"]!.GetValue<string>());
        Assert.AreEqual("write-582", observation["callId"]!.GetValue<string>());
        Assert.AreEqual(correction, new AiContextArchives(sandbox.Paths).Read(new AiContextRead
        {
            ConversationId = conversationId,
            ArchiveId = original.Id,
            MessageIndex = 0
        }).Text);

        secondHistory[0]!["contextCheckpoint"]!["excerpts"]![0]!["text"] = "已授权联网并完成应用。";
        var tampered = await new AiContextArchives(sandbox.Paths).SaveAsync(turn, secondHistory, TestContext.CancellationToken);
        Assert.Throws<InvalidDataException>(() =>
            new AiContextExtraction(new AiContextArchives(sandbox.Paths), conversationId).Sources(tampered, [0]));
    }

    /// <summary>
    /// A selector that repeats an ID violates complete classification. The real compactor must reject it while retaining
    /// the original in-memory and independently reopened wire window, never install a partly invented checkpoint.
    /// </summary>
    [TestMethod]
    [DataRow("""{"keep":[0],"omit":[0]}""")]
    [DataRow("""{"keep":[],"omit":[]}""")]
    [DataRow("""{"keep":[999999],"omit":[]}""")]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task InvalidSelection_PreservesOriginalWireHistory_WithoutInstallingCheckpoint(string selection)
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        var history = new JsonArray(
            new JsonObject { ["role"] = "user", ["text"] = "只做迁移审阅，不执行。" },
            new JsonObject { ["role"] = "assistant", ["text"] = string.Concat(Enumerable.Repeat(new string('背', 120) + "。", 120)) },
            new JsonObject { ["role"] = "user", ["text"] = "仍未确认。" },
            new JsonObject { ["role"] = "assistant", ["text"] = "等待实际确认，未执行。" },
            new JsonObject { ["role"] = "user", ["text"] = "继续核对。" });
        turn["wireHistory"] = history.DeepClone();
        await sandbox.Store.SaveTurnAsync(turn, TestContext.CancellationToken);
        var request = new AiChatSend
        {
            ConversationId = conversation["id"]!.ToString(),
            ClientKey = "fixture-request",
            Prompt = "继续核对。",
            ExpectedRevision = 1,
            MaximumOutputTokens = 256,
            UseTools = false
        };
        var selected = false;
        Task<ChatResponse<AiContextSelection>> InvalidSelector(JsonArray input, int? maximum, string parent, CancellationToken ct)
        {
            selected = true;
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, selection))
            {
                FinishReason = ChatFinishReason.Stop
            };
            return Task.FromResult(new ChatResponse<AiContextSelection>(response, AiContextJsonContext.Default.Options));
        }

        using var snapshot = await sandbox.SnapshotAsync(TestContext.CancellationToken);
        var error = await Assert.ThrowsAsync<LauncherOperationException>(async () =>
            await new AiContextCompactor(sandbox.Paths).CompactAsync(snapshot,
                history, [], request, 12000, turn, "budget", InvalidSelector, (_, _, _) => Task.CompletedTask, TestContext.CancellationToken));
        Assert.IsTrue(selected, "A budget/setup failure before the controlled selector would not exercise classification rejection.");
        Assert.AreEqual("AI_CONTEXT_SELECTION_INVALID", error.Code);
        Assert.IsTrue(JsonNode.DeepEquals(history, turn["wireHistory"]));
        var reopened = new AiChatStore(sandbox.Paths).GetTurnDocument(request.ConversationId, turn["id"]!.ToString(), true);
        Assert.IsTrue(JsonNode.DeepEquals(history, reopened["wireHistory"]));
        Assert.IsNull(reopened["contextCheckpoint"]);
        Assert.AreEqual("running", turn["status"]!.GetValue<string>());
    }

    /// <summary>A large fixed tool schema can make the 85% trigger unreachable even after a useful source-preserving reduction.
    /// Existing source-selection tests have small fixed overhead. This case requires the actual serialized request to fit
    /// its hard budget, preserve current call/result pairing and retain the complete archived result, without replay.
    /// The current preview image must remain model-visible when only its large tool text becomes a reference.</summary>
    [TestMethod]
    [DataRow(false, 20000, AiProtocols.OpenAiChat)]
    [DataRow(true, 4000, AiProtocols.OpenAiChat)]
    [DataRow(false, 20000, AiProtocols.OpenAiResponses)]
    [DataRow(true, 4000, AiProtocols.OpenAiResponses)]
    [DataRow(false, 900, AiProtocols.OpenAiChat)]
    [DataRow(false, 900, AiProtocols.OpenAiResponses)]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task LargeFixedCatalog_CompactsUnderHardBudget_WithoutRequiringSoftWatermark(bool olderBackground, int resultCharacters, string protocol)
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        var originalResult = new JsonObject { ["status"] = "succeeded", ["catalog"] = new string('c', resultCharacters) }.ToJsonString();
        var history = new JsonArray(new JsonObject { ["role"] = "system", ["text"] = new string('s', 4000) },
            new JsonObject { ["role"] = "user", ["text"] = "只核对当前实例，不执行变更。" });
        if (olderBackground)
        {
            history.Add((JsonNode)new JsonObject { ["role"] = "assistant", ["text"] = new string('b', 4000) });
            history.Add((JsonNode)new JsonObject { ["role"] = "user", ["text"] = "继续只读核对。" });
        }

        history.Add((JsonNode)new JsonObject { ["role"] = "assistant", ["text"] = string.Empty, ["calls"] = new JsonArray(new JsonObject { ["id"] = "catalog-call", ["name"] = "capabilities", ["arguments"] = new JsonObject() }) });
        history.Add((JsonNode)new JsonObject { ["role"] = "tool", ["callId"] = "catalog-call", ["name"] = "capabilities", ["text"] = originalResult });
        var originalIndex = history.Count - 1;
        var request = new AiChatSend { ConversationId = conversation["id"]!.ToString(), ClientKey = "catalog-floor", Prompt = "继续只读核对。", ExpectedRevision = 1 };
        AIFunctionDeclaration[] tools = [AiToolDeclarations.Create("capabilities", new string('d', olderBackground ? 57000 : 52000), new JsonObject { ["type"] = "object" }), AiContextCompactor.ReadTool()];
        static Task<ChatResponse<AiContextSelection>> OmitBackground(JsonArray input, int? maximum, string parent, CancellationToken ct)
        {
            var ids = input.Where(item => item?["text"]?.ToString().StartsWith("EXCERPT ", StringComparison.Ordinal) == true)
                .Select(item => JsonNode.Parse(item!["text"]!.ToString().Split('\n')[0]["EXCERPT ".Length..])!["id"]!.DeepClone()).ToArray();
            return Task.FromResult(new ChatResponse<AiContextSelection>(
                new(new ChatMessage(ChatRole.Assistant, new JsonObject { ["keep"] = new JsonArray(), ["omit"] = new JsonArray(ids) }.ToJsonString()))
                { FinishReason = ChatFinishReason.Stop }, AiContextJsonContext.Default.Options));
        }

        using var snapshot = await sandbox.SnapshotAsync(TestContext.CancellationToken, protocol);
        var illustrated = AiChatHistory.Read(new JsonArray(history[originalIndex]!.DeepClone())).Single();
        illustrated.Contents.Add(new DataContent(new byte[] { 1, 2, 3 }, "image/png"));
        AiChatMessages.Bind(illustrated, new(snapshot.Service.Protocol, snapshot.Model!.Id, snapshot.Service.Revision, new JsonObject()));
        history[originalIndex] = AiChatHistory.Write(illustrated);
        if (protocol == AiProtocols.OpenAiResponses)
        {
            BindNative(snapshot, history[originalIndex - 1]!.AsObject(), new JsonArray(
                new JsonObject { ["type"] = "reasoning", ["id"] = "opaque", ["encrypted_content"] = "signed-current-state" },
                new JsonObject { ["type"] = "function_call", ["call_id"] = "catalog-call", ["name"] = "capabilities", ["arguments"] = "{}" }));
        }

        var before = AiContextCompactor.Measure(snapshot, history, tools, request.MaximumOutputTokens);
        // A small current result can save a real request just over the limit; an unnecessary empty checkpoint must not erase that gain.
        var maximumCharacters = resultCharacters == 900 ? before - 100 : 64000;
        var compacted = await new AiContextCompactor(sandbox.Paths).CompactAsync(snapshot, history, tools, request, maximumCharacters, turn, "history_budget", OmitBackground, (_, _, _) => Task.CompletedTask, TestContext.CancellationToken);
        Assert.IsNotNull(compacted);
        var after = AiContextCompactor.Measure(snapshot, compacted, tools, request.MaximumOutputTokens);
        Assert.IsGreaterThan(maximumCharacters, before);
        Assert.IsGreaterThan(maximumCharacters * .85, after, "The fixture must exercise a usable request above the early trigger.");
        Assert.IsTrue(after < before && after <= maximumCharacters);
        var reference = JsonNode.Parse(compacted.Single(item => item?["role"]?.ToString() == "tool")!["text"]!.ToString())!;
        Assert.IsTrue(reference["archivedToolResult"]!.GetValue<bool>());
        Assert.AreEqual("catalog-call", compacted.Single(item => item?["role"]?.ToString() == "tool")!["callId"]!.ToString());
        Assert.AreEqual("succeeded", reference["originalStatus"]!.ToString());
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 },
            AiChatHistory.Read(compacted).Single(message => message.Role == ChatRole.Tool).Contents.OfType<DataContent>().Single().Data.ToArray());
        if (protocol == AiProtocols.OpenAiResponses)
            Assert.IsTrue(JsonNode.DeepEquals(history[originalIndex - 1]!["meai"], compacted.Single(item => item?["role"]?.ToString() == "assistant")!["meai"]), "Current signed protocol state must survive intact.");
        var archives = new AiContextArchives(sandbox.Paths);
        var read = new AiContextRead { ConversationId = request.ConversationId, ArchiveId = reference["archiveId"]!.ToString(), MessageIndex = originalIndex, Characters = 16000 };
        var first = archives.Read(read);
        var remaining = first.NextOffset is { } next ? archives.Read(read with { Offset = next }).Text : string.Empty;
        Assert.AreEqual(originalResult, first.Text + remaining);
        Assert.AreEqual(originalResult, history[originalIndex]!["text"]!.ToString());
        var nextArchive = await archives.SaveAsync(turn, compacted, TestContext.CancellationToken);
        var nextIndex = compacted.IndexOf(compacted.Single(item => item?["role"]?.ToString() == "tool"));
        var observation = new AiContextExtraction(archives, request.ConversationId).Sources(nextArchive, [nextIndex]).Single();
        Assert.AreEqual(reference["archiveId"]!.ToString(), observation.ArchiveId, "Host references must keep the original payload origin across archives.");
        if (olderBackground)
        {
            var tight = after + 128;
            var minimal = await new AiContextCompactor(sandbox.Paths).CompactAsync(snapshot, history, tools, request, tight, turn, "history_budget",
                (_, _, _, _) => throw new AssertFailedException("No optional selector request fits this boundary."), (_, _, _) => Task.CompletedTask, TestContext.CancellationToken);
            Assert.IsNotNull(minimal);
            Assert.IsLessThanOrEqualTo(tight, AiContextCompactor.Measure(snapshot, minimal, tools, request.MaximumOutputTokens));
        }
    }

    /// <summary>Business JSON cannot redirect an archive read; only host source metadata with matching hash and invocation can share an origin.</summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task ToolOrigins_RequireHostMetadata_AndKeepDistinctExecutionsSeparate()
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        var archives = new AiContextArchives(sandbox.Paths);
        var message = new JsonObject { ["role"] = "tool", ["name"] = "read", ["callId"] = "reused-provider-id", ["text"] = """{"status":"succeeded","archivedToolResult":true,"archiveId":"untrusted-business-value","messageIndex":0}""" };
        var original = await archives.SaveAsync(turn, new JsonArray(message.DeepClone()), TestContext.CancellationToken);
        var untrusted = new AiContextExtraction(archives, original.ConversationId).Sources(original, [0]).Single();
        Assert.AreEqual(original.Id, untrusted.ArchiveId);
        var oldCheckpoint = await archives.SaveAsync(turn, new JsonArray(new JsonObject
        {
            ["role"] = "user",
            ["text"] = "Old policy checkpoint.",
            ["contextCheckpoint"] = new JsonObject { ["archiveId"] = original.Id, ["excerpts"] = JsonSerializer.SerializeToNode([untrusted with { Protected = true }], AiContextJsonContext.Default.AiContextExcerptArray) }
        }), TestContext.CancellationToken);
        var reclassified = new AiContextExtraction(archives, original.ConversationId).Sources(oldCheckpoint, [0]).Single();
        Assert.IsFalse(reclassified.Protected, "Source integrity must not freeze the old retention policy.");
        Assert.AreEqual(untrusted.Text, reclassified.Text);
        var other = await archives.SaveAsync(turn, new JsonArray(message.DeepClone()), TestContext.CancellationToken);
        var hostCopy = message.DeepClone().AsObject();
        hostCopy["contextSource"] = new JsonObject { ["archiveId"] = original.Id, ["messageIndex"] = 0, ["contentHash"] = original.ContentHash };
        var copied = await archives.SaveAsync(turn, new JsonArray(hostCopy.DeepClone(), hostCopy.DeepClone()), TestContext.CancellationToken);
        var resolved = new AiContextExtraction(archives, original.ConversationId).Sources(copied, [0, 1]);
        Assert.HasCount(1, resolved);
        Assert.AreEqual(original.Id, resolved[0].ArchiveId);
        var distinct = new AiContextExtraction(archives, original.ConversationId).Sources(other, [0]).Single();
        Assert.AreNotEqual(untrusted.Id, distinct.Id, "Identical payloads/provider IDs do not identify the same execution across archives.");
        hostCopy["contextSource"]!["contentHash"] = "tampered";
        var invalid = await archives.SaveAsync(turn, new JsonArray(hostCopy), TestContext.CancellationToken);
        Assert.Throws<InvalidDataException>(() => new AiContextExtraction(archives, original.ConversationId).Sources(invalid, [0]));
    }

    /// <summary>
    /// Completed write/read exchanges must not become the next question's mandatory protocol window.
    /// Twelve compactions under a large fixed catalog retain exact user constraints and archived results,
    /// while omitted successful observations do not grow a permanent receipt floor or replay prior calls.
    /// </summary>
    [TestMethod]
    [DataRow(AiProtocols.OpenAiChat)]
    [DataRow(AiProtocols.OpenAiResponses)]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task ConsecutiveQuestions_ArchiveCompletedExchanges_WithoutGrowingMandatoryReceipts(string protocol)
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        using var snapshot = await sandbox.SnapshotAsync(TestContext.CancellationToken, protocol);
        const string constraint = "仅检查当前配置，Java 25，不准重放历史写入。";
        var history = new JsonArray(new JsonObject { ["role"] = "system", ["text"] = new string('s', 4500) },
            new JsonObject { ["role"] = "user", ["text"] = constraint });
        AIFunctionDeclaration[] tools = [AiToolDeclarations.Create("fixture", new string('d', 54000), new JsonObject { ["type"] = "object" }), AiContextCompactor.ReadTool()];
        for (var round = 0; round < 12; round++)
        {
            var turn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
            var result = new JsonObject { ["status"] = "succeeded", ["value"] = new string('v', 1000) }.ToJsonString();
            foreach (var name in new[] { "config_patch", "instance_show" })
            {
                var call = name + "-" + round;
                var assistant = new JsonObject { ["role"] = "assistant", ["text"] = string.Empty, ["calls"] = new JsonArray(new JsonObject { ["id"] = call, ["name"] = name, ["arguments"] = new JsonObject() }) };
                if (protocol == AiProtocols.OpenAiResponses)
                    BindNative(snapshot, assistant, new JsonArray(new JsonObject { ["type"] = "function_call", ["call_id"] = call, ["name"] = name, ["arguments"] = "{}" }));
                history.Add((JsonNode)assistant);
                history.Add((JsonNode)new JsonObject { ["role"] = "tool", ["name"] = name, ["callId"] = call, ["text"] = result });
            }

            var answer = new JsonObject { ["role"] = "assistant", ["text"] = new string('a', 3000) };
            if (protocol == AiProtocols.OpenAiResponses)
                BindNative(snapshot, answer, new JsonArray(new JsonObject { ["type"] = "message", ["role"] = "assistant", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = new string('a', 3000) }) }));
            history.Add((JsonNode)answer);
            var prompt = "独立的只读问题 " + round + "，不继续先前操作。";
            history.Add((JsonNode)new JsonObject { ["role"] = "user", ["text"] = prompt });
            var request = new AiChatSend { ConversationId = conversation["id"]!.ToString(), ClientKey = Guid.NewGuid().ToString("N"), Prompt = prompt, ExpectedRevision = 1 };
            var compacted = await new AiContextCompactor(sandbox.Paths).CompactAsync(snapshot, history, tools, request, 64000, turn, "history_budget", OmitOptionalHistory, (_, _, _) => Task.CompletedTask, TestContext.CancellationToken);
            Assert.IsNotNull(compacted, "The completed exchange must be available for archival.");
            Assert.IsLessThanOrEqualTo(64000, AiContextCompactor.Measure(snapshot, compacted, tools, request.MaximumOutputTokens), "Each new question must fit the same budget.");
            Assert.DoesNotContain(item => item?["role"]?.ToString() is "assistant" or "tool", compacted, "Previous completed protocol state is not the current exchange.");
            var checkpoint = turn["contextCheckpoint"]!;
            var archive = new AiContextArchives(sandbox.Paths).Load(request.ConversationId, checkpoint["archiveId"]!.ToString());
            Assert.HasCount(2, archive.Messages.Where(item => item?["text"]?.ToString() == result).ToArray());
            Assert.Contains(constraint, compacted.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            Assert.DoesNotContain(item => item?["length"]?.GetValue<int>() == 0, checkpoint["excerpts"]!.AsArray(), "Omitted successful receipts must not remain in the mandatory checkpoint.");
            history = compacted;
        }
    }

    /// <summary>Large source pools must split before the selector itself overflows; every source is still classified once and returned verbatim.</summary>
    [TestMethod]
    public async Task LocalSelection_PacketsFitContext_WithoutDroppingSourcesOrCappingOutput()
    {
        using var sandbox = new AiSandbox();
        var services = new AiServiceStore(sandbox.Paths);
        var state = await services.SaveServiceAsync(new() { Protocol = AiProtocols.RodelInference, Models = [new() { Model = "fixture", ContextLength = 8192 }] }, ct: TestContext.CancellationToken);
        var service = state.Services.Single();
        using var snapshot = await services.AcquireAsync(service.Id, service.Models.Single().Id, true, TestContext.CancellationToken);
        var sources = Enumerable.Range(0, 8).Select(index =>
        {
            var text = string.Join(' ', Enumerable.Range(0, 900).Select(n => "observation" + n));
            return new AiContextExcerpt("source:" + index, "archive", index, 0, text.Length, "user", text, false);
        }).ToList();
        var packets = 0; var observed = 0;
        Task<ChatResponse<AiContextSelection>> Select(JsonArray input, int? output, string parent, CancellationToken ct)
        {
            packets++;
            Assert.IsNull(output, "No user/model output cap was supplied.");
            // Independent full-input observation: without batching this exceeds even the entire 8192-token context.
            Assert.IsLessThan(8192L, AiTokenCounter.Estimate(snapshot.ProviderContext(), AiChatHistory.Read(input), [], null)!.Value);
            var count = input.Count - 2; observed += count;
            return Task.FromResult(new ChatResponse<AiContextSelection>(new(new ChatMessage(ChatRole.Assistant,
                System.Text.Json.JsonSerializer.Serialize(new AiContextSelection([.. Enumerable.Range(0, count)], []), AiContextJsonContext.Default.AiContextSelection)))
            { FinishReason = ChatFinishReason.Stop }, AiContextJsonContext.Default.Options));
        }

        var selected = await AiContextExtraction.ChooseAsync(snapshot, sources, "Retain notes", "Read notes", 1000000,
            new() { ConversationId = "conversation", Prompt = "Read notes", ClientKey = "case", ExpectedRevision = 0 }, 1000000, "parent", Select, (_, _, _) => Task.CompletedTask, TestContext.CancellationToken);
        Assert.IsGreaterThan(1, packets);
        Assert.AreEqual(sources.Count, observed);
        CollectionAssert.AreEqual(sources.ToArray(), selected.ToArray());
    }

    private static Task<ChatResponse<AiContextSelection>> OmitOptionalHistory(JsonArray input, int? maximum, string parent, CancellationToken ct)
    {
        var ids = input.Where(item => item?["text"]?.ToString().StartsWith("EXCERPT ", StringComparison.Ordinal) == true)
            .Select(item => JsonNode.Parse(item!["text"]!.ToString().Split('\n')[0]["EXCERPT ".Length..])!["id"]!.DeepClone()).ToArray();
        return Task.FromResult(new ChatResponse<AiContextSelection>(
            new(new ChatMessage(ChatRole.Assistant, new JsonObject { ["keep"] = new JsonArray(), ["omit"] = new JsonArray(ids) }.ToJsonString()))
            { FinishReason = ChatFinishReason.Stop }, AiContextJsonContext.Default.Options));
    }

    private static void BindNative(AiServiceSnapshot snapshot, JsonObject record, JsonArray payload)
    {
        var message = AiChatHistory.Read(new JsonArray(record.DeepClone()))[0];
        AiChatMessages.Bind(message, new(snapshot.Service.Protocol, snapshot.Model!.Id, snapshot.Service.Revision, payload));
        record["meai"] = AiChatHistory.Write(message)["meai"]!.DeepClone();
    }

    /// <summary>
    /// Archived text must round-trip across Unicode page boundaries, remain conversation-scoped and fail closed after corruption.
    /// These independent storage checks replace the archive portion of the old compaction process probe, not semantic compression scoring.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task ArchivePages_RestoreExactUnicode_RejectForeignScopeAndCorruptContent()
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        var text = new string('中', 255) + "🙂" + new string('文', 270);
        var archives = new AiContextArchives(sandbox.Paths);
        var saved = await archives.SaveAsync(turn, new JsonArray(new JsonObject { ["role"] = "user", ["text"] = text }), TestContext.CancellationToken);
        var collected = new System.Text.StringBuilder();
        var offset = 0;
        do
        {
            var page = archives.Read(new AiContextRead { ConversationId = saved.ConversationId, ArchiveId = saved.Id, MessageIndex = 0, Offset = offset, Characters = 256 });
            collected.Append(page.Text);
            if (!page.HasMore) break;
            var next = page.NextOffset!.Value;
            Assert.IsTrue(next > offset && next < text.Length, "Archive pagination must make progress within the original text.");
            offset = next;
        } while (true);
        Assert.AreEqual(text, collected.ToString());
        var other = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var foreign = Assert.Throws<LauncherOperationException>(() => archives.Read(new AiContextRead
        {
            ConversationId = other["id"]!.ToString(),
            ArchiveId = saved.Id
        }));
        Assert.AreEqual("AI_CONTEXT_ARCHIVE_NOT_FOUND", foreign.Code);

        var file = Path.Combine(sandbox.Root, "ai", "conversations", saved.ConversationId, "context-archives", saved.Id + ".json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.CancellationToken))!;
        document["messages"]![0]!["text"] = "Invented replacement.";
        await File.WriteAllTextAsync(file, document.ToJsonString(), TestContext.CancellationToken);
        var corrupt = Assert.Throws<LauncherOperationException>(() => new AiContextArchives(sandbox.Paths).Read(new AiContextRead
        {
            ConversationId = saved.ConversationId,
            ArchiveId = saved.Id
        }));
        Assert.AreEqual("AI_CONTEXT_ARCHIVE_INVALID", corrupt.Code);
    }

    /// <summary>Gets or sets MSTest's bounded cancellation context.</summary>
    public TestContext TestContext { get; set; }
}