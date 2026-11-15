// Copyright (c) Richasy. All rights reserved.
using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.Skins;
using Rodel.MinecraftLauncher.Core.Tools;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Rodel.MinecraftLauncher.Assistant.Tests;
/// <summary>Exercises actual Core tools and progressive disclosure through MEAI and HTTP; only model decisions are scripted.</summary>
[TestClass]
public sealed class ProgressiveToolWorkflowTests
{
    /// <summary>
    /// Author review: declaration-only tests miss stale MEAI invokers and a provider still using the old full catalog.
    /// A real config patch/readback with no CLI in this test output proves both invocation and subsequent model input.
    /// Reset must remove the prior schema while permission/effect guards still execute the mutation exactly once.
    /// </summary>
    [TestMethod]
    [Timeout(20000, CooperativeCancellation = true)]
    public async Task Discovery_InvokesCoreWithoutCli_AndResetsTheActualModelDefinitions()
    {
        using var sandbox = new AiSandbox();
        var requests = new List<JsonObject>();
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            requests.Add(await ModelEndpoint.ReadRequestAsync(context));
            var frames = requests.Count switch
            {
                1 => Call("discover-write", "tools_discover", """{"group":"launcher","names":["config_patch"],"limit":1}"""),
                2 => Call("patch", "config_patch", """{"changes":{"playerName":"CoreOnlyPlayer"}}"""),
                3 => Call("discover-read", "tools_discover", """{"group":"launcher","names":["config_get"],"reset":true,"limit":1}"""),
                4 => Call("read", "config_get", """{"key":"playerName"}"""),
                _ => Answer
            };
            await ModelEndpoint.SseAsync(context, frames);
        }, TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        var id = await ConfigureAsync(sandbox, session, endpoint.Endpoint);
        var conversation = sandbox.Store.GetDocument(id);
        conversation["mode"] = "allow_operations";
        await sandbox.Store.SaveConversationAsync(conversation, TestContext.CancellationToken);
        Assert.IsFalse(File.Exists(Path.Combine(AppContext.BaseDirectory, "rodel-mc.exe")), "This boundary must not silently gain an executable companion.");
        var receipt = await session.SendAsync(Request(id), TestContext.CancellationToken);
        var result = await session.FinishAsync(receipt, TestContext.CancellationToken);
        Assert.AreEqual("completed", result["status"]!.ToString(), result.ToJsonString());
        Assert.HasCount(5, requests);
        Assert.Contains("host pauses before writes", requests[0]["messages"]![0]!["content"]!.ToString());
        CollectionAssert.AreEquivalent(expected, Names(requests[0]));
        Assert.Contains("config_patch", Names(requests[1]));
        Assert.DoesNotContain("config_get", Names(requests[1]));
        Assert.Contains("config_get", Names(requests[3]));
        Assert.DoesNotContain("config_patch", Names(requests[3]));
        Assert.AreEqual("CoreOnlyPlayer", new Core.Configuration.SettingsService(sandbox.Paths).Get("playerName").Value?.ToString());
        var observed = requests[^1]["messages"]!.AsArray().Last()!["content"]!.ToString();
        Assert.Contains("CoreOnlyPlayer", observed);
        var events = await session.InvokeAsync("chat.events", ChatSession.Reference(receipt), TestContext.CancellationToken);
        Assert.ContainsSingle(item => item?["kind"]?.ToString() == "intent_result_recorded", events["items"]!.AsArray());
        Assert.DoesNotContain(item => item?["kind"]?.ToString() is "managed_mcp_started" or "mcp_started", events["items"]!.AsArray());
    }

    /// <summary>
    /// Author review: the skin grant and original layer inventory can be larger than the tools themselves. Observe
    /// the real first request and later explicit read, so removing a label while still serializing state cannot pass.
    /// The no-tools variant protects ordinary chat from skill bodies, capability indexes and bound operation state.
    /// Direct grants already allow layer creation despite a stored default false flag. Check the actual model wire
    /// against that effective capability; the candidate variant must retain its narrower layer restriction.
    /// The same wire must not contradict the skin contract with the generic safe-mode write-approval rule:
    /// execution guards alone cannot prevent the model from stopping to request unnecessary permission.
    /// </summary>
    [TestMethod]
    [DataRow(true, false)]
    [DataRow(true, true)]
    [DataRow(false, false)]
    [Timeout(20000, CooperativeCancellation = true)]
    public async Task BoundSkin_StateAndSkillsStayDeferred_UntilExplicitRead(bool useTools, bool direct)
    {
        using var sandbox = new AiSandbox();
        var skins = new SkinService(sandbox.Paths);
        var document = await skins.CreateAsync(SkinModel.Cape, cancellationToken: TestContext.CancellationToken);
        var scope = await skins.Proposals.CreateScopeAsync(document, new(DirectEditing: direct), TestContext.CancellationToken);
        // Reproduce an existing grant from the editor, whose layer flag defaulted to false even in direct mode.
        await File.WriteAllTextAsync(Path.Combine(sandbox.Paths.Root, "skins", "scopes", scope.ScopeId + ".json"),
            JsonSerializer.Serialize(scope with { Limits = scope.Limits with { AllowLayerChanges = false } }, SkinsJsonContext.Default.SkinScope), TestContext.CancellationToken);
        var requests = new List<JsonObject>();
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            requests.Add(await ModelEndpoint.ReadRequestAsync(context));
            var frames = !useTools ? Answer : requests.Count switch
            {
                1 => Call("discover-state", "tools_discover", """{"group":"conversation","names":["conversation_context_read"]}"""),
                2 => Call("state", "conversation_context_read", "{}"),
                _ => Answer
            };
            await ModelEndpoint.SseAsync(context, frames);
        }, TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        var id = await ConfigureAsync(sandbox, session, endpoint.Endpoint, scope.ScopeId);
        var conversation = sandbox.Store.GetDocument(id);
        conversation["mode"] = "safe";
        await sandbox.Store.SaveConversationAsync(conversation, TestContext.CancellationToken);
        var receipt = await session.SendAsync(Request(id) with { UseTools = useTools }, TestContext.CancellationToken);
        var result = await session.FinishAsync(receipt, TestContext.CancellationToken);
        Assert.AreEqual("completed", result["status"]!.ToString(), result.ToJsonString());
        var first = requests[0]["messages"]![0]!["content"]!.ToString();
        Assert.DoesNotContain("pixelMask", first);
        Assert.DoesNotContain("paintLayers", first);
        var skill = (await AiSkills.LoadAsync(sandbox.Paths, TestContext.CancellationToken)).Read("builtin:skin-editing");
        Assert.DoesNotContain(skill.Body!, first);
        if (useTools)
        {
            CollectionAssert.AreEquivalent(expected, Names(requests[0]));
            Assert.DoesNotContain("host pauses before writes", first);
            Assert.Contains(direct ? "no approval is needed" : "Only candidates are authorized", first);
            var read = JsonNode.Parse(requests[2]["messages"]!.AsArray().Last()!["content"]!.ToString())!;
            Assert.AreEqual(scope.ScopeId, read["data"]!["scope"]!["scopeId"]!.ToString());
            var limits = read["data"]!["scope"]!["limits"]!;
            Assert.AreEqual(direct, limits["directEditing"]!.GetValue<bool>());
            Assert.AreEqual(direct, limits["allowLayerChanges"]!.GetValue<bool>(), "Layer authority disclosed to the model must match the host's actual direct/candidate mode.");
            Assert.AreEqual(document, read["data"]!["originalSkin"]!["documentId"]!.ToString());
            Assert.IsNotNull(read["data"]!["originalSkin"]!["paintLayers"]);
        }
        else
        {
            Assert.HasCount(1, requests);
            Assert.IsEmpty(Names(requests[0]));
            Assert.DoesNotContain(scope.ScopeId, first);
            Assert.DoesNotContain("builtin:skin-editing", first);
        }

        Assert.AreEqual(0L, skins.Read(document).Revision);
    }

    /// <summary>
    /// Author review: replacing the CLI bridge must preserve its durable submission identity. Without forwarding
    /// that identity, a lost response followed by the same submission creates another job and another instance.
    /// This uses the real copy workflow and persistent jobs; unrelated task getter tests cannot detect this wiring.
    /// </summary>
    [TestMethod]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task CoreTaskTool_PreservesSubmissionIdentity_AndDoesNotCopyTwice()
    {
        using var sandbox = new AiSandbox();
        var client = new LauncherClient(sandbox.Paths);
        var source = await client.Instances.CreateAsync("Source", "26.3", TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(source.GameDirectory, "preserve.txt"), "source-marker", TestContext.CancellationToken);
        var service = new LauncherToolService(client);
        var invocation = Guid.NewGuid().ToString("N");
        JsonObject arguments = new() { ["instanceId"] = source.Id, ["name"] = "Copy" };
        var first = await service.InvokeAsync("instance_copy", arguments, TestContext.CancellationToken, invocation);
        Assert.AreEqual("succeeded", first["status"]!.ToString(), first.ToJsonString());
        var jobId = first["data"]!["id"]!.ToString();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        while (client.Jobs.Get(jobId).Status is "queued" or "running") await timer.WaitForNextTickAsync(TestContext.CancellationToken);
        Assert.AreEqual("succeeded", client.Jobs.Get(jobId).Status);
        Assert.AreEqual(invocation, client.Jobs.Get(jobId).ClientRequestId);
        var retry = await service.InvokeAsync("instance_copy", arguments, TestContext.CancellationToken, invocation);
        Assert.AreEqual(jobId, retry["data"]!["id"]!.ToString(), retry.ToJsonString());
        var copies = client.Instances.List().Where(instance => instance.Id != source.Id).ToArray();
        Assert.HasCount(1, copies);
        Assert.AreEqual("source-marker", await File.ReadAllTextAsync(Path.Combine(copies[0].GameDirectory, "preserve.txt"), TestContext.CancellationToken));
    }

    private async Task<string> ConfigureAsync(AiSandbox sandbox, ChatSession session, Uri endpoint, string? scope = null)
    {
        await new AiServiceStore(sandbox.Paths).SaveServiceAsync(new() { Protocol = "openai-chat", BaseUrl = endpoint.AbsoluteUri, Models = [new() { Model = "fixture" }] }, ct: TestContext.CancellationToken);
        var created = await session.InvokeAsync("chat.create", scope is null ? [] : new() { ["skinScopeId"] = scope }, TestContext.CancellationToken);
        return created["id"]!.ToString();
    }
    private static AiChatSend Request(string id) => new() { ConversationId = id, ExpectedRevision = 1, ClientKey = Guid.NewGuid().ToString("N"), Prompt = "Discuss this design, then perform only the explicitly requested operation.", MaximumModelCalls = 5 };
    private static string[] Names(JsonObject request) => request["tools"] is JsonArray tools ? [.. tools.Select(tool => tool!["function"]!["name"]!.ToString())] : [];
    private static string[] Call(string id, string name, string arguments) => [new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["finish_reason"] = "tool_calls", ["delta"] = new JsonObject
        {
            ["tool_calls"] = new JsonArray(new JsonObject { ["index"] = 0, ["id"] = id, ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["arguments"] = arguments } })
        } })
    }.ToJsonString(), "[DONE]"];
    private static readonly string[] Answer = ["""{"choices":[{"index":0,"delta":{"content":"Done."},"finish_reason":"stop"}]}""", "[DONE]"];
    /// <summary>Gets or sets the bounded Core/model cancellation context.</summary>
    public TestContext TestContext { get; set; }

    private static readonly string[] expected = ["tools_discover", "skill_read"];
}