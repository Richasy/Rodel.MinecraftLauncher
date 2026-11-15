using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects typed App mutations from bypassing conversation file grants or exact user decisions.</summary>
[TestClass]
public sealed class AppAuthorizationTests
{
    /// <summary>
    /// Case-insensitive enum input must be normalized before capability checks; otherwise a valid lower-case import
    /// could reach the App despite file access being disabled. No App callback or confirmation may run in this case.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task LowerCaseImport_WithFilesDisabled_IsRejectedBeforeAppObservation()
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        turn["fileAccessEnabled"] = false;
        var calls = 0;
        var launcher = new AiCoreTools(sandbox.Paths);
        var executor = new AiToolExecutor(sandbox.Paths, turn, launcher, (_, _, _, _) =>
        {
            calls++;
            throw new AssertFailedException("An unauthorized App request reached transport.");
        });
        var error = await Assert.ThrowsAsync<LauncherOperationException>(async () => await executor.InvokeAsync("app_control",
            Arguments(Path.Combine(sandbox.Root, "image.png")), Guid.NewGuid().ToString("N"), "image",
            (_, _, _) => Task.CompletedTask, TestContext.CancellationToken));
        Assert.AreEqual("AI_FILE_ACCESS_DISABLED", error.Code);
        Assert.AreEqual(0, calls);
    }

    /// <summary>
    /// Exact approval is not a filesystem grant. An App image import outside registered roots must stop before mutation intent/dispatch,
    /// even though the real confirmation store approved its current App fingerprint.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task ApprovedImport_WithoutDirectoryGrant_DoesNotDispatchOrPrepareMutation()
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        conversation["mode"] = "safe";
        await sandbox.Store.SaveConversationAsync(conversation, TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        turn["mode"] = "safe";
        await sandbox.Store.SaveTurnAsync(turn, TestContext.CancellationToken);
        // Keep the source outside both built-in directories; data-root files are now authorized by default.
        using var sourceSandbox = new AiSandbox();
        var source = Path.Combine(sourceSandbox.Root, "image.png");
        await File.WriteAllBytesAsync(source, [1, 2, 3], TestContext.CancellationToken);
        var mutations = 0;
        var decisions = 0;
        var launcher = new AiCoreTools(sandbox.Paths);
        var executor = new AiToolExecutor(sandbox.Paths, turn, launcher, (name, _, _, _) =>
        {
            if (name == "app_show")
                return Task.FromResult(new JsonObject { ["status"] = "succeeded", ["data"] = new JsonObject { ["fingerprint"] = "current" } });
            mutations++;
            throw new AssertFailedException("Unscoped image import reached the App.");
        });
        async Task Approve(string kind, string? invocation, JsonObject detail)
        {
            if (kind != "confirmation_required") return;
            decisions++;
            await new AiToolApprovals(sandbox.Paths).DecideAsync(new AiToolConfirmation
            {
                ConversationId = conversation["id"]!.ToString(),
                TurnId = turn["id"]!.ToString(),
                InvocationId = invocation!,
                Fingerprint = detail["fingerprint"]!.ToString(),
                Approve = true
            }, TestContext.CancellationToken);
        }

        var error = await Assert.ThrowsAsync<LauncherOperationException>(async () => await executor.InvokeAsync("app_control",
            Arguments(source), Guid.NewGuid().ToString("N"), "image", Approve, TestContext.CancellationToken));
        Assert.AreEqual("AI_FILE_SCOPE_REQUIRED", error.Code);
        Assert.AreEqual(1, decisions);
        Assert.AreEqual(0, mutations);
        Assert.IsTrue(turn["intents"] is not JsonArray { Count: > 0 });
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(source, TestContext.CancellationToken));
    }

    private static JsonObject Arguments(string source) => new()
    {
        ["appId"] = Guid.NewGuid().ToString("N"),
        ["request"] = new JsonObject { ["action"] = "wallpaperimport", ["expectedFingerprint"] = "current", ["theme"] = "light", ["source"] = source }
    };

    /// <summary>Gets or sets the deadline for real persisted approvals and filesystem preservation checks.</summary>
    public TestContext TestContext { get; set; }
}