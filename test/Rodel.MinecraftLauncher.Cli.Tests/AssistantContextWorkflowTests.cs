using Microsoft.AspNetCore.Http;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Crosses the actual CLI/Core host and NativeAOT metadata boundary for typed source selection.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class AssistantContextWorkflowTests
{
    /// <summary>
    /// A real provider context rejection must use bounded typed selection and one ordinary retry while preserving
    /// Chinese corrections. Selection packets share the explicit model budget rather than an assumed batch size.
    /// The published executable must retain schema/parser metadata; only the HTTP model is substituted.
    /// </summary>
    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task ContextRejection_UsesBudgetedTypedSelection_AndPreservesOriginalCorrections()
    {
        var ordinary = new List<JsonObject>();
        var selections = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            var body = await ModelEndpoint.ReadRequestAsync(context);
            var messages = body["messages"]!.AsArray();
            var excerpts = messages.Where(message => message!["content"]!.ToString().StartsWith("EXCERPT ", StringComparison.Ordinal)).ToArray();
            if (excerpts.Length > 0)
            {
                selections++;
                var packet = JsonNode.Parse(messages[1]!["content"]!.ToString())!;
                Assert.IsNotNull(packet["selectionSchema"]?["properties"]?["keep"]);
                Assert.IsNotNull(packet["selectionSchema"]?["properties"]?["omit"]);
                var omitted = excerpts.Select(message => JsonNode.Parse(
                    message!["content"]!.ToString().Split('\n')[0]["EXCERPT ".Length..])!["id"]!.DeepClone()).ToArray();
                await ReplyAsync(context, new JsonObject { ["keep"] = new JsonArray(), ["omit"] = new JsonArray(omitted) }.ToJsonString());
                return;
            }

            ordinary.Add(body);
            if (ordinary.Count == 1)
            {
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("""{"error":{"code":"context_length_exceeded","message":"Controlled context rejection."}}""", context.RequestAborted);
            }
            else
            {
                await ReplyAsync(context, "NATIVE-CONTEXT-OK");
            }
        }, TestContext.CancellationToken);
        await using var session = await AssistantCliSession.StartAsync();
        await session.ConfigureAsync(endpoint.Endpoint);
        var services = (await session.Cli.RunAsync("ai", "service", "list")).ReadEnvelope(0)["data"]!["services"]!;
        var service = services[0]!;
        var conversation = (await session.Cli.RunAsync("ai", "chat", "create")).ReadEnvelope(0)["data"]!["id"]!.ToString();
        const string correction = "纠正：Java 25，不是21；未授权联网，未执行变更。";
        var background = string.Concat(Enumerable.Repeat(new string('背', 180) + "。", 80));
        var turnId = Guid.NewGuid().ToString("N");
        var previous = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["id"] = turnId,
            ["conversationId"] = conversation,
            ["createdUtc"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"),
            ["status"] = "completed",
            ["mode"] = "read_only",
            ["modelId"] = service["models"]![0]!["id"]!.DeepClone(),
            ["service"] = new JsonObject { ["revision"] = service["revision"]!.DeepClone() },
            ["wireHistory"] = new JsonArray(
                new JsonObject { ["role"] = "user", ["text"] = "只做只读审阅，保留纠正。" },
                new JsonObject { ["role"] = "assistant", ["text"] = background },
                new JsonObject { ["role"] = "user", ["text"] = correction },
                new JsonObject { ["role"] = "assistant", ["text"] = "已知悉，未执行。" })
        };
        var turns = Path.Combine(session.Cli.Root, "ai", "conversations", conversation, "turns");
        Directory.CreateDirectory(turns);
        await File.WriteAllTextAsync(Path.Combine(turns, turnId + ".json"), previous.ToJsonString(), TestContext.CancellationToken);
        var result = (await session.Cli.RunAsync("ai", "chat", "send", conversation, "--expected-revision", "1",
            "--prompt", "继续只读审阅。", "--no-tools", "--max-turns", "4", "--wait")).ReadEnvelope(0)["data"]!;
        Assert.AreEqual("completed", result["status"]!.ToString());
        Assert.AreEqual("NATIVE-CONTEXT-OK", result["answer"]!.ToString());
        Assert.HasCount(2, ordinary);
        Assert.IsGreaterThan(0, selections);
        Assert.IsLessThanOrEqualTo(4, selections + ordinary.Count, "Selection and generation share the same four-call budget.");
        Assert.Contains(message => message!["content"]!.ToString().Contains(correction, StringComparison.Ordinal), ordinary[1]["messages"]!.AsArray());
        var archive = JsonNode.Parse(await File.ReadAllTextAsync(
            Directory.GetFiles(Path.Combine(session.Cli.Root, "ai", "conversations", conversation, "context-archives"), "*.json").Single(),
            TestContext.CancellationToken))!;
        Assert.Contains(message => message!["text"]?.ToString() == background, archive["messages"]!.AsArray());
        Assert.Contains(message => message!["text"]?.ToString() == correction, archive["messages"]!.AsArray());
    }

    private static Task ReplyAsync(HttpContext context, string text) => ModelEndpoint.SseAsync(context,
    [
        new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = new JsonObject { ["content"] = text }, ["finish_reason"] = "stop" })
        }.ToJsonString(),
        "[DONE]"
    ]);

    /// <summary>Gets or sets the deadline for isolated host and HTTP operations.</summary>
    public TestContext TestContext { get; set; }
}