// Copyright (c) Richasy. All rights reserved.
using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Core.Tools.ToolSerialization;
namespace Rodel.MinecraftLauncher.Core.Tools;
/// <summary>Thin typed bindings for the shared editable document and candidate services.</summary>
internal sealed class SkinToolActions(SkinService skins)
{
    private static Task<JsonNode?> Value<T>(T value) => Task.FromResult(Node(value));
    private Task<JsonNode?> Summary(string id, SkinDocument? document = null) => Value(skins.Describe(id, document));
    internal async Task<JsonNode?> SkinCreateAsync(SkinCreateInput a, CancellationToken ct) => await Summary(await skins.CreateAsync(a.Model, a.Input, a.Color ?? "#808080", a.Size, a.Template, a.Name, ct).ConfigureAwait(false)).ConfigureAwait(false);
    internal Task<JsonNode?> SkinInspectAsync(SkinDocumentInput a, CancellationToken _) => Summary(a.DocumentId);
    internal Task<JsonNode?> SkinReadFaceAsync(SkinReadInput a, CancellationToken _)
    {
        var document = skins.Read(a.DocumentId);
        var block = SkinEditor.Capture(document, new() { Part = a.Part, Layer = a.Layer, Face = a.Face }, paintLayerId: a.PaintLayerId);
        var rows = new JsonArray();
        for (var y = 0; y < block.Height; y++)
        {
            var row = new JsonArray();
            for (var x = 0; x < block.Width; x++) row.Add((JsonNode?)JsonValue.Create($"#{block.Pixels[(y * block.Width) + x]:X8}"));
            rows.Add((JsonNode)row);
        }

        return Task.FromResult<JsonNode?>(new JsonObject { ["revision"] = document.Revision, ["width"] = block.Width, ["height"] = block.Height, ["rows"] = rows });
    }
    internal async Task<JsonNode?> SkinEditAsync(SkinEditInput a, CancellationToken ct) => await Summary(a.DocumentId, await skins.EditAsync(a.DocumentId, a.ExpectedRevision, a.Edits, ct).ConfigureAwait(false)).ConfigureAwait(false);
    internal async Task<JsonNode?> SkinUndoAsync(SkinRestoreInput a, CancellationToken ct) => await Summary(a.DocumentId, await skins.RestoreAsync(a.DocumentId, a.ExpectedRevision, false, ct).ConfigureAwait(false)).ConfigureAwait(false);
    internal async Task<JsonNode?> SkinRedoAsync(SkinRestoreInput a, CancellationToken ct) => await Summary(a.DocumentId, await skins.RestoreAsync(a.DocumentId, a.ExpectedRevision, true, ct).ConfigureAwait(false)).ConfigureAwait(false);
    internal async Task<JsonNode?> SkinRestoreAsync(SkinHistoryInput a, CancellationToken ct) => await Summary(a.DocumentId, await skins.RestoreToAsync(a.DocumentId, a.ExpectedRevision, a.Index, ct).ConfigureAwait(false)).ConfigureAwait(false);
    internal async Task<JsonNode?> SkinExportAsync(SkinExportInput a, CancellationToken ct)
    {
        if (a.PaintLayerId is null) await skins.ExportAsync(a.DocumentId, a.Output, a.Overwrite, ct).ConfigureAwait(false);
        else await skins.ExportLayerAsync(a.DocumentId, a.PaintLayerId, a.Output, a.Overwrite, ct).ConfigureAwait(false);
        return new JsonObject { ["path"] = Path.GetFullPath(a.Output), ["accountSkinChanged"] = false };
    }
    internal async Task<JsonNode?> SkinOutfitAsync(SkinOutfitInput a, CancellationToken ct)
    {
        if (a.Clear == (a.CompanionId is not null)) throw new ArgumentException("Choose a companion or clear.");
        await skins.SetCompanionAsync(a.DocumentId, a.ExpectedRevision, a.Clear ? null : a.CompanionId, ct).ConfigureAwait(false);
        return await Summary(a.DocumentId).ConfigureAwait(false);
    }
    internal async Task<JsonNode?> SkinPreviewAsync(SkinPreviewInput a, CancellationToken ct)
    {
        await skins.PreviewAsync(a.DocumentId, a.Output, a.View, a.Overwrite, a.Solo, ct).ConfigureAwait(false);
        return new JsonObject { ["path"] = Path.GetFullPath(a.Output), ["kind"] = "png" };
    }
    internal Task<JsonNode?> SkinPickAsync(SkinPickInput a, CancellationToken _)
    {
        var document = skins.Read(a.DocumentId);
        return Task.FromResult<JsonNode?>(new JsonObject { ["revision"] = document.Revision, ["hit"] = Node(SkinGeometry.Pick(document, a.Request)) });
    }
    internal Task<JsonNode?> SkinSelectAsync(SkinSelectionInput a, CancellationToken _)
    {
        var document = skins.Read(a.DocumentId);
        return Task.FromResult<JsonNode?>(new JsonObject { ["revision"] = document.Revision, ["selection"] = Node(SkinSelections.Create(document, a.Request)) });
    }
    internal Task<JsonNode?> SkinCaptureAsync(SkinCaptureInput a, CancellationToken _)
    {
        var document = skins.Read(a.DocumentId);
        return Task.FromResult<JsonNode?>(new JsonObject { ["revision"] = document.Revision, ["clipboard"] = Node(SkinEditor.Capture(document, a.Request.Surface, a.Request.Selection, a.Request.PaintLayerId)) });
    }
    internal Task<JsonNode?> SkinListAsync(SkinEmptyInput _, CancellationToken _1) => Value(skins.List());
    internal static Task<JsonNode?> SkinTemplatesAsync(SkinEmptyInput _, CancellationToken _1) => Task.FromResult<JsonNode?>(new JsonArray([.. SkinTemplates.Ids.Select(id => (JsonNode)new JsonObject { ["templateId"] = id, ["suggestedModel"] = SkinTemplates.SuggestedModel(id).ToString() })]));
    internal async Task<JsonNode?> SkinCloneAsync(SkinNameInput a, CancellationToken ct) => await Summary(await skins.CloneAsync(a.DocumentId, a.Name, ct).ConfigureAwait(false)).ConfigureAwait(false);
    internal async Task<JsonNode?> SkinRenameAsync(SkinNameInput a, CancellationToken ct)
    {
        await skins.RenameAsync(a.DocumentId, a.Name, ct).ConfigureAwait(false);
        return await Summary(a.DocumentId).ConfigureAwait(false);
    }
    internal Task<JsonNode?> SkinHistoryAsync(SkinDocumentInput a, CancellationToken _) => Task.FromResult<JsonNode?>(new JsonObject { ["revision"] = skins.Read(a.DocumentId).Revision, ["entries"] = Node(skins.History(a.DocumentId)) });
    internal async Task<JsonNode?> SkinImportAsync(SkinImportInput a, CancellationToken ct)
    {
        if (!a.AsLayer && a.Name is not null) throw new ArgumentException("A layer name requires additive import.");
        var document = a.AsLayer ? await skins.ImportLayerAsync(a.DocumentId, a.ExpectedRevision, a.Input, a.Name, ct).ConfigureAwait(false) : await skins.ImportAsync(a.DocumentId, a.ExpectedRevision, a.Input, a.Model, ct).ConfigureAwait(false);
        return await Summary(a.DocumentId, document).ConfigureAwait(false);
    }
    internal Task<JsonNode?> SkinFootprintAsync(SkinFootprintInput a, CancellationToken ct) => Task.FromResult<JsonNode?>(new JsonObject { ["revision"] = skins.Read(a.DocumentId).Revision, ["marks"] = Node(skins.BrushFootprint(a.DocumentId, a.Request, ct)) });
    internal async Task<JsonNode?> SkinPreviewEditsAsync(SkinPreviewEditsInput a, CancellationToken ct)
    {
        var document = skins.PreviewEdits(a.DocumentId, a.ExpectedRevision, a.Edits, ct);
        await SkinService.PublishPreviewAsync(document, a.Output, a.Overwrite, ct).ConfigureAwait(false);
        return new JsonObject { ["path"] = Path.GetFullPath(a.Output), ["kind"] = "png", ["saved"] = false, ["revision"] = document.Revision };
    }
    internal async Task<JsonNode?> SkinProposeAsync(SkinProposeInput a, CancellationToken ct) => Node(await skins.Proposals.ProposeAsync(a.ScopeId, a.ExpectedRevision, a.Edits, a.Label, ct).ConfigureAwait(false));
    internal Task<JsonNode?> SkinProposalShowAsync(SkinProposalInput a, CancellationToken _) => Value(skins.Proposals.Describe(a.ProposalId));
    internal async Task<JsonNode?> SkinProposalAcceptAsync(SkinAcceptInput a, CancellationToken ct) => Node(await skins.Proposals.AcceptAsync(a.ProposalId, a.ExpectedRevision, ct).ConfigureAwait(false));
    internal async Task<JsonNode?> SkinProposalRejectAsync(SkinProposalInput a, CancellationToken ct)
    {
        await skins.Proposals.RejectAsync(a.ProposalId, ct).ConfigureAwait(false);
        return Node(skins.Proposals.Describe(a.ProposalId));
    }
    internal async Task<JsonNode?> SkinProposalPreviewAsync(SkinCandidatePreviewInput a, CancellationToken ct) => new JsonObject { ["path"] = await skins.Proposals.PreviewAsync(a.ProposalId, a.View, ct).ConfigureAwait(false), ["kind"] = "png", ["saved"] = false };
    internal Task<JsonNode?> SkinReceiptAsync(SkinReceiptInput a, CancellationToken _) => Value(skins.Proposals.Receipt(a.DocumentId, a.OperationId));
}