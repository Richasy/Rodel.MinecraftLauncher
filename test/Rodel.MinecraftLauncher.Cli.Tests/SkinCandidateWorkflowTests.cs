// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Exercises real project/grant/candidate persistence and publication; MCP transport has a separate bound-host case.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class SkinCandidateWorkflowTests
{
    private static readonly SkinSurface Front = new() { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };

    /// <summary>Acceptance must publish a frozen new layer and receipt together; a lost response followed by undo must never replay the candidate.</summary>
    [TestMethod]
    public async Task AcceptedCandidate_ReopenAndRetryAfterUndo_DoNotReplayPixels()
    {
        using var sandbox = new CliSandbox();
        var skins = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await skins.CreateAsync(SkinModel.Slim, cancellationToken: TestContext.CancellationToken, size: 128, template: "alex", name: "Original");
        var scope = await skins.Proposals.CreateScopeAsync(id, new("ai", [Front], AllowLayerChanges: true), TestContext.CancellationToken);
        SkinEdit[] edits = [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "ai", Name = "Candidate" } },
            new() { Tool = SkinTool.Pencil, Surface = Front, PaintLayerId = "ai", X = 1, Y = 1, Color = "#FF0000" }];
        var candidate = await skins.Proposals.ProposeAsync(scope.ScopeId, 0, edits, "Recolor", TestContext.CancellationToken);
        edits[1] = edits[1] with { Color = "#00FF00" }; // Caller mutation after staging must not alter reviewed content.
        Assert.IsEmpty(skins.Read(id).Layers);
        Assert.AreEqual(0L, skins.Read(id).Revision);
        var receipt = await skins.Proposals.AcceptAsync(candidate.ProposalId, 0, TestContext.CancellationToken);
        Assert.AreEqual(1L, receipt.Revision);
        var other = new SkinService(new LauncherPaths(sandbox.Root));
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(other.Read(id), Front, 1, 1, "ai"));
        Assert.AreEqual(receipt, other.Proposals.Receipt(id, candidate.ProposalId));
        await other.RestoreAsync(id, 1, cancellationToken: TestContext.CancellationToken);
        var retried = await other.Proposals.AcceptAsync(candidate.ProposalId, 0, TestContext.CancellationToken);
        Assert.AreEqual(receipt, retried);
        Assert.AreEqual(2L, other.Read(id).Revision);
        Assert.IsEmpty(other.Read(id).Layers);
        Assert.AreEqual("accepted", other.Proposals.Describe(candidate.ProposalId).State);
        Assert.IsNotEmpty(other.Read(id).Redo);
    }

    /// <summary>Expanded pixel edits and document-wide transparency must respect a restricted layer grant, even at an allowed texel.</summary>
    [TestMethod]
    [DataRow("mirror")]
    [DataRow("fill")]
    [DataRow("metadata")]
    [DataRow("transparency")]
    public async Task ExpandedEffects_OutsideGrant_AreRejectedBeforeAnyProjectPublication(string operation)
    {
        using var sandbox = new CliSandbox();
        var skins = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await skins.CreateAsync(SkinModel.Classic, cancellationToken: TestContext.CancellationToken);
        await skins.EditAsync(id, 0,
            [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "paint" } },
             new() { Tool = SkinTool.Fill, Surface = Front, PaintLayerId = "paint", Color = "#FF0000", FillMode = SkinFillMode.Face }], TestContext.CancellationToken);
        var mask = new bool[4096];
        mask[(8 * 64) + 8] = true; // Explicit standard head-front origin, independent of production layout.
        var scope = await skins.Proposals.CreateScopeAsync(id, new("paint", PixelMask: mask, AllowLayerChanges: true), TestContext.CancellationToken);
        var before = await File.ReadAllBytesAsync(Path.Combine(sandbox.Root, "skins", id + ".rmcs"), TestContext.CancellationToken);
        SkinEdit edit = operation switch
        {
            "mirror" => new() { Tool = SkinTool.Pencil, Surface = Front, PaintLayerId = "paint", Color = "#00FF00", Mirror = SkinMirrorMode.Body },
            "fill" => new() { Tool = SkinTool.Fill, Surface = Front, PaintLayerId = "paint", Color = "#00FF00", FillMode = SkinFillMode.Layer },
            "transparency" => new() { Tool = SkinTool.Pencil, Surface = Front, TransparencyMask = true },
            _ => new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Set, Id = "paint", Visible = false } }
        };
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => skins.Proposals.ProposeAsync(scope.ScopeId, 1, [edit], cancellationToken: TestContext.CancellationToken));
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(Path.Combine(sandbox.Root, "skins", id + ".rmcs"), TestContext.CancellationToken));
        Assert.IsFalse(Directory.Exists(Path.Combine(sandbox.Root, "skins", "proposals")));
    }

    /// <summary>Review cannot bypass manual edits, live grant revocation or rejection; the check occurs at acceptance, not just at generation.</summary>
    [TestMethod]
    [DataRow("stale")]
    [DataRow("revoked")]
    [DataRow("rejected")]
    public async Task CandidateReview_StateChangesBlockAcceptanceWithoutOverwritingTheProject(string state)
    {
        using var sandbox = new CliSandbox();
        var skins = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await skins.CreateAsync(SkinModel.Classic, cancellationToken: TestContext.CancellationToken);
        var scope = await skins.Proposals.CreateScopeAsync(id, new(Surfaces: [Front]), TestContext.CancellationToken);
        var candidate = await skins.Proposals.ProposeAsync(scope.ScopeId, 0,
            [new() { Tool = SkinTool.Pencil, Surface = Front, Color = "#FF0000" }], cancellationToken: TestContext.CancellationToken);
        if (state == "stale")
            await skins.EditAsync(id, 0, [new() { Tool = SkinTool.Pencil, Surface = Front, Color = "#00FF00" }], TestContext.CancellationToken);
        else if (state == "revoked")
            await skins.Proposals.RevokeScopeAsync(scope.ScopeId, TestContext.CancellationToken);
        else
            await skins.Proposals.RejectAsync(candidate.ProposalId, TestContext.CancellationToken);
        var before = await File.ReadAllBytesAsync(Path.Combine(sandbox.Root, "skins", id + ".rmcs"), TestContext.CancellationToken);
        if (state == "revoked")
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => skins.Proposals.AcceptAsync(candidate.ProposalId, 0, TestContext.CancellationToken));
        else
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => skins.Proposals.AcceptAsync(candidate.ProposalId, 0, TestContext.CancellationToken));
        Assert.AreEqual(state, skins.Proposals.Describe(candidate.ProposalId).State);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(Path.Combine(sandbox.Root, "skins", id + ".rmcs"), TestContext.CancellationToken));
    }

    /// <summary>Read-only grants reject staging; a legitimate no-op acceptance has a receipt without inventing a history step or clearing redo.</summary>
    [TestMethod]
    public async Task ReadOnlyGrantAndNoOpCandidate_PreserveExistingHistory()
    {
        using var sandbox = new CliSandbox();
        var skins = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await skins.CreateAsync(SkinModel.Classic, color: "#000000", cancellationToken: TestContext.CancellationToken);
        var readOnly = await skins.Proposals.CreateScopeAsync(id, new(ReadOnly: true), TestContext.CancellationToken);
        SkinEdit[] edits = [new() { Tool = SkinTool.Pencil, Surface = Front, Color = "#000000" }];
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => skins.Proposals.ProposeAsync(readOnly.ScopeId, 0, edits, cancellationToken: TestContext.CancellationToken));
        await skins.EditAsync(id, 0, [edits[0] with { Color = "#FF0000" }], TestContext.CancellationToken);
        await skins.RestoreAsync(id, 1, cancellationToken: TestContext.CancellationToken);
        var scope = await skins.Proposals.CreateScopeAsync(id, new(), TestContext.CancellationToken);
        var candidate = await skins.Proposals.ProposeAsync(scope.ScopeId, 2, edits, cancellationToken: TestContext.CancellationToken);
        var receipt = await skins.Proposals.AcceptAsync(candidate.ProposalId, 2, TestContext.CancellationToken);
        Assert.IsFalse(receipt.Changed);
        Assert.AreEqual(2L, skins.Read(id).Revision);
        Assert.HasCount(1, skins.Read(id).Redo);
    }

    /// <summary>A failed project publication cannot leave a receipt that falsely claims acceptance; retry uses the unchanged base revision.</summary>
    [TestMethod]
    public async Task FailedAcceptance_PreservesProjectAndReceiptUntilSuccessfulRetry()
    {
        using var sandbox = new CliSandbox();
        var skins = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await skins.CreateAsync(SkinModel.Classic, cancellationToken: TestContext.CancellationToken);
        var scope = await skins.Proposals.CreateScopeAsync(id, new(), TestContext.CancellationToken);
        var candidate = await skins.Proposals.ProposeAsync(scope.ScopeId, 0,
            [new() { Tool = SkinTool.Pencil, Surface = Front, Color = "#FF0000" }], cancellationToken: TestContext.CancellationToken);
        var path = Path.Combine(sandbox.Root, "skins", id + ".rmcs");
        var original = await File.ReadAllBytesAsync(path, TestContext.CancellationToken);
        var attributes = File.GetAttributes(path);
        File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
        try
        {
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => skins.Proposals.AcceptAsync(candidate.ProposalId, 0, TestContext.CancellationToken));
        }
        finally { File.SetAttributes(path, attributes); }

        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path, TestContext.CancellationToken));
        Assert.IsNull(skins.Proposals.Receipt(id, candidate.ProposalId));
        Assert.AreEqual("pending", skins.Proposals.Describe(candidate.ProposalId).State);
        var receipt = await skins.Proposals.AcceptAsync(candidate.ProposalId, 0, TestContext.CancellationToken);
        Assert.AreEqual(1L, receipt.Revision);
        Assert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(path)!, id + ".rmcs.*.tmp"));
    }

    /// <summary>Two actual clients waiting at the same held lease must accept one candidate exactly once and both obtain the same durable receipt.</summary>
    [TestMethod]
    public async Task CompetingAccepts_OneHistoryStepAndIdenticalReceipts()
    {
        using var sandbox = new CliSandbox();
        var skins = new SkinService(new LauncherPaths(sandbox.Root));
        var other = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await skins.CreateAsync(SkinModel.Classic, cancellationToken: TestContext.CancellationToken);
        var scope = await skins.Proposals.CreateScopeAsync(id, new(), TestContext.CancellationToken);
        var candidate = await skins.Proposals.ProposeAsync(scope.ScopeId, 0,
            [new() { Tool = SkinTool.Pencil, Surface = Front, Color = "#FF0000" }], cancellationToken: TestContext.CancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var held = await ResourceLease.AcquireAsync(Path.Combine(sandbox.Root, "skins", id + ".rmcs"), deadline.Token);
        var first = skins.Proposals.AcceptAsync(candidate.ProposalId, 0, deadline.Token);
        var second = other.Proposals.AcceptAsync(candidate.ProposalId, 0, deadline.Token);
        SkinCommitReceipt[] receipts;
        try
        {
            Assert.IsFalse(first.IsCompleted);
            Assert.IsFalse(second.IsCompleted);
            Assert.AreEqual(0L, skins.Read(id).Revision);
        }
        finally
        {
            held.Dispose();
            receipts = await Task.WhenAll(first, second);
        }

        Assert.AreEqual(receipts[0], receipts[1]);
        Assert.AreEqual(1L, skins.Read(id).Revision);
        Assert.HasCount(1, skins.Read(id).Undo);
    }

    /// <summary>Admission freezes the host's mutable selection and nested point buffers before a real held lease; otherwise queued work can expand its grant or change reviewed pixels.</summary>
    [TestMethod]
    public async Task PendingGrantAndCandidate_FreezeCallerBuffersBeforeLeaseWait()
    {
        using var sandbox = new CliSandbox();
        var skins = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await skins.CreateAsync(SkinModel.Classic, cancellationToken: TestContext.CancellationToken);
        var mask = new bool[4096];
        mask[(8 * 64) + 8] = true;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var path = Path.Combine(sandbox.Root, "skins", id + ".rmcs");
        var held = await ResourceLease.AcquireAsync(path, deadline.Token);
        var pendingScope = skins.Proposals.CreateScopeAsync(id, new(PixelMask: mask), deadline.Token);
        SkinScope scope;
        try
        {
            Assert.IsFalse(pendingScope.IsCompleted);
            Array.Fill(mask, true);
        }
        finally
        {
            held.Dispose();
            scope = await pendingScope;
        }

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => skins.Proposals.ProposeAsync(scope.ScopeId, 0,
            [new() { Tool = SkinTool.Fill, Surface = Front, Color = "#FFFFFF", FillMode = SkinFillMode.Face }], cancellationToken: deadline.Token));
        SkinStrokePoint[] points = [new(0, 0)];
        SkinEdit[] edits = [new() { Tool = SkinTool.Stroke, Surface = Front, Points = points, Color = "#FF0000" }];
        held = await ResourceLease.AcquireAsync(path, deadline.Token);
        var pendingCandidate = skins.Proposals.ProposeAsync(scope.ScopeId, 0, edits, cancellationToken: deadline.Token);
        SkinProposalInfo candidate;
        try
        {
            Assert.IsFalse(pendingCandidate.IsCompleted);
            points[0] = new(1, 0);
            edits[0] = edits[0] with { Color = "#00FF00" };
        }
        finally
        {
            held.Dispose();
            candidate = await pendingCandidate;
        }

        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(skins.Proposals.ReadContent(candidate.ProposalId), Front, 0, 0));
        Assert.AreEqual("#808080FF", SkinEditor.Sample(skins.Proposals.ReadContent(candidate.ProposalId), Front, 1, 0));
        Assert.AreEqual(0L, skins.Read(id).Revision);
    }

    /// <summary>Direct host grants disclose and apply full layer authority without frontend approval, but cannot silently discard partial/read-only restrictions.</summary>
    [TestMethod]
    public async Task DirectEditing_RequiresWholeDocumentGrant_AndCommitsOneUndoableChange()
    {
        using var sandbox = new CliSandbox();
        var skins = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await skins.CreateAsync(SkinModel.Classic, cancellationToken: TestContext.CancellationToken);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => skins.Proposals.CreateScopeAsync(id,
            new(Surfaces: [Front], DirectEditing: true), TestContext.CancellationToken));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => skins.Proposals.CreateScopeAsync(id,
            new(ReadOnly: true, DirectEditing: true), TestContext.CancellationToken));
        var scope = await skins.Proposals.CreateScopeAsync(id, new(DirectEditing: true), TestContext.CancellationToken);
        Assert.IsTrue(scope.Limits.AllowLayerChanges, "The returned grant must not deny the layer creation it authorizes.");
        Assert.IsTrue(new SkinService(new LauncherPaths(sandbox.Root)).Proposals.ReadScope(scope.ScopeId).Limits.AllowLayerChanges);
        var result = await skins.Proposals.ProposeAsync(scope.ScopeId, 0,
            [new() { Tool = SkinTool.Pencil, Surface = Front, Color = "#FF0000" },
             new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "ai" } },
             new() { Tool = SkinTool.Pencil, Surface = Front, PaintLayerId = "ai", X = 1, Color = "#00FF00" }], cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual("accepted", result.State);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(skins.Read(id), Front, 0, 0, SkinDocument.BackgroundLayerId));
        Assert.AreEqual("#00FF00FF", SkinEditor.Sample(skins.Read(id), Front, 1, 0, "ai"));
        await skins.RestoreAsync(id, 1, cancellationToken: TestContext.CancellationToken);
        Assert.IsEmpty(skins.Read(id).Layers);
        Assert.AreEqual("#808080FF", SkinEditor.Sample(skins.Read(id), Front, 0, 0));
    }

    /// <summary>
    /// A coherent AI part-edit batch can preserve the original layer, hide it, and expose a separately editable
    /// copy in one named undo step. This verifies the real transaction recommended by the skin skill, not a
    /// scripted model's compliance; ordinary undo must restore both artwork and visibility together.
    /// </summary>
    [TestMethod]
    public async Task LayeredAiStep_PreservesOriginalPixels_AndRestoresVisibilityInOneUndo()
    {
        using var sandbox = new CliSandbox();
        var skins = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await skins.CreateAsync(SkinModel.Classic, template: "classic", cancellationToken: TestContext.CancellationToken);
        await skins.EditAsync(id, 0,
            [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "original", Name = "原帽徽" } },
             new() { Tool = SkinTool.Pencil, Surface = Front, PaintLayerId = "original", Color = "#FF0000" }], TestContext.CancellationToken);
        var before = skins.Read(id);
        var scope = await skins.Proposals.CreateScopeAsync(id, new(DirectEditing: true), TestContext.CancellationToken);
        await skins.Proposals.ProposeAsync(scope.ScopeId, 1,
            [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Duplicate, Id = "hat", SourceId = "original", Name = "帽徽工作层" } },
             new() { Tool = SkinTool.Pencil, Surface = Front, PaintLayerId = "hat", Color = "#C9A227" },
             new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Set, Id = "original", Visible = false } }],
            "帽徽配色，保留原稿", TestContext.CancellationToken);
        var saved = skins.Read(id);
        CollectionAssert.AreEqual(before.Pixels, saved.Pixels);
        CollectionAssert.AreEqual(before.Layers.Single().Pixels, saved.Layers.Single(layer => layer.Id == "original").Pixels);
        Assert.IsFalse(saved.Layers.Single(layer => layer.Id == "original").Visible);
        Assert.AreEqual("#C9A227FF", SkinEditor.Sample(saved, Front, 0, 0, "hat"));
        Assert.AreEqual("帽徽配色，保留原稿", SkinEditor.History(saved).Last().Transaction.Label);
        Assert.HasCount(before.Undo.Count + 1, saved.Undo);
        var restored = await skins.RestoreAsync(id, 2, cancellationToken: TestContext.CancellationToken);
        Assert.HasCount(1, restored.Layers);
        Assert.IsTrue(restored.Layers.Single().Visible);
        CollectionAssert.AreEqual(before.Layers.Single().Pixels, restored.Layers.Single().Pixels);
    }

    /// <summary>Gets or sets cancellation for isolated filesystem operations.</summary>
    public TestContext TestContext { get; set; }
}