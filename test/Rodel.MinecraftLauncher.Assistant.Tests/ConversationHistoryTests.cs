using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects public export and exact history deletion using real storage, independent of provider wire parsing.</summary>
[TestClass]
public sealed class ConversationHistoryTests
{
    /// <summary>
    /// Export must preserve public answers but exclude native protocol state, and never overwrite an existing destination implicitly.
    /// The source fixture includes opaque data so a pass cannot come from exporting an already-public-only record.
    /// The returned receipt must identify those exact published bytes; compare against the file rather than the serializer.
    /// Skipping private protocol values during history reads must still preserve unknown public nested data and numeric kinds.
    /// </summary>
    [TestMethod]
    [DataRow("json")]
    [DataRow("text")]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task PublicExport_ExcludesNativeState_AndRequiresExplicitOverwrite(string format)
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        turn["status"] = "completed";
        turn["prompt"] = "原始问题。";
        turn["answer"] = "Public answer MIGR-582.";
        turn["wireHistory"] = new JsonArray(new JsonObject { ["role"] = "assistant", ["native"] = new JsonObject { ["signature"] = "opaque-private-state" } });
        turn["providerState"] = new JsonObject { ["private"] = "opaque-private-state" };
        var future = new JsonObject { ["text"] = "Public future field.", ["values"] = new JsonArray(1.75, null, 1234567890123456789L) };
        turn["futurePublic"] = future.DeepClone();
        await sandbox.Store.SaveTurnAsync(turn, TestContext.CancellationToken);
        var visible = sandbox.Store.GetTurn(conversation["id"]!.ToString(), turn["id"]!.ToString());
        Assert.IsTrue(JsonNode.DeepEquals(future, JsonNode.Parse(visible.AdditionalData!["futurePublic"].GetRawText())));
        var destination = Path.Combine(sandbox.Root, "public-export." + format);
        await File.WriteAllTextAsync(destination, "Existing user document.", TestContext.CancellationToken);
        var request = new AiConversationExport { ConversationId = conversation["id"]!.ToString(), Destination = destination, Format = format };
        var history = new AiConversationHistory(sandbox.Paths);
        var failure = await Assert.ThrowsAsync<LauncherOperationException>(async () => await history.ExportAsync(request, TestContext.CancellationToken));
        Assert.AreEqual("AI_EXPORT_EXISTS", failure.Code);
        Assert.AreEqual("Existing user document.", await File.ReadAllTextAsync(destination, TestContext.CancellationToken));
        var exported = await history.ExportAsync(request with { Overwrite = true }, TestContext.CancellationToken);
        var bytes = await File.ReadAllBytesAsync(destination, TestContext.CancellationToken);
        Assert.AreEqual(destination, exported.Path);
        Assert.AreEqual(bytes.Length, exported.Bytes);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(bytes)), exported.Sha256);
        var text = await File.ReadAllTextAsync(destination, TestContext.CancellationToken);
        Assert.Contains("MIGR-582", text);
        Assert.DoesNotContain("opaque-private-state", text);
        Assert.DoesNotContain("wireHistory", text);
        Assert.DoesNotContain("providerState\"", text);
    }

    /// <summary>
    /// A new turn after deletion review must invalidate that exact review even without a conversation revision change.
    /// Fresh approval removes only that conversation, preserving another conversation's independent records.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task DeleteReview_NewTurnInvalidatesApproval_AndFreshReviewRemovesOnlyItsConversation()
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var other = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var history = new AiConversationHistory(sandbox.Paths);
        var id = conversation["id"]!.ToString();
        var review = history.ReviewRemoval(id);
        var turn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        turn["status"] = "completed";
        await sandbox.Store.SaveTurnAsync(turn, TestContext.CancellationToken);
        var stale = new AiConversationRemoval
        {
            ConversationId = id,
            ExpectedRevision = 1,
            Confirm = true,
            Fingerprint = review.Fingerprint
        };
        var rejected = await Assert.ThrowsAsync<LauncherOperationException>(async () => await history.RemoveAsync(stale, TestContext.CancellationToken));
        Assert.AreEqual("AI_HISTORY_REVIEW_CHANGED", rejected.Code);
        Assert.AreEqual(turn["id"]!.ToString(), sandbox.Store.GetTurn(id, turn["id"]!.ToString()).Id);
        var current = history.ReviewRemoval(id);
        await history.RemoveAsync(stale with { Fingerprint = current.Fingerprint }, TestContext.CancellationToken);
        Assert.IsFalse(Directory.Exists(Path.Combine(sandbox.Root, "ai", "conversations", id)));
        Assert.AreEqual(other["id"]!.ToString(), sandbox.Store.Get(other["id"]!.ToString()).Id);
    }

    /// <summary>Gets or sets the deadline for actual temporary-file writes and recovery.</summary>
    public TestContext TestContext { get; set; }
}