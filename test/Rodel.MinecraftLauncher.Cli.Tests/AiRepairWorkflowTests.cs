using Microsoft.AspNetCore.Http;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Tests;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Crosses real CLI/Core/MEAI/MCP boundaries for incident grants; only model replies and the Java reporting worker are synthetic.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class AiRepairWorkflowTests
{
    /// <summary>Review: Core-only tests cannot detect an absent MCP mapping, a broad tool catalog or an unintended approval pause.
    /// One trial runs through the actual command tree, launch/session monitor and existing ask_user response, then restores its mod.
    /// The reporting worker exits normally without game initialization: its new run must remain inconclusive, never a claimed fix.
    /// The question event is the synchronization checkpoint; no timing guess establishes the awaiting-user boundary.</summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task Repair_RealToolsApplyVerifyAndAsk_WithoutPerStepApproval()
    {
        await using var session = await AssistantCliSession.StartAsync();
        var paths = new LauncherPaths(session.Cli.Root); var client = new LauncherClient(paths);
        var instance = await client.Instances.CreateAsync("Repair MCP fixture", "fixture", TestContext.CancellationToken);
        var java = new JavaProbeFixture(session.Cli.Root);
        await client.Settings.SetAsync("javaPath", JsonValue.Create(java.Executable), instance, TestContext.CancellationToken);
        var version = Path.Combine(instance.MinecraftDirectory, "versions", "fixture"); Directory.CreateDirectory(version);
        await File.WriteAllTextAsync(Path.Combine(version, "fixture.json"), """{"id":"fixture","mainClass":"fixture.Main","libraries":[],"minecraftArguments":""}""", TestContext.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(version, "fixture.jar"), [], TestContext.CancellationToken);
        var mods = Path.Combine(instance.GameDirectory, "mods"); Directory.CreateDirectory(mods);
        var archive = Path.Combine(mods, "fixture-mod.jar");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            await using var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open());
            await writer.WriteAsync("""{"schemaVersion":1,"id":"fixture_mod","version":"1.0","name":"Fixture mod"}""");
        }

        var origin = Guid.NewGuid().ToString("N"); var log = Path.Combine(paths.LogsDirectory, origin + ".log");
        // A real large retained log forces saved-result continuation, as a large modded instance can do.
        await File.WriteAllTextAsync(log, string.Join('\n', Enumerable.Repeat("[12:00:00] [main/INFO]: " + new string('x', 200), 450))
            + "\n[12:00:00] [main/FATAL]: MixinApplyError: fixture_mod could not apply mixin", TestContext.CancellationToken);
        await JsonFiles.WriteAsync(Path.Combine(paths.SessionsDirectory, origin + ".json"), new JsonObject
        {
            ["id"] = origin,
            ["instanceId"] = instance.Id,
            ["gameDirectory"] = instance.GameDirectory,
            ["status"] = "exited",
            ["exitCode"] = 1,
            ["stdoutLog"] = log
        }, TestContext.CancellationToken);
        var created = (await session.Cli.RunAsync("ai", "repair", "create", instance.Id, "--session", origin)).ReadEnvelope(0)["data"]!;
        var repairId = created["id"]!.ToString(); string? planId = null; JsonObject? verification = null; var calls = 0; var phase = 0; var continuationReads = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            var request = await ModelEndpoint.ReadRequestAsync(context);
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                await ToolAsync(context, call, "tools_discover", new() { ["names"] = new JsonArray("repair_show", "repair_plan", "repair_apply", "repair_verify", "repair_restore", "result_read", "ask_user"), ["limit"] = 8 });
                return;
            }

            if (call > 1 && LastResult(request)["data"] is JsonObject { } bounded && bounded["continuation"]?["tool"]?.ToString() == "result_read")
            {
                continuationReads++;
                await ToolAsync(context, call, "result_read", new() { ["resultId"] = bounded["resultId"]!.DeepClone(), ["pointer"] = "/data/originalDiagnosis/problem" });
                return;
            }

            if (continuationReads > 0) Assert.AreEqual("succeeded", LastResult(request)["status"]!.ToString());
            var args = new JsonObject { ["repairId"] = repairId };
            switch (++phase)
            {
                case 1:
                    var names = request["tools"]!.AsArray().Select(t => t!["function"]!["name"]!.ToString()).ToArray();
                    Assert.Contains("ask_user", names); Assert.Contains("repair_verify", names);
                    Assert.DoesNotContain("execute", names); Assert.DoesNotContain("config_patch", names); Assert.DoesNotContain("file_write", names);
                    Assert.Contains(repairId, request["messages"]![0]!["content"]!.ToString());
                    await ToolAsync(context, call, "repair_show", args); break;
                case 2:
                    args["kind"] = "disable_mod"; args["target"] = "fixture-mod.jar"; args["reason"] = "Observed MixinApplyError names fixture_mod";
                    await ToolAsync(context, call, "repair_plan", args); break;
                case 3:
                    planId = LastResult(request)["data"]!["id"]!.ToString();
                    args["planId"] = planId; await ToolAsync(context, call, "repair_apply", args); break;
                case 4:
                    Assert.IsTrue(File.Exists(archive + ".disabled"));
                    args["planId"] = planId; await ToolAsync(context, call, "repair_verify", args); break;
                case 5:
                    verification = LastResult(request)["data"]!.DeepClone().AsObject();
                    Assert.AreEqual("inconclusive", verification["outcome"]!.ToString(), verification.ToJsonString());
                    Assert.AreNotEqual(origin, verification["verificationSessionId"]!.ToString());
                    await ToolAsync(context, call, "ask_user", new() { ["question"] = "崩溃发生在进入世界时还是执行某个操作后？", ["options"] = new JsonArray(), ["multiple"] = false }); break;
                case 6:
                    Assert.AreEqual("进入世界时", LastResult(request)["data"]!["text"]!.ToString());
                    args["planId"] = planId; await ToolAsync(context, call, "repair_restore", args); break;
                default:
                    await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"delta":{"content":"已恢复试验改动；游戏内场景尚未验证。"},"finish_reason":"stop"}]}""", "[DONE]"]); break;
            }
        }, TestContext.CancellationToken);
        await session.ConfigureAsync(endpoint.Endpoint);
        var started = (await session.Cli.RunAsync("ai", "repair", "start", instance.Id, "--session", origin)).ReadEnvelope(0)["data"]!;
        var conversationId = started["conversationId"]!.ToString();
        var conversation = (await session.Cli.RunAsync("ai", "chat", "show", conversationId)).ReadEnvelope(0)["data"]!;
        var turnId = conversation["turns"]![0]!["id"]!.ToString();
        var question = await QuestionAsync(session, conversationId, turnId, TestContext.CancellationToken);
        var response = Path.Combine(session.Cli.Root, "answer.json");
        await File.WriteAllTextAsync(response, new JsonObject { ["conversationId"] = conversationId, ["turnId"] = turnId, ["promptId"] = question["id"]!.DeepClone(), ["text"] = "进入世界时" }.ToJsonString(), TestContext.CancellationToken);
        (await session.Cli.RunAsync("ai", "chat", "respond", "--file", response)).ReadEnvelope(0);
        var final = (await session.Cli.RunAsync("ai", "chat", "turn", "wait", conversationId, turnId)).ReadEnvelope(0)["data"]!;
        Assert.AreEqual("completed", final["status"]!.ToString(), final.ToJsonString());
        Assert.AreEqual(9, calls); Assert.AreEqual(1, continuationReads); Assert.IsNotNull(verification); Assert.IsTrue(File.Exists(archive)); Assert.IsFalse(File.Exists(archive + ".disabled"));
        var events = (await session.Cli.RunAsync("ai", "chat", "turn", "events", conversationId, turnId)).ReadEnvelope(0)["data"]!["items"]!.AsArray();
        Assert.DoesNotContain(e => e?["kind"]?.ToString() == "tool_confirmation_required", events);
        var reopened = (await session.Cli.RunAsync("ai", "repair", "start", instance.Id, "--session", origin)).ReadEnvelope(0)["data"]!;
        Assert.AreEqual(conversationId, reopened["conversationId"]!.ToString());
        Assert.HasCount(1, (await session.Cli.RunAsync("ai", "chat", "show", conversationId)).ReadEnvelope(0)["data"]!["turns"]!.AsArray());
    }

    private static JsonObject LastResult(JsonObject request) => JsonNode.Parse(request["messages"]!.AsArray().Last(m => m?["role"]?.ToString() == "tool")!["content"]!.ToString())!.AsObject();
    private static async Task<JsonObject> QuestionAsync(AssistantCliSession session, string conversationId, string turnId, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(75));
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var turn = (await session.Cli.RunAsync("ai", "chat", "turn", "show", conversationId, turnId)).ReadEnvelope(0)["data"]!;
            if (turn["status"]?.ToString() == "awaiting_user") return turn["pendingUserPrompt"]!.DeepClone().AsObject();
            Assert.IsFalse(Core.Application.AI.AiChatStates.IsTerminal(turn["status"]?.ToString()), turn.ToJsonString());
            await Task.Delay(100, deadline.Token);
        }
    }
    private static Task ToolAsync(HttpContext context, int call, string name, JsonObject args) => ModelEndpoint.SseAsync(context,
    [new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = new JsonObject { ["tool_calls"] = new JsonArray(new JsonObject { ["index"] = 0, ["id"] = "repair-" + call, ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["arguments"] = args.ToJsonString() } }) }, ["finish_reason"] = "tool_calls" }) }.ToJsonString(), "[DONE]"]);

    /// <summary>Gets cancellation for owned CLI processes and the loopback endpoint.</summary>
    public TestContext TestContext { get; set; } = null!;
}