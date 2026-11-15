using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects player-file bytes and scoped snapshots. Only the community MCP's staged result is substituted; Core commits and journals are real.</summary>
[TestClass]
public sealed class ManagedFileSafetyTests
{
    /// <summary>
    /// A staged exact edit must preserve encoding, BOM and CRLF, keep the original backup and record a verified durable effect.
    /// An external edit before the next commit must be preserved rather than overwritten. This is Core transaction evidence, not native MCP acceptance.
    /// </summary>
    [TestMethod]
    [DataRow(1200)]
    [DataRow(1201)]
    [DataRow(65001)]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task EncodedEdit_CommitsExactBytesAndBackup_ThenRejectsSourceDrift(int codePage)
    {
        using var sandbox = new AiSandbox();
        var rootPath = Path.Combine(sandbox.Root, "selected");
        Directory.CreateDirectory(rootPath);
        var root = (await new AiExtensionStore(sandbox.Paths).SaveRootAsync(new AiFileRootUpdate
        {
            Path = rootPath,
            Mode = "read_write",
            ExpectedRevision = 0
        }, TestContext.CancellationToken)).FileRoots![0]!.AsObject();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        var encoding = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        const string text = "Original \u4e2d\u6587\r\nmode=old\r\nKEEP-5821\r\n";
        byte[] originalBytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];
        var file = Path.Combine(rootPath, "settings.txt");
        await File.WriteAllBytesAsync(file, originalBytes, TestContext.CancellationToken);
        var snapshot = new AiFileSnapshot(sandbox.Paths, Path.Combine(sandbox.Root, "staging"), null);
        var original = snapshot.Read(rootPath, "settings.txt");
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(originalBytes)), original.Hash);
        var patch = new JsonObject { ["edits"] = new JsonArray(new JsonObject { ["oldText"] = "mode=old", ["newText"] = "mode=new" }) };
        var changed = AiManagedFileTools.ExactPatch(original.Text, patch, true);
        using var components = new AiManagedComponents();
        await using var tools = new AiManagedFileTools(sandbox.Paths, turn, components, null, (_, _, _) => Task.CompletedTask);
        var result = new JsonObject { ["status"] = "succeeded", ["fileState"] = new JsonObject() };
        await tools.CommitAsync("edit_file", patch, Guid.NewGuid().ToString("N"), "edit", new("fixture", "edit_file", "fixture", "fixture"),
            AiFileRootReader.FromDocument(root), "settings.txt", AiFileSnapshot.Encode(changed, original), original, new(), result, TestContext.CancellationToken);

        byte[] expected = [.. encoding.GetPreamble(), .. encoding.GetBytes(text.Replace("mode=old", "mode=new", StringComparison.Ordinal))];
        CollectionAssert.AreEqual(expected, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
        var backup = Path.Combine(rootPath, result["fileState"]!["backupPath"]!.ToString());
        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(backup, TestContext.CancellationToken));
        var reopened = sandbox.Store.GetTurnDocument(conversation["id"]!.ToString(), turn["id"]!.ToString(), true);
        Assert.AreEqual("result_recorded", reopened["intents"]![0]!["state"]!.ToString());
        Assert.IsTrue(reopened["intents"]![0]!["verification"]!["desiredStateObserved"]!.GetValue<bool>());

        var current = snapshot.Read(rootPath, "settings.txt");
        byte[] external = [.. encoding.GetPreamble(), .. encoding.GetBytes("Player's independent edit\r\n")];
        await File.WriteAllBytesAsync(file, external, TestContext.CancellationToken);
        var failure = await Assert.ThrowsAsync<LauncherOperationException>(async () =>
            await tools.CommitAsync("edit_file", patch, Guid.NewGuid().ToString("N"), "stale", new("fixture", "edit_file", "fixture", "fixture"),
                AiFileRootReader.FromDocument(root), "settings.txt", originalBytes, current, new(), result, TestContext.CancellationToken));
        Assert.AreEqual("AI_FILE_REVISION_CONFLICT", failure.Code);
        CollectionAssert.AreEqual(external, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
        Assert.HasCount(1, turn["intents"]!.AsArray());
    }

    /// <summary>Ambiguous, whitespace-fuzzy or mixed-newline edits must not silently normalize or replace unrelated player text.</summary>
    [TestMethod]
    [DataRow("a\r\nb\n", "a", "AI_FILE_MIXED_NEWLINES")]
    [DataRow("same\nsame\n", "same", "AI_FILE_PATCH_AMBIGUOUS")]
    [DataRow("    spaced\n", "spaced   ", "AI_FILE_PATCH_NO_EXACT_MATCH")]
    public void UnsafePatch_IsRejectedBeforePreparingAWrite(string original, string oldText, string code)
    {
        var failure = Assert.Throws<LauncherOperationException>(() => AiManagedFileTools.ExactPatch(original,
            new JsonObject { ["edits"] = new JsonArray(new JsonObject { ["oldText"] = oldText, ["newText"] = "changed" }) }, true));
        Assert.AreEqual(code, failure.Code);
    }

    /// <summary>
    /// A broad directory grant must still exclude credential paths and reject traversal. Invalid bytes must not be replacement-decoded into model input.
    /// A valid neighboring file remains usable, so a pass cannot come from disabling all access.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task DirectorySnapshot_ExcludesSecretsAndInvalidBytes_WhileKeepingOrdinaryFiles()
    {
        using var sandbox = new AiSandbox();
        var source = Path.Combine(sandbox.Root, "selected");
        var staging = Path.Combine(sandbox.Root, "staging");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "notes.txt"), "ordinary evidence", TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(source, ".env"), "PRIVATE-FIXTURE-MUST-NOT-STAGE", TestContext.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(source, "invalid.txt"), [0xff, 1], TestContext.CancellationToken);
        var snapshot = new AiFileSnapshot(sandbox.Paths, staging, null);
        Assert.Throws<ArgumentException>(() => snapshot.Target(source, "..\\outside.txt"));
        var blocked = Assert.Throws<LauncherOperationException>(() => snapshot.Read(source, ".env"));
        Assert.AreEqual("AI_FILE_PROTECTED", blocked.Code);
        snapshot.StageDirectory(source, string.Empty, true, true, TestContext.CancellationToken);
        Assert.AreEqual("ordinary evidence", await File.ReadAllTextAsync(Path.Combine(staging, "notes.txt"), TestContext.CancellationToken));
        Assert.IsFalse(File.Exists(Path.Combine(staging, ".env")));
        Assert.IsFalse(File.Exists(Path.Combine(staging, "invalid.txt")));
        Assert.Contains(item => item!["code"]?.ToString() == "AI_FILE_PROTECTED", snapshot.Warnings);
        Assert.Contains(item => item!["error"]?["code"]?.ToString() == "AI_FILE_ENCODING_UNSUPPORTED", snapshot.Warnings);
    }

    /// <summary>Value/design review: existing edits start with an existing parent. Creating a genuinely new nested file
    /// reproduces the reported fatal scoped-open failure; actual bytes and a verified durable intent distinguish success
    /// from a staged-only result. A file blocking the parent must remain intact and produce a recoverable unknown effect.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task NewNestedFile_CreatesPinnedParents_OrReportsSourceFailureWithoutOverwriting(bool blocked)
    {
        using var sandbox = new AiSandbox();
        var rootPath = Path.Combine(sandbox.Root, "selected");
        Directory.CreateDirectory(rootPath);
        var root = (await new AiExtensionStore(sandbox.Paths).SaveRootAsync(new AiFileRootUpdate
        {
            Path = rootPath,
            Mode = "read_write",
            ExpectedRevision = 0
        }, TestContext.CancellationToken)).FileRoots![0]!.AsObject();
        var turn = await sandbox.TurnAsync(await sandbox.ConversationAsync(TestContext.CancellationToken), true, TestContext.CancellationToken);
        if (blocked) await File.WriteAllTextAsync(Path.Combine(rootPath, "missing"), "preserve player file", TestContext.CancellationToken);
        using var components = new AiManagedComponents();
        await using var tools = new AiManagedFileTools(sandbox.Paths, turn, components, null, (_, _, _) => Task.CompletedTask);
        var result = new JsonObject { ["status"] = "succeeded", ["fileState"] = new JsonObject() };
        const string relative = "missing/nested/notes.txt";
        var commit = tools.CommitAsync("write_file", new JsonObject { ["expectedSha256"] = "absent" }, Guid.NewGuid().ToString("N"),
            "new", new("fixture", "write_file", "fixture", "fixture"), AiFileRootReader.FromDocument(root), relative, Encoding.UTF8.GetBytes("new source"), null, new(), result, TestContext.CancellationToken);
        if (blocked)
        {
            var failure = await Assert.ThrowsAsync<LauncherOperationException>(async () => await commit);
            Assert.AreEqual("AI_FILE_COMMIT_FAILED", failure.Code);
            Assert.AreEqual("preserve player file", await File.ReadAllTextAsync(Path.Combine(rootPath, "missing"), TestContext.CancellationToken));
            Assert.AreEqual("unknown_effect", turn["intents"]![0]!["state"]!.ToString());
        }
        else
        {
            await commit;
            Assert.AreEqual("new source", AiScopedFiles.ReadText(rootPath, relative));
            Assert.IsTrue(turn["intents"]![0]!["verification"]!["desiredStateObserved"]!.GetValue<bool>());
            Assert.IsTrue(turn["intents"]![0]!["fileTransaction"]!["createsParentDirectories"]!.GetValue<bool>());
        }
    }

    /// <summary>Gets or sets the deadline for isolated source writes and durable transaction checks.</summary>
    public TestContext TestContext { get; set; }
}