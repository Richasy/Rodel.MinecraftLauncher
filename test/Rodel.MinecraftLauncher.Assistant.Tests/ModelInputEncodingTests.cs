using Microsoft.Extensions.AI;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects lossless machine-input encoding independently of human-readable persistence.</summary>
[TestClass]
public sealed class ModelInputEncodingTests
{
    /// <summary>Equivalent MCP JSON copies add token cost; distinct prose, values and the retained source must survive deduplication.</summary>
    [TestMethod]
    public void McpModelInput_RemovesOnlyEquivalentJsonText_PreservesExtraEvidenceAndSource()
    {
        var structured = new JsonObject { ["title"] = "实例", ["unknown"] = null, ["value"] = "  preserve\n" };
        var result = new JsonObject
        {
            ["status"] = "succeeded",
            ["data"] = new JsonObject
            {
                ["sourceId"] = "test",
                ["toolName"] = "read",
                ["structuredContent"] = structured,
                ["content"] = new JsonArray(
                    new JsonObject { ["type"] = "text", ["text"] = structured.ToJsonString(new() { WriteIndented = true }) },
                    new JsonObject { ["type"] = "text", ["text"] = "Additional warning: source is incomplete." },
                    new JsonObject { ["type"] = "text", ["text"] = "{\"distinct\":true}" })
            }
        };
        var original = result.DeepClone();
        var projected = JsonNode.Parse(AiChatMessages.ResultText(AiChatMessages.ToolResult("call", result)))!;
        Assert.AreEqual(2, projected["data"]!["content"]!.AsArray().Count);
        Assert.AreEqual("Additional warning: source is incomplete.", projected["data"]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.AreEqual("{\"distinct\":true}", projected["data"]!["content"]![1]!["text"]!.GetValue<string>());
        Assert.IsTrue(JsonNode.DeepEquals(structured, projected["data"]!["structuredContent"]));
        Assert.IsTrue(JsonNode.DeepEquals(original, result), "Model projection must not rewrite persistence or effect receipts.");
    }

    /// <summary>Compaction must preserve Unicode, nulls and significant whitespace inside strings, while reducing BPE input cost.</summary>
    [TestMethod]
    public void MachineJson_PreservesNestedValues_AndReducesTokenEstimate()
    {
        var value = new JsonObject { ["name"] = "实例：测试世界", ["description"] = "路径与技能说明\n保留  两个空格", ["data"] = new JsonArray(new JsonObject { ["script"] = "  x = 1\n  y = 2", ["unknown"] = null }) };
        var pretty = value.ToJsonString(new() { WriteIndented = true });
        var compact = AiModelJson.Text(value);
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(pretty), JsonNode.Parse(compact)));
        var context = new AiProviderContext("rodel-inference", 1, "binding", "model", null, null, null);
        var before = AiTokenCounter.Estimate(context, [new ChatMessage(ChatRole.System, pretty)], [], null);
        var after = AiTokenCounter.Estimate(context, [new ChatMessage(ChatRole.System, compact)], [], null);
        Assert.IsNotNull(before); Assert.IsNotNull(after); Assert.IsLessThan(before.Value, after.Value);
        Assert.IsTrue(JsonNode.DeepEquals(value, JsonNode.Parse(AiChatMessages.ResultText(AiChatMessages.ToolResult("call", value)))));
    }
}