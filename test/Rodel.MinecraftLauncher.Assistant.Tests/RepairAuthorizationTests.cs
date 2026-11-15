using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects the dispatch boundary of a repair delegation, independently of what the provider's tool catalog advertises.</summary>
[TestClass]
public sealed class RepairAuthorizationTests
{
    /// <summary>Review: the incident grants add-only dependencies, while Content's actual field is change, not action.
    /// A replacement must be rejected even if unrelated metadata advertises action=add. The existing dispatch tests cannot
    /// detect this candidate-level authorization error. Exercise the production gate without downloads or player storage.</summary>
    [TestMethod]
    [DataRow("replace")]
    [DataRow("update")]
    public void DependencyRepair_RejectsCanonicalReplacementDespiteMisleadingExtraField(string change)
    {
        using var extra = JsonDocument.Parse("\"add\"");
        var candidate = new ContentPlanNode
        {
            Platform = "modrinth",
            ProjectId = "dependency",
            VersionId = "candidate",
            FileName = "dependency.jar",
            Kind = "mods",
            Upstream = [],
            Change = change,
            AdditionalData = new() { ["action"] = extra.RootElement.Clone() }
        };
        var failure = Assert.ThrowsExactly<LauncherOperationException>(() => AiRepairService.EnsureAddOnlyDependencies([candidate]));
        Assert.AreEqual("REPAIR_DEPENDENCIES_REPLACE", failure.Code);
        AiRepairService.EnsureAddOnlyDependencies([candidate with { Change = "add" }]);
    }

    /// <summary>Review: catalog filtering cannot prevent a fabricated provider call. The real executor must reject arbitrary settings,
    /// another incident, a revoked file grant and repair tools in an ordinary operation-authorized conversation before dispatch.
    /// A substituted dispatcher deliberately counts any escape; no provider, CLI or unrelated business effect is needed at this boundary.</summary>
    [TestMethod]
    public async Task RepairDelegation_ForgedOrRevokedCalls_NeverReachDispatcher()
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var repairId = Guid.NewGuid().ToString("N"); var instanceId = Guid.NewGuid().ToString("N");
        conversation["repairId"] = repairId; conversation["instanceId"] = instanceId;
        await sandbox.Store.SaveConversationAsync(conversation, TestContext.CancellationToken);
        await JsonFiles.WriteAsync(Path.Combine(sandbox.Root, "ai", "repairs", repairId + ".json"), new JsonObject
        { ["id"] = repairId, ["instanceId"] = instanceId, ["conversationId"] = conversation["id"]!.DeepClone() }, TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        turn["repairId"] = repairId; turn["instanceId"] = instanceId;
        var dispatches = 0;
        var launcher = new AiCoreTools(sandbox.Paths);
        var executor = new AiToolExecutor(sandbox.Paths, turn, launcher, (_, _, _, _) => { dispatches++; return Task.FromResult(new JsonObject()); });
        async Task Reject(string name, JsonObject args)
        {
            var error = await Assert.ThrowsExactlyAsync<LauncherOperationException>(() => executor.InvokeAsync(name, args, Guid.NewGuid().ToString("N"), "forged", (_, _, _) => Task.CompletedTask, TestContext.CancellationToken));
            Assert.AreEqual("AI_REPAIR_SCOPE", error.Code);
        }

        await Reject("config_patch", new() { ["instanceId"] = instanceId, ["changes"] = new JsonObject { ["maxMemoryMb"] = 99999 } });
        await Reject("repair_show", new() { ["repairId"] = Guid.NewGuid().ToString("N") });
        conversation["fileAccessEnabled"] = false; await sandbox.Store.SaveConversationAsync(conversation, TestContext.CancellationToken);
        var revoked = await Assert.ThrowsExactlyAsync<LauncherOperationException>(() => executor.InvokeAsync("repair_show", new() { ["repairId"] = repairId }, "revoked", "revoked", (_, _, _) => Task.CompletedTask, TestContext.CancellationToken));
        Assert.AreEqual("AI_FILE_ACCESS_DISABLED", revoked.Code);
        turn.Remove("repairId"); turn["mode"] = "allow_operations"; turn["turnAuthorization"] = "explicit_turn";
        await Reject("repair_apply", new() { ["repairId"] = repairId, ["planId"] = Guid.NewGuid().ToString("N") });
        Assert.AreEqual(0, dispatches);
    }

    /// <summary>Review: large mod inventories use saved-result continuations. Advertising result_read must not expose unrelated launcher
    /// results. A real dispatch guard records only the continuation returned by the current incident's tool; forged result IDs are rejected.
    /// The dispatcher substitutes output retention only, while the Core grant and durable incident record remain real.</summary>
    [TestMethod]
    public async Task RepairContinuation_OnlyHostReturnedResultIdsAreReadable()
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var repairId = Guid.NewGuid().ToString("N"); var owned = Guid.NewGuid().ToString("N");
        conversation["repairId"] = repairId; conversation["instanceId"] = "instance-fixture";
        await sandbox.Store.SaveConversationAsync(conversation, TestContext.CancellationToken);
        await JsonFiles.WriteAsync(Path.Combine(sandbox.Root, "ai", "repairs", repairId + ".json"), new JsonObject
        { ["id"] = repairId, ["instanceId"] = "instance-fixture", ["conversationId"] = conversation["id"]!.DeepClone() }, TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        turn["repairId"] = repairId; turn["instanceId"] = "instance-fixture";
        var dispatches = 0;
        var launcher = new AiCoreTools(sandbox.Paths);
        var executor = new AiToolExecutor(sandbox.Paths, turn, launcher, (name, _, _, _) =>
        {
            dispatches++;
            return Task.FromResult(new JsonObject
            {
                ["status"] = "succeeded",
                ["data"] = name == "repair_show"
                ? new JsonObject { ["resultId"] = owned, ["continuation"] = new JsonObject { ["tool"] = "result_read" } }
                : new JsonObject { ["evidence"] = "incident fixture" }
            });
        });
        await executor.InvokeAsync("repair_show", new() { ["repairId"] = repairId }, "show", "show", (_, _, _) => Task.CompletedTask, TestContext.CancellationToken);
        var read = await executor.InvokeAsync("result_read", new() { ["resultId"] = owned }, "read", "read", (_, _, _) => Task.CompletedTask, TestContext.CancellationToken);
        Assert.AreEqual("incident fixture", read["data"]!["evidence"]!.ToString());
        var error = await Assert.ThrowsExactlyAsync<LauncherOperationException>(() => executor.InvokeAsync("result_read", new() { ["resultId"] = Guid.NewGuid().ToString("N") }, "forged", "forged", (_, _, _) => Task.CompletedTask, TestContext.CancellationToken));
        Assert.AreEqual("AI_REPAIR_SCOPE", error.Code); Assert.AreEqual(2, dispatches);
    }

    /// <summary>Gets cancellation for this case's real temporary storage.</summary>
    public TestContext TestContext { get; set; } = null!;
}