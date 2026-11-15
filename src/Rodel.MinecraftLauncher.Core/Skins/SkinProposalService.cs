// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Skins;

/// <summary>Stages frozen candidates within host grants and accepts reviewed content atomically with durable operation receipts.</summary>
public sealed class SkinProposalService(LauncherPaths paths)
{
    private readonly SkinService _skins = new(paths);

    /// <summary>Creates a revocable document/geometry/layer/pixel grant; only the host should expose this operation.</summary>
    public async Task<SkinScope> CreateScopeAsync(string documentId, SkinScopeRequest limits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();
        if (limits.DirectEditing && (limits.ReadOnly || limits.PaintLayerId is not null || limits.Surfaces is not null || limits.PixelMask is not null))
            throw new ArgumentException("Direct editing authorizes this whole document; do not combine it with read-only or partial edit limits.", nameof(limits));
        limits = limits with
        {
            AllowLayerChanges = limits.DirectEditing || limits.AllowLayerChanges,
            Surfaces = limits.Surfaces is null ? null : [.. limits.Surfaces],
            PixelMask = limits.PixelMask is null ? null : (bool[])limits.PixelMask.Clone()
        };
        using var lease = await ResourceLease.AcquireAsync(_skins.DocumentPath(documentId), cancellationToken).ConfigureAwait(false);
        var document = _skins.Read(documentId);
        if (limits.PixelMask is not null && limits.PixelMask.Length != document.Pixels.Length)
            throw new ArgumentException("The authorized mask must match the document texture dimensions.", nameof(limits));
        foreach (var surface in limits.Surfaces ?? [])
            _ = SkinLayout.Face(document.Model, surface, document.Size);
        var layerId = limits.PaintLayerId ?? SkinLayers.BackgroundId;
        if (layerId != SkinLayers.BackgroundId && !document.Layers.Any(layer => layer.Id == layerId) && !limits.AllowLayerChanges)
            throw new ArgumentException("The authorized paint layer does not exist.", nameof(limits));
        if (string.IsNullOrWhiteSpace(layerId))
            throw new ArgumentException("The authorized paint layer must have an identifier.", nameof(limits));
        var scope = new SkinScope(Guid.NewGuid().ToString("N"), documentId, document.Model, document.Size, limits);
        await WriteAsync(ScopePath(scope.ScopeId), scope, SkinsJsonContext.Default.SkinScope, cancellationToken).ConfigureAwait(false);
        return scope;
    }

    /// <summary>Reads a host grant with effective layer authority for its edit mode; it does not make a revoked grant active again.</summary>
    public SkinScope ReadScope(string scopeId)
    {
        var scope = Read(ScopePath(scopeId), SkinsJsonContext.Default.SkinScope);
        // Direct mode already bypasses candidate-only layer limits in CheckEffects. Disclose that same authority,
        // including for grants retained by an existing editor conversation, without rewriting stored grants.
        return scope.Limits is { DirectEditing: true, AllowLayerChanges: false }
            ? scope with { Limits = scope.Limits with { AllowLayerChanges = true } }
            : scope;
    }

    /// <summary>Revokes a grant under its document lease, serializing against candidate acceptance.</summary>
    public async Task RevokeScopeAsync(string scopeId, CancellationToken cancellationToken = default)
    {
        var scope = ReadScope(scopeId);
        using var lease = await ResourceLease.AcquireAsync(_skins.DocumentPath(scope.DocumentId), cancellationToken).ConfigureAwait(false);
        scope = ReadScope(scopeId);
        await WriteAsync(ScopePath(scopeId), scope with { Revoked = true }, SkinsJsonContext.Default.SkinScope, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Freezes typed edits within the host grant; direct-edit grants immediately commit them with a receipt, while other grants leave a pending candidate.</summary>
    public async Task<SkinProposalInfo> ProposeAsync(string scopeId, long expectedRevision, SkinEdit[] edits, string? label = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(edits);
        // Host selection/point buffers may be reused while the document lease is pending.
        // Freeze the entire typed action graph before the first await, not just its top-level array.
        edits = JsonSerializer.Deserialize(JsonSerializer.Serialize(edits, SkinJsonContext.Default.SkinEditArray), SkinJsonContext.Default.SkinEditArray)!;
        var scope = ReadScope(scopeId);
        using var lease = await ResourceLease.AcquireAsync(_skins.DocumentPath(scope.DocumentId), cancellationToken).ConfigureAwait(false);
        scope = ReadScope(scopeId);
        var document = _skins.Read(scope.DocumentId);
        RequireScope(scope, document, write: true);
        var content = SkinEditor.Preview(document, edits, expectedRevision, cancellationToken) with { Undo = [], Redo = [] };
        CheckEffects(scope, document, content);
        var proposal = new SkinProposal(Guid.NewGuid().ToString("N"), scopeId, scope.DocumentId, expectedRevision,
            content, label, DateTimeOffset.UtcNow);
        await WriteAsync(ProposalPath(proposal.ProposalId), proposal, SkinStoreJsonContext.Default.SkinProposal, cancellationToken).ConfigureAwait(false);
        if (scope.Limits.DirectEditing)
        {
            lease.Dispose();
            await AcceptAsync(proposal.ProposalId, expectedRevision, cancellationToken).ConfigureAwait(false);
        }

        return Describe(proposal.ProposalId);
    }

    /// <summary>Returns current candidate state; a durable receipt remains authoritative even after later edits or grant revocation.</summary>
    public SkinProposalInfo Describe(string proposalId)
    {
        var proposal = ReadProposal(proposalId);
        var project = _skins.ReadProject(proposal.DocumentId);
        var receipt = project.Receipts.SingleOrDefault(item => item.OperationId == proposalId);
        var scope = ReadScope(proposal.ScopeId);
        var state = receipt is not null ? "accepted" : proposal.Rejected ? "rejected" : scope.Revoked ? "revoked"
            : project.Document.Revision != proposal.BaseRevision ? "stale" : "pending";
        return new(proposalId, proposal.ScopeId, proposal.DocumentId, proposal.BaseRevision, project.Document.Revision,
            state, proposal.Label, proposal.CreatedAt, receipt);
    }

    /// <summary>Reads frozen candidate content for comparison; this does not replace the saved document.</summary>
    public SkinDocument ReadContent(string proposalId) => ReadProposal(proposalId).Content;

    /// <summary>Accepts the exact frozen candidate once; repeated acceptance returns its original receipt without replaying edits.</summary>
    public async Task<SkinCommitReceipt> AcceptAsync(string proposalId, long expectedRevision, CancellationToken cancellationToken = default)
    {
        var proposal = ReadProposal(proposalId);
        using var lease = await ResourceLease.AcquireAsync(_skins.DocumentPath(proposal.DocumentId), cancellationToken).ConfigureAwait(false);
        var project = _skins.ReadProject(proposal.DocumentId);
        if (project.Receipts.SingleOrDefault(item => item.OperationId == proposalId) is { } existing)
            return existing;
        proposal = ReadProposal(proposalId);
        if (proposal.Rejected)
            throw new InvalidOperationException("This candidate was discarded.");
        var scope = ReadScope(proposal.ScopeId);
        RequireScope(scope, project.Document, write: true);
        if (expectedRevision != proposal.BaseRevision || project.Document.Revision != proposal.BaseRevision)
            throw new InvalidOperationException("The skin changed after candidate generation; generate a new candidate without replaying the old one.");
        CheckEffects(scope, project.Document, proposal.Content);
        var document = SkinEditor.ReplaceContent(project.Document, proposal.Content, expectedRevision,
            new("candidate", "ai", proposal.Label), cancellationToken);
        var receipt = new SkinCommitReceipt(proposalId, proposal.DocumentId, expectedRevision, document.Revision,
            document.Revision != expectedRevision, DateTimeOffset.UtcNow);
        // One atomic project publication owns both pixels and receipt: a lost response can be resolved without replay.
        await _skins.SaveProjectAsync(proposal.DocumentId, project with
        {
            Document = document,
            Receipts = [.. project.Receipts, receipt],
            ModifiedAt = receipt.CommittedAt
        }, cancellationToken).ConfigureAwait(false);
        return receipt;
    }

    /// <summary>Discards an unaccepted candidate without touching project pixels or history; accepted candidates cannot be silently undone.</summary>
    public async Task RejectAsync(string proposalId, CancellationToken cancellationToken = default)
    {
        var proposal = ReadProposal(proposalId);
        using var lease = await ResourceLease.AcquireAsync(_skins.DocumentPath(proposal.DocumentId), cancellationToken).ConfigureAwait(false);
        if (_skins.ReadProject(proposal.DocumentId).Receipts.Any(item => item.OperationId == proposalId))
            throw new InvalidOperationException("An accepted candidate requires an ordinary document undo.");
        proposal = ReadProposal(proposalId);
        await WriteAsync(ProposalPath(proposalId), proposal with { Rejected = true }, SkinStoreJsonContext.Default.SkinProposal, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the atomically recorded result for an operation, or null when it was never accepted in this project.</summary>
    public SkinCommitReceipt? Receipt(string documentId, string operationId) =>
        _skins.ReadProject(documentId).Receipts.SingleOrDefault(item => item.OperationId == operationId);

    /// <summary>Publishes a candidate preview only inside the managed skin store; callers do not choose arbitrary output paths.</summary>
    public async Task<string> PreviewAsync(string proposalId, SkinViewOptions? view = null, CancellationToken cancellationToken = default)
    {
        var output = Path.Combine(paths.Root, "skins", "previews", SkinService.CheckId(proposalId) + ".png");
        await SkinService.PublishAsync(output, SkinPreview.Render(ReadContent(proposalId), view, cancellationToken: cancellationToken), true, cancellationToken).ConfigureAwait(false);
        return output;
    }

    /// <summary>Checks that a bound MCP host still owns the requested document and candidate; grant identity is not supplied by model arguments.</summary>
    public void CheckBinding(string scopeId, string? documentId = null, string? proposalId = null)
    {
        var scope = ReadScope(scopeId);
        RequireScope(scope, _skins.Read(scope.DocumentId), write: false);
        if ((documentId is not null && documentId != scope.DocumentId)
            || (proposalId is not null && ReadProposal(proposalId).ScopeId != scopeId))
        {
            throw new UnauthorizedAccessException("The request is outside this editor's bound skin scope.");
        }
    }

    private static void RequireScope(SkinScope scope, SkinDocument document, bool write)
    {
        if (scope.Revoked || (write && scope.Limits.ReadOnly))
            throw new UnauthorizedAccessException("The skin grant is revoked or read-only.");
        if (scope.Model != document.Model || scope.Size != document.Size)
            throw new InvalidOperationException("The document geometry changed; create a new host grant.");
    }

    private static void CheckEffects(SkinScope scope, SkinDocument before, SkinDocument after)
    {
        if (before.Model != after.Model || before.Size != after.Size)
            throw new UnauthorizedAccessException("Candidate cannot replace document configuration or background metadata.");
        var limits = scope.Limits;
        if (limits.DirectEditing) return;
        if (!before.TransparencyMask.AsSpan().SequenceEqual(after.TransparencyMask))
            throw new UnauthorizedAccessException("Changing the document transparency mask requires an unrestricted editor grant.");
        if (before.BackgroundName != after.BackgroundName)
            throw new UnauthorizedAccessException("Candidate cannot replace background metadata.");
        var allowed = new bool[before.Pixels.Length];
        foreach (var face in SkinLayout.Faces(before.Model, before.Size).Where(face => limits.Surfaces is null || limits.Surfaces.Contains(face.Surface)))
        {
            for (var y = 0; y < face.Height; y++)
            {
                for (var x = 0; x < face.Width; x++)
                {
                    var index = ((face.Y + y) * before.Size) + face.X + x;
                    allowed[index] = limits.PixelMask is null || limits.PixelMask[index];
                }
            }
        }

        var target = limits.PaintLayerId ?? SkinLayers.BackgroundId;
        CheckPixels(before.Pixels, after.Pixels, target == SkinLayers.BackgroundId);
        var beforeIds = before.Layers.Select(layer => layer.Id).Where(id => id != target);
        var afterIds = after.Layers.Select(layer => layer.Id).Where(id => id != target);
        if (!beforeIds.SequenceEqual(afterIds) || !before.Layers.Select(layer => layer.Id).Intersect(after.Layers.Select(layer => layer.Id))
            .SequenceEqual(after.Layers.Select(layer => layer.Id).Intersect(before.Layers.Select(layer => layer.Id))))
        {
            throw new UnauthorizedAccessException("Candidate cannot reorder or remove unrelated layers.");
        }

        foreach (var id in before.Layers.Select(layer => layer.Id).Union(after.Layers.Select(layer => layer.Id)))
        {
            var a = before.Layers.SingleOrDefault(layer => layer.Id == id);
            var b = after.Layers.SingleOrDefault(layer => layer.Id == id);
            var writable = id == target;
            if ((a is null || b is null) && (!writable || !limits.AllowLayerChanges || b is null))
                throw new UnauthorizedAccessException("Candidate cannot add or remove this layer.");
            if (a is not null && b is not null && (a.Name != b.Name || a.Visible != b.Visible || a.Opacity != b.Opacity || a.Locked != b.Locked)
                && (!writable || !limits.AllowLayerChanges))
            {
                throw new UnauthorizedAccessException("Candidate cannot change this layer's metadata.");
            }

            CheckPixels(a?.Pixels ?? new uint[before.Pixels.Length], b?.Pixels ?? new uint[before.Pixels.Length], writable);
        }

        CheckPixels(SkinLayers.Pixels(before), SkinLayers.Pixels(after), true);
        return;

        void CheckPixels(uint[] a, uint[] b, bool writable)
        {
            for (var index = 0; index < a.Length; index++)
            {
                if (a[index] != b[index] && (!writable || !allowed[index]))
                    throw new UnauthorizedAccessException("Candidate writes outside the host-authorized layer, surface or pixel mask.");
            }
        }
    }

    private SkinProposal ReadProposal(string id)
    {
        var proposal = Read(ProposalPath(id), SkinStoreJsonContext.Default.SkinProposal);
        proposal.Content.Validate();
        return proposal;
    }
    private string ScopePath(string id) => Path.Combine(paths.Root, "skins", "scopes", SkinService.CheckId(id) + ".json");
    private string ProposalPath(string id) => Path.Combine(paths.Root, "skins", "proposals", SkinService.CheckId(id) + ".json");
    private static T Read<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) where T : class =>
        JsonFiles.ReadObject(path).Deserialize(type) ?? throw new InvalidDataException("The skin review record is empty.");
    private static Task WriteAsync<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken cancellationToken) =>
        JsonFiles.WriteAsync(path, JsonSerializer.SerializeToNode(value, type)!, cancellationToken);
}