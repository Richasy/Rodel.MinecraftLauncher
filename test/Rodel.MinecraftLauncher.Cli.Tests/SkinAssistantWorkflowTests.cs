// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Crosses the actual MEAI/provider/Core tool boundary for editor candidates; only the model response is scripted.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class SkinAssistantWorkflowTests
{
    /// <summary>
    /// Unlike the dispatch-substituted authorization tests, this protects the real catalog and prompt wiring:
    /// the model discovers its scoped Core tools and reads the editing skill before staging. The host-selected mode, not model prose,
    /// determines whether those pixels remain pending or are committed immediately as one undoable change.
    /// A subsequent preview must reach the next actual HTTP request as PNG bytes, not merely a saved path;
    /// this catches the former MCP non-text discard and the independent tool-message image projection loss.
    /// The direct variant uses the editor's safe conversation with no broad operation grant, which must never
    /// wait for an approval that the focused editor intentionally does not expose.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task ScopedGeneration_RealAgentAndMcp_UsesHostSelectedCommitMode(bool direct)
    {
        await using var session = await AssistantCliSession.StartAsync();
        var skins = new SkinService(new LauncherPaths(session.Cli.Root));
        var id = await skins.CreateAsync(SkinModel.Slim, size: 128, template: "alex", cancellationToken: TestContext.CancellationToken);
        await skins.EditAsync(id, 0,
            [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "drawing", Name = "Drawing" } }],
            TestContext.CancellationToken);
        var mask = new bool[128 * 128];
        mask[(16 * 128) + 16] = true; // Independent HD head-front origin, not the production layout helper.
        var scope = await skins.Proposals.CreateScopeAsync(id, direct ? new(DirectEditing: true) : new("drawing", PixelMask: mask), TestContext.CancellationToken);
        var calls = 0;
        JsonObject? firstWire = null;
        JsonObject? imageWire = null;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            var request = await ModelEndpoint.ReadRequestAsync(context);
            var step = Interlocked.Increment(ref calls) - 2;
            if (step < 1)
            {
                JsonObject args = step == -1 ? new() { ["names"] = new JsonArray("skin_propose", "skin_proposal_preview", "conversation_context_read") }
                    : new() { ["id"] = "builtin:skin-editing" };
                await ModelEndpoint.SseAsync(context, [new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["finish_reason"] = "tool_calls", ["delta"] = new JsonObject { ["tool_calls"] = new JsonArray(new JsonObject { ["index"] = 0, ["id"] = "load-" + calls, ["type"] = "function", ["function"] = new JsonObject { ["name"] = step == -1 ? "tools_discover" : "skill_read", ["arguments"] = args.ToJsonString() } }) } }) }.ToJsonString(), "[DONE]"]);
                return;
            }

            if (step == 1)
            {
                firstWire = request;
                var arguments = new JsonObject
                {
                    ["scopeId"] = scope.ScopeId,
                    ["expectedRevision"] = 1,
                    ["edits"] = JsonNode.Parse("""[{"tool":"Pencil","paintLayerId":"drawing","surface":{"part":"Head","layer":"Base","face":"Front"},"x":0,"y":0,"color":"#FF0000"}]""")
                };
                var delta = new JsonObject
                {
                    ["choices"] = new JsonArray(new JsonObject
                    {
                        ["index"] = 0,
                        ["delta"] = new JsonObject
                        {
                            ["tool_calls"] = new JsonArray(new JsonObject
                            {
                                ["index"] = 0,
                                ["id"] = "skin-preview",
                                ["type"] = "function",
                                ["function"] = new JsonObject { ["name"] = "skin_propose", ["arguments"] = arguments.ToJsonString() }
                            })
                        },
                        ["finish_reason"] = "tool_calls"
                    })
                };
                await ModelEndpoint.SseAsync(context, [delta.ToJsonString(), "[DONE]"]);
            }
            else if (step == 2)
            {
                var result = JsonNode.Parse(request["messages"]!.AsArray().Last(message => message!["role"]!.ToString() == "tool")!["content"]!.ToString())!;
                await ModelEndpoint.SseAsync(context,
                [
                    new JsonObject
                    {
                        ["choices"] = new JsonArray(new JsonObject
                        {
                            ["index"] = 0,
                            ["delta"] = new JsonObject
                            {
                                ["tool_calls"] = new JsonArray(new JsonObject
                                {
                                    ["index"] = 0, ["id"] = "skin-image-preview", ["type"] = "function",
                                    ["function"] = new JsonObject
                                    {
                                        ["name"] = "skin_proposal_preview",
                                        ["arguments"] = new JsonObject { ["proposalId"] = result["data"]!["proposalId"]!.DeepClone() }.ToJsonString()
                                    }
                                })
                            },
                            ["finish_reason"] = "tool_calls"
                        })
                    }.ToJsonString(),
                    "[DONE]"
                ]);
            }
            else
            {
                imageWire = request;
                await ModelEndpoint.SseAsync(context,
                ["""{"choices":[{"index":0,"delta":{"content":"Preview is ready for host review."},"finish_reason":"stop"}]}""", "[DONE]"]);
            }
        }, TestContext.CancellationToken);
        await session.ConfigureAsync(endpoint.Endpoint);
        var conversation = (await session.Cli.RunAsync("ai", "chat", "create", "--skin-scope", scope.ScopeId)).ReadEnvelope(0)["data"]!["id"]!.ToString();
        if (direct)
            (await session.Cli.RunAsync("ai", "chat", "update", conversation, "--expected-revision", "1", "--mode", "safe")).ReadEnvelope(0);
        var turn = (await session.Cli.RunAsync(["ai", "chat", "send", conversation, "--expected-revision", direct ? "2" : "1",
            .. direct ? Array.Empty<string>() : ["--allow-operations"], "--max-turns", "5", "--prompt", "Make the authorized texel red; propose only.", "--wait"])).ReadEnvelope(0)["data"]!;
        Assert.AreEqual("completed", turn["status"]!.ToString(), turn.ToJsonString());
        Assert.AreEqual(5, calls);
        Assert.IsNotNull(firstWire);
        var system = firstWire["messages"]![0]!["content"]!.ToString();
        Assert.DoesNotContain("pixelMask", system);
        Assert.DoesNotContain("paintLayers", system);
        Assert.Contains(scope.ScopeId, system);
        Assert.IsNotNull(imageWire);
        var previewResult = JsonNode.Parse(imageWire["messages"]!.AsArray().Last(message => message!["role"]!.ToString() == "tool")!["content"]!.ToString())!;
        var image = imageWire["messages"]!.AsArray().Where(message => message!["role"]!.ToString() == "user")
            .SelectMany(message => message!["content"] is JsonArray blocks ? blocks : [])
            .Single(block => block?["type"]?.ToString() == "image_url")!["image_url"]!["url"]!.ToString();
        Assert.StartsWith("data:image/png;base64,", image);
        CollectionAssert.AreEqual(await File.ReadAllBytesAsync(previewResult["data"]!["path"]!.ToString(), TestContext.CancellationToken),
            Convert.FromBase64String(image["data:image/png;base64,".Length..]));
        var events = (await session.Cli.RunAsync("ai", "chat", "turn", "events", conversation, turn["id"]!.ToString())).ReadEnvelope(0)["data"]!["items"]!.AsArray();
        Assert.DoesNotContain(item => item?["kind"]?.ToString() == "confirmation_required", events);
        Assert.Contains(item => item is JsonObject entry && entry["kind"]?.ToString() == "skill_finished"
            && entry["data"]?["id"]?.ToString() == "builtin:skin-editing", events);
        var previewEvent = events.Single(item => item?["kind"]?.ToString() == "tool_finished"
            && item["data"]?["name"]?.ToString() == "skin_proposal_preview")!["data"]!["result"]!["imageContent"]![0]!;
        Assert.AreEqual("image/png", previewEvent["mediaType"]!.ToString());
        Assert.IsNull(previewEvent["data"], "Chat events retain image metadata without dumping the binary image body.");
        var publicImage = turn["intents"]!.AsArray().Single(intent => intent?["name"]?.ToString() == "skin_proposal_preview")!["result"]!["imageContent"]![0]!;
        Assert.IsNull(publicImage["data"], "Turn observation must not repeatedly transport private image bodies.");
        var privateTurn = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(session.Cli.Root, "ai", "conversations",
            conversation, "turns", turn["id"]!.ToString() + ".json"), TestContext.CancellationToken))!;
        Assert.IsNotNull(privateTurn["intents"]!.AsArray().Single(intent => intent?["name"]?.ToString() == "skin_proposal_preview")!["result"]!["imageContent"]![0]!["data"],
            "Durable intent retains the original bytes for no-replay recovery.");
        var tools = firstWire["tools"]!.AsArray().Select(tool => tool!["function"]!["name"]!.ToString()).ToArray();
        CollectionAssert.Contains(tools, "skin_propose");
        // The focused editor must never inherit general chat questions or pause for ask_user.
        CollectionAssert.DoesNotContain(tools, "ask_user");
        Assert.DoesNotContain("用户交互规则", system);
        CollectionAssert.DoesNotContain(tools, "skin_edit");
        CollectionAssert.DoesNotContain(tools, "skin_proposal_accept");
        CollectionAssert.DoesNotContain(tools, "execute");
        var recovered = new SkinService(new LauncherPaths(session.Cli.Root));
        var proposalId = turn["intents"]!.AsArray().Single(intent => intent?["name"]?.ToString() == "skin_propose")!["skinProposalId"]!.ToString();
        var candidate = recovered.Proposals.Describe(proposalId);
        Assert.AreEqual(direct ? "accepted" : "pending", candidate.State);
        Assert.AreEqual(direct ? 2L : 1L, recovered.Read(id).Revision);
        Assert.AreEqual(direct ? 0xFF0000FFu : 0u, recovered.Read(id).Layers.Single().Pixels[(16 * 128) + 16]);
        Assert.AreEqual(0xFF0000FFu, recovered.Proposals.ReadContent(candidate.ProposalId).Layers.Single().Pixels[(16 * 128) + 16]);
        await recovered.Proposals.AcceptAsync(candidate.ProposalId, 1, TestContext.CancellationToken);
        Assert.AreEqual(0xFF0000FFu, recovered.Read(id).Layers.Single().Pixels[(16 * 128) + 16]);
        await recovered.RestoreAsync(id, 2, cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual(0u, recovered.Read(id).Layers.Single().Pixels[(16 * 128) + 16]);
        Assert.AreEqual("accepted", recovered.Proposals.Describe(proposalId).State);
    }

    /// <summary>Gets or sets cancellation for the owned endpoint and isolated launcher processes.</summary>
    public TestContext TestContext { get; set; }
}