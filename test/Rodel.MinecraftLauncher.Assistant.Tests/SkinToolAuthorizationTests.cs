// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.Skins;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects scoped skin authorization and journal integration; direct-grant cases use real Core tools and storage.</summary>
[TestClass]
public sealed class SkinToolAuthorizationTests
{
    /// <summary>
    /// Author review: prior skin flows supplied allow-operations and missed the native editor's default safe mode.
    /// The actual tool must commit a direct grant without a second approval; a candidate grant must still stop
    /// before publication. Cancel at the real confirmation event so the broken path fails without a timed wait.
    /// </summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task SafeSkinTool_UsesOnlyTheHostDirectGrant_WithoutBroadPermission(bool direct)
    {
        using var sandbox = new AiSandbox();
        var skins = new SkinService(sandbox.Paths);
        var id = await skins.CreateAsync(SkinModel.Classic, template: "blank", cancellationToken: TestContext.CancellationToken);
        var original = skins.Read(id);
        var scope = await skins.Proposals.CreateScopeAsync(id,
            direct ? new(DirectEditing: true) : new("design", AllowLayerChanges: true), TestContext.CancellationToken);
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        conversation["mode"] = "safe";
        conversation["skinScopeId"] = scope.ScopeId;
        conversation["fileAccessEnabled"] = false;
        conversation["networkAccessEnabled"] = false;
        await sandbox.Store.SaveConversationAsync(conversation, TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        turn["mode"] = "safe";
        turn["skinScopeId"] = scope.ScopeId;
        turn["fileAccessEnabled"] = false;
        turn["networkAccessEnabled"] = false;
        await sandbox.Store.SaveTurnAsync(turn, TestContext.CancellationToken);
        var launcher = new AiCoreTools(sandbox.Paths);
        launcher.Load(turn);
        var executor = new AiToolExecutor(sandbox.Paths, turn, launcher);
        var arguments = new JsonObject
        {
            ["scopeId"] = scope.ScopeId,
            ["expectedRevision"] = 0L,
            ["edits"] = JsonNode.Parse("""[{"tool":"Layer","layerChange":{"operation":"Add","id":"design","name":"Head"}},{"tool":"Pencil","paintLayerId":"design","surface":{"part":"Head","layer":"Base","face":"Front"},"color":"#FF0000"}]""")
        };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var confirmation = false;
        Task Emit(string kind, string? invocation, JsonObject detail)
        {
            if (kind == "confirmation_required")
            {
                confirmation = true;
                stop.Cancel();
            }

            return Task.CompletedTask;
        }

        Task<JsonObject> Invoke() => executor.InvokeAsync("skin_propose", arguments, Guid.NewGuid().ToString("N"), "paint", Emit, stop.Token);
        if (direct)
        {
            var result = await Invoke();
            Assert.IsFalse(confirmation);
            Assert.AreEqual("accepted", result["data"]!["state"]!.ToString());
            Assert.AreEqual("#FF0000FF", SkinEditor.Sample(skins.Read(id), new() { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front }, 0, 0, "design"));
            CollectionAssert.AreEqual(original.Pixels, skins.Read(id).Pixels);
        }
        else
        {
            await Assert.ThrowsAsync<OperationCanceledException>(Invoke);
            Assert.IsTrue(confirmation);
            Assert.IsEmpty(skins.Read(id).Layers);
            Assert.AreEqual(0L, skins.Read(id).Revision);
        }

        var saved = sandbox.Store.GetDocument(conversation["id"]!.ToString());
        Assert.AreEqual("safe", saved["mode"]!.ToString());
        Assert.IsNull(saved["operationGrantRevision"]);
        Assert.IsNull(saved["operationToolGrants"]);
        Assert.IsFalse(saved["fileAccessEnabled"]!.GetValue<bool>());
        Assert.IsFalse(saved["networkAccessEnabled"]!.GetValue<bool>());
    }

    /// <summary>Real conversation admission must persist its host grant and reject mixed instance context or a revoked grant before creating another chat.</summary>
    [TestMethod]
    public async Task SkinConversation_AdmissionBindsGrantAndRejectsInvalidContext()
    {
        using var sandbox = new AiSandbox();
        using var model = await sandbox.SnapshotAsync(TestContext.CancellationToken); // Offline profile only; no provider request.
        var skins = new SkinService(sandbox.Paths);
        var id = await skins.CreateAsync(SkinModel.Classic, cancellationToken: TestContext.CancellationToken);
        var scope = await skins.Proposals.CreateScopeAsync(id, new(), TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        AiChatCreate create = new() { ModelId = model.Model!.Id, SkinScopeId = scope.ScopeId };
        static JsonObject Input(AiChatCreate request) => JsonSerializer.SerializeToNode(request, AiChatJsonContext.Default.AiChatCreate)!.AsObject();
        var created = await session.InvokeAsync("chat.create", Input(create), TestContext.CancellationToken);
        Assert.AreEqual(scope.ScopeId, sandbox.Store.Get(created["id"]!.GetValue<string>()).SkinScopeId);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => session.InvokeAsync("chat.create", Input(create with { InstanceId = "unrelated" }), TestContext.CancellationToken));
        await skins.Proposals.RevokeScopeAsync(scope.ScopeId, TestContext.CancellationToken);
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => session.InvokeAsync("chat.create", Input(create), TestContext.CancellationToken));
        Assert.AreEqual(1, sandbox.Store.List().Total);
    }

    /// <summary>Generating a candidate must not edit the project, and repeated goals reuse its recorded identity even after the host accepts it.</summary>
    [TestMethod]
    public async Task CandidateTool_JournalReusesDraft_AndObservesHostAcceptanceWithoutReplay()
    {
        using var sandbox = new AiSandbox();
        var skins = new SkinService(sandbox.Paths);
        var id = await skins.CreateAsync(SkinModel.Slim, cancellationToken: TestContext.CancellationToken, size: 128);
        var scope = await skins.Proposals.CreateScopeAsync(id, new(), TestContext.CancellationToken);
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        conversation["skinScopeId"] = scope.ScopeId;
        await sandbox.Store.SaveConversationAsync(conversation, TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        turn["skinScopeId"] = scope.ScopeId;
        await sandbox.Store.SaveTurnAsync(turn, TestContext.CancellationToken);
        var dispatches = 0;
        async Task<JsonObject> Dispatch(string name, JsonObject input, CancellationToken ct, string? requestId)
        {
            dispatches++;
            Assert.AreEqual("skin_propose", name);
            var edits = input["edits"]!.Deserialize(SkinJsonContext.Default.SkinEditArray)!;
            var info = await skins.Proposals.ProposeAsync(input["scopeId"]!.GetValue<string>(), input["expectedRevision"]!.GetValue<long>(), edits, cancellationToken: ct);
            return new JsonObject { ["status"] = "succeeded", ["data"] = JsonSerializer.SerializeToNode(info, SkinsJsonContext.Default.SkinProposalInfo) };
        }

        var launcher = new AiCoreTools(sandbox.Paths);
        var executor = new AiToolExecutor(sandbox.Paths, turn, launcher, Dispatch);
        var arguments = new JsonObject
        {
            ["scopeId"] = scope.ScopeId,
            ["expectedRevision"] = 0L,
            ["edits"] = JsonNode.Parse("""[{"tool":"Pencil","surface":{"part":"Head","layer":"Base","face":"Front"},"color":"#FF0000"}]""")
        };
        Task Emit(string kind, string? invocation, JsonObject detail) => Task.CompletedTask;
        var first = await executor.InvokeAsync("skin_propose", arguments.DeepClone().AsObject(), Guid.NewGuid().ToString("N"), "first", Emit, TestContext.CancellationToken);
        var candidate = first["data"]!["proposalId"]!.GetValue<string>();
        Assert.AreEqual(0L, skins.Read(id).Revision);
        Assert.AreEqual("pending", first["verification"]!["candidate"]!["state"]!.GetValue<string>());
        await skins.Proposals.AcceptAsync(candidate, 0, TestContext.CancellationToken);
        var reused = await executor.InvokeAsync("skin_propose", arguments.DeepClone().AsObject(), Guid.NewGuid().ToString("N"), "repeat", Emit, TestContext.CancellationToken);
        Assert.AreEqual(1, dispatches);
        Assert.IsFalse(reused["mutationReplayed"]!.GetValue<bool>());
        Assert.AreEqual("accepted", reused["verification"]!["candidate"]!["state"]!.GetValue<string>());
        Assert.AreEqual(1L, skins.Read(id).Revision);
    }

    /// <summary>Bound conversations cannot address another document, bypass host acceptance, use execute, or inherit operation permission in a read-only turn.</summary>
    [TestMethod]
    [DataRow("other-document", false)]
    [DataRow("accept", false)]
    [DataRow("execute", false)]
    [DataRow("read-only", false)]
    [DataRow("revoked", false)]
    [DataRow("other-document", true)]
    [DataRow("accept", true)]
    [DataRow("execute", true)]
    [DataRow("read-only", true)]
    [DataRow("revoked", true)]
    public async Task SkinScope_ForbiddenDispatchPreservesProject(string restriction, bool direct)
    {
        using var sandbox = new AiSandbox();
        var skins = new SkinService(sandbox.Paths);
        var id = await skins.CreateAsync(SkinModel.Classic, cancellationToken: TestContext.CancellationToken);
        var scope = await skins.Proposals.CreateScopeAsync(id, new(DirectEditing: direct), TestContext.CancellationToken);
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        if (direct)
            conversation["mode"] = "safe";
        conversation["skinScopeId"] = scope.ScopeId;
        await sandbox.Store.SaveConversationAsync(conversation, TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, restriction != "read-only", TestContext.CancellationToken);
        if (direct && restriction != "read-only")
            turn["mode"] = "safe";
        turn["skinScopeId"] = scope.ScopeId;
        await sandbox.Store.SaveTurnAsync(turn, TestContext.CancellationToken);
        if (restriction == "revoked")
            await skins.Proposals.RevokeScopeAsync(scope.ScopeId, TestContext.CancellationToken);
        var dispatched = false;
        var launcher = new AiCoreTools(sandbox.Paths);
        var executor = new AiToolExecutor(sandbox.Paths, turn, launcher, (_, _, _, _) =>
        {
            dispatched = true;
            return Task.FromResult(new JsonObject { ["status"] = "succeeded" });
        });
        var name = restriction switch { "accept" => "skin_proposal_accept", "execute" => "execute", "other-document" => "skin_inspect", _ => "skin_propose" };
        var arguments = restriction == "other-document" ? new JsonObject { ["documentId"] = Guid.NewGuid().ToString("N") }
            : new JsonObject { ["scopeId"] = scope.ScopeId };
        Task Invoke() => executor.InvokeAsync(name, arguments, Guid.NewGuid().ToString("N"), "forbidden",
            (_, _, _) => Task.CompletedTask, TestContext.CancellationToken);
        if (restriction is "other-document" or "revoked")
        {
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(Invoke);
        }
        else
        {
            var failure = await Assert.ThrowsExactlyAsync<LauncherOperationException>(Invoke);
            Assert.AreEqual(restriction == "read-only" ? "AI_TOOL_READ_ONLY" : "AI_SKIN_SCOPE", failure.Code);
        }

        Assert.IsFalse(dispatched);
        Assert.AreEqual(0L, skins.Read(id).Revision);
        Assert.IsEmpty(skins.Read(id).Undo);
    }

    /// <summary>Gets or sets cancellation for stored grants and intents.</summary>
    public TestContext TestContext { get; set; }
}