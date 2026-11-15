using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects persisted new-chat defaults without rewriting existing conversation permissions.</summary>
[TestClass]
public sealed class AccessDefaultsTests
{
    /// <summary>New installations start safe; explicit allow-all survives reopening and capability edits without rewriting existing chats.</summary>
    [TestMethod]
    public async Task Defaults_PersistMode_WithoutChangingExistingConversation()
    {
        using var sandbox = new AiSandbox();
        var store = new AiServiceStore(sandbox.Paths);
        var initial = await store.GetAsync(TestContext.CancellationToken);
        Assert.AreEqual("safe", initial.DefaultOperationMode);
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var id = conversation["id"]!.ToString();
        var before = sandbox.Store.GetDocument(id).ToJsonString();

        var saved = await store.SetAccessDefaultsAsync(null, null, initial.Revision, "allow_operations", TestContext.CancellationToken);
        var reopened = new AiServiceStore(sandbox.Paths);
        Assert.AreEqual("allow_operations", (await reopened.GetAsync(TestContext.CancellationToken)).DefaultOperationMode);
        saved = await reopened.SetAccessDefaultsAsync(false, null, saved.Revision, ct: TestContext.CancellationToken);
        Assert.AreEqual("allow_operations", saved.DefaultOperationMode);
        Assert.IsFalse(saved.FileAccessEnabled);
        Assert.AreEqual(before, sandbox.Store.GetDocument(id).ToJsonString());

        await reopened.SetAccessDefaultsAsync(null, null, saved.Revision, "safe", TestContext.CancellationToken);
        Assert.AreEqual("safe", (await store.GetAsync(TestContext.CancellationToken)).DefaultOperationMode);
    }

    /// <summary>Invalid or stale mode edits must reject the whole transaction, including accompanying capability changes.</summary>
    [TestMethod]
    public async Task Defaults_RejectInvalidAndStaleEdits_WithoutWriting()
    {
        using var sandbox = new AiSandbox();
        var store = new AiServiceStore(sandbox.Paths);
        var state = await store.SetAccessDefaultsAsync(null, null, 0, "safe", TestContext.CancellationToken);
        var file = Path.Combine(sandbox.Root, "ai", "services.json");
        var before = await File.ReadAllTextAsync(file, TestContext.CancellationToken);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.SetAccessDefaultsAsync(false, false, state.Revision, "invalid", TestContext.CancellationToken));
        await Assert.ThrowsExactlyAsync<LauncherOperationException>(() => store.SetAccessDefaultsAsync(false, false, state.Revision - 1, "allow_operations", TestContext.CancellationToken));
        Assert.AreEqual(before, await File.ReadAllTextAsync(file, TestContext.CancellationToken));
    }

    /// <summary>Gets or sets the runner cancellation context for all storage operations.</summary>
    public TestContext TestContext { get; set; }
}