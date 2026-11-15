// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Exercises the real Core skin storage adapter and filesystem; these cases do not claim to run in a NativeAOT child.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class SkinStorageWorkflowTests
{
    private static readonly SkinSurface Front = new() { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };

    /// <summary>Two independent clients blocked at a real document lease must not both commit the same observed revision.</summary>
    [TestMethod]
    public async Task CompetingClients_WithOneObservedRevision_CommitExactlyOneChange()
    {
        using var sandbox = new CliSandbox();
        var firstClient = new SkinService(new LauncherPaths(sandbox.Root));
        var secondClient = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await firstClient.CreateAsync(SkinModel.Classic, color: "#0000FF", cancellationToken: TestContext.CancellationToken);
        var path = Path.Combine(sandbox.Root, "skins", id + ".rmcs");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var lease = await ResourceLease.AcquireAsync(path, deadline.Token);
        // Both async methods execute up to their first incomplete await: acquisition of the lease held above.
        var first = firstClient.EditAsync(id, 0, [new() { Tool = SkinTool.Pencil, Surface = Front, Color = "#FF0000" }], deadline.Token);
        var second = secondClient.EditAsync(id, 0, [new() { Tool = SkinTool.Pencil, Surface = Front, Color = "#00FF00" }], deadline.Token);
        (SkinDocument? Document, InvalidOperationException? Conflict)[] results = [];
        try
        {
            Assert.IsFalse(first.IsCompleted, "The first writer must be waiting at the held resource boundary.");
            Assert.IsFalse(second.IsCompleted, "The second writer must also be in flight before release.");
            Assert.AreEqual(0L, firstClient.Read(id).Revision);
        }
        finally
        {
            lease.Dispose();
            results = await Task.WhenAll(ObserveAsync(first), ObserveAsync(second));
        }

        Assert.ContainsSingle(result => result.Document is not null, results);
        Assert.ContainsSingle(result => result.Conflict is not null, results);
        var persisted = firstClient.Read(id);
        Assert.AreEqual(1L, persisted.Revision);
        Assert.AreEqual(results[0].Document is not null ? "#FF0000FF" : "#00FF00FF", SkinEditor.Sample(persisted, Front, 0, 0));
        Assert.AreEqual("#0000FFFF", SkinEditor.Sample(persisted, Front, 1, 0));
    }

    /// <summary>A controlled target-file denial after temp creation must preserve the old project, remove the temp and permit a same-revision retry.</summary>
    [TestMethod]
    public async Task FailedPublication_AfterTemporaryFileCreation_PreservesProjectAndCanRetry()
    {
        using var sandbox = new CliSandbox();
        var client = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await client.CreateAsync(SkinModel.Slim, color: "#112233", cancellationToken: TestContext.CancellationToken);
        var directory = Path.Combine(sandbox.Root, "skins");
        var path = Path.Combine(directory, id + ".rmcs");
        var before = await File.ReadAllBytesAsync(path, TestContext.CancellationToken);
        var created = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(directory, id + ".rmcs.*.tmp") { NotifyFilter = NotifyFilters.FileName };
        watcher.Created += (_, _) => created.TrySetResult(true);
        watcher.Error += (_, error) => created.TrySetException(error.GetException());
        watcher.EnableRaisingEvents = true;
        var attributes = File.GetAttributes(path);
        File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
        SkinEdit[] edits = [new() { Tool = SkinTool.Pencil, Surface = Front, Color = "#FF0000" }];
        try
        {
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => client.EditAsync(id, 0, edits, TestContext.CancellationToken));
        }
        finally
        {
            File.SetAttributes(path, attributes);
        }

        await created.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path, TestContext.CancellationToken));
        Assert.IsEmpty(Directory.GetFiles(directory, id + ".rmcs.*.tmp"), "Failed publication must not leave an unowned transaction file.");
        Assert.AreEqual(0L, client.Read(id).Revision);
        var recovered = await client.EditAsync(id, 0, edits, TestContext.CancellationToken);
        Assert.AreEqual(1L, recovered.Revision);
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(client.Read(id), Front, 0, 0));
    }

    private static async Task<(SkinDocument? Document, InvalidOperationException? Conflict)> ObserveAsync(Task<SkinDocument> operation)
    {
        try { return (await operation, null); }
        catch (InvalidOperationException conflict) { return (null, conflict); }
    }

    /// <summary>Gets or sets the test context supplying cooperative cancellation.</summary>
    public TestContext TestContext { get; set; }
}