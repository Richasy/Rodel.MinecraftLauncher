using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.App.Features.Skins;
using Rodel.MinecraftLauncher.Core.Application.AI;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects grouped skin activity and superseded-attempt presentation without a native window or model account.</summary>
[TestClass]
public sealed class SkinActivityContentTests
{
    private static readonly string[] expected = ["read", "read-more"];

    /// <summary>
    /// Thinking separates two tool runs rather than becoming a nested step. Flattening must preserve every
    /// original ID and its order; adding a consecutive streamed call must keep the first run's expand-state key.
    /// Generic transcript projection tests do not exercise this skin-specific grouping boundary.
    /// </summary>
    [TestMethod]
    public void ConsecutiveTools_GroupTogether_AndThinkingSeparatesRunsWithoutChangingIdentity()
    {
        AiTranscriptPart[] parts = [
            new(AiTranscriptPartKind.Response, "before") { Text = "先查看帽子。" },
            new(AiTranscriptPartKind.Tool, "read") { Name = "skin_read_face", Complete = true },
            new(AiTranscriptPartKind.Tool, "read-more") { Name = "skin_read_face" },
            new(AiTranscriptPartKind.Thinking, "reason") { Text = "保留原图层，在帽子新层调整。" },
            new(AiTranscriptPartKind.Tool, "paint") { Name = "skin_propose" },
            new(AiTranscriptPartKind.Response, "after") { Text = "帽子已调整。" },
            new(AiTranscriptPartKind.Tool, "verify") { Name = "skin_proposal_preview" },
        ];
        var first = SkinAiActivity.Group(parts[..2]).Last();
        var grouped = SkinAiActivity.Group(parts);
        Assert.HasCount(6, grouped);
        Assert.AreEqual(first.Id, grouped[1].Id);
        CollectionAssert.AreEqual(expected, grouped[1].Entries.Select(part => part.Id).ToArray());
        Assert.AreSame(parts[3], grouped[2].Content, "Thinking must remain a separate disclosure between tool runs.");
        Assert.IsTrue(grouped.Where(block => block.Content is null).All(block => block.Entries.All(part => part.Kind == AiTranscriptPartKind.Tool)));
        CollectionAssert.AreEqual(parts.Select(part => part.Id).ToArray(),
            grouped.SelectMany(block => block.Content is { } content ? [content] : block.Entries).Select(part => part.Id).ToArray());
        Assert.IsFalse(grouped[1].Entries[^1].Complete, "A streamed invocation must remain visibly active.");
    }

    /// <summary>
    /// Step descriptions must address the actual recorded face/layer and typed drawing actions, not generic
    /// read/edit labels or a model's invented success prose. Malformed details remain an explicit read error.
    /// </summary>
    [TestMethod]
    public void StepDetails_RetainExactReadAndDrawTargets_AndReportUnreadableData()
    {
        var read = new AiTranscriptPart(AiTranscriptPartKind.Tool, "read")
        {
            Name = "skin_read_face",
            Arguments = """{"documentId":"skin","part":"LeftArm","layer":"Outer","face":"Back","paintLayerId":"sleeve"}"""
        };
        var draw = new AiTranscriptPart(AiTranscriptPartKind.Tool, "draw")
        {
            Name = "skin_propose",
            Arguments = """{"label":"帽徽","edits":[{"tool":"Rectangle","paintLayerId":"hat","surface":{"part":"Head","layer":"Outer","face":"Front"},"x":1,"y":2,"x2":3,"y2":4,"color":"#C9A227","filled":true}]}""",
            Result = """{"status":"succeeded","data":{"proposalId":"candidate","label":"帽徽"}}"""
        };
        var inspected = SkinAiActivity.Read(read, [read, draw]);
        Assert.IsNull(inspected.Error);
        Assert.AreEqual(new SkinSurface { Part = SkinPart.LeftArm, Layer = SkinLayer.Outer, Face = SkinFace.Back }, inspected.Surface);
        Assert.AreEqual("sleeve", inspected.PaintLayerId);
        var drawn = SkinAiActivity.Read(draw, [read, draw]);
        Assert.IsNull(drawn.Error);
        Assert.AreEqual("帽徽", drawn.Label);
        var action = drawn.Edits.Single();
        Assert.AreEqual("hat", action.PaintLayerId);
        Assert.AreEqual(SkinFace.Front, action.Surface!.Face);
        Assert.AreEqual(1, action.X);
        Assert.AreEqual(2, action.Y);
        Assert.AreEqual(3, action.X2);
        Assert.AreEqual(4, action.Y2);
        Assert.AreEqual("#C9A227", action.Color);
        var preview = new AiTranscriptPart(AiTranscriptPartKind.Tool, "preview") { Name = "skin_proposal_preview", Arguments = """{"proposalId":"candidate"}""" };
        Assert.AreEqual("帽徽", SkinAiActivity.Read(preview, [read, draw]).Label);
        var broken = new AiTranscriptPart(AiTranscriptPartKind.Tool, "broken") { Name = "skin_propose", Arguments = """{"edits":[{"tool":"NotARealDrawingAction"}]}""" };
        Assert.IsNotNull(SkinAiActivity.Read(broken, []).Error);
    }

    /// <summary>An old failed attempt must stop presenting retry/warning prompts after either an active or completed explicit continuation.</summary>
    [TestMethod]
    public void ContinuedAttempts_AreSuperseded_WithoutTreatingUnrelatedMessagesAsRetries()
    {
        var original = new AiChatRecord { Id = "a", GoalId = "a", Status = "incomplete" };
        var retry = new AiChatRecord { Id = "b", GoalId = "a", RetryOf = "a", Status = "generating" };
        var completed = new AiChatRecord { Id = "c", GoalId = "a", RetryOf = "b", Status = "completed" };
        var unrelated = new AiChatRecord { Id = "d", GoalId = "d", Status = "completed" };
        Assert.IsTrue(SkinAiActivity.HasContinuation(original, [original, retry]));
        Assert.IsTrue(SkinAiActivity.HasContinuation(original, [original, retry, completed]));
        Assert.IsTrue(SkinAiActivity.HasContinuation(retry, [original, retry, completed]));
        Assert.IsFalse(SkinAiActivity.HasContinuation(completed, [original, retry, completed, unrelated]));
        Assert.IsFalse(SkinAiActivity.HasContinuation(original, [original, unrelated]));
    }
}