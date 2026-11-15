// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Tools;

internal sealed record SkinCreateInput(SkinModel Model = SkinModel.Classic, string? Input = null, string? Color = null, int Size = 64, string? Template = null, string Name = "Untitled");
internal sealed record SkinDocumentInput(string DocumentId);
internal sealed record SkinReadInput(string DocumentId, SkinPart Part, SkinLayer Layer, SkinFace Face, string? PaintLayerId = null);
internal sealed record SkinEditInput(string DocumentId, long ExpectedRevision, SkinEdit[] Edits);
internal sealed record SkinRestoreInput(string DocumentId, long ExpectedRevision);
/// <summary>Addresses a composite or one original paint layer for an explicitly authorized PNG publication.</summary>
/// <param name="DocumentId">The source project identity.</param>
/// <param name="Output">The exact destination PNG path.</param>
/// <param name="Overwrite">Whether replacement of an existing destination is authorized.</param>
/// <param name="PaintLayerId">The layer whose raw RGBA is exported, or null for the visible composite.</param>
internal sealed record SkinExportInput(string DocumentId, string Output, bool Overwrite = false, string? PaintLayerId = null);
/// <summary>Publishes a static view of the current work and its optional saved companion.</summary>
/// <param name="DocumentId">The current managed work.</param>
/// <param name="Output">The authorized destination PNG path.</param>
/// <param name="View">Optional camera and surface visibility.</param>
/// <param name="Overwrite">Whether replacing an existing output is authorized.</param>
/// <param name="Solo">Whether to omit the saved companion for this preview only.</param>
internal sealed record SkinPreviewInput(string DocumentId, string Output, SkinViewOptions? View = null, bool Overwrite = false, bool Solo = false);
/// <summary>Sets or clears a preview association through the existing project transaction.</summary>
/// <param name="DocumentId">The work whose association is changed.</param>
/// <param name="ExpectedRevision">The observed artwork revision.</param>
/// <param name="CompanionId">A saved character work for a cape preview, used when Clear is false.</param>
/// <param name="Clear">Whether to remove the association; mutually exclusive with CompanionId.</param>
internal sealed record SkinOutfitInput(string DocumentId, long ExpectedRevision, string? CompanionId = null, bool Clear = false);
internal sealed record SkinPickInput(string DocumentId, SkinPickRequest Request);
internal sealed record SkinSelectionInput(string DocumentId, SkinSelectionRequest Request);
internal sealed record SkinCaptureInput(string DocumentId, SkinCaptureRequest Request);

internal sealed record SkinNameInput(string DocumentId, string Name);
internal sealed record SkinHistoryInput(string DocumentId, long ExpectedRevision, int Index);
/// <summary>Snapshots a PNG for one undoable whole-skin replacement or additive layer import.</summary>
/// <param name="DocumentId">The existing destination project.</param>
/// <param name="ExpectedRevision">The observed project revision.</param>
/// <param name="Input">The authorized source PNG path.</param>
/// <param name="Model">The character or cape model for whole-work replacement; additive import retains the current model.</param>
/// <param name="AsLayer">Whether to preserve existing content and add the same-sized PNG as a new layer.</param>
/// <param name="Name">An optional new layer label; omission uses the source filename.</param>
internal sealed record SkinImportInput(string DocumentId, long ExpectedRevision, string Input, SkinModel Model = SkinModel.Classic, bool AsLayer = false, string? Name = null);
internal sealed record SkinFootprintInput(string DocumentId, SkinEdit Request);
internal sealed record SkinPreviewEditsInput(string DocumentId, long ExpectedRevision, SkinEdit[] Edits, string Output, bool Overwrite = false);
internal sealed record SkinProposeInput(string ScopeId, long ExpectedRevision, SkinEdit[] Edits, string? Label = null);
internal sealed record SkinProposalInput(string ProposalId);
internal sealed record SkinAcceptInput(string ProposalId, long ExpectedRevision);
internal sealed record SkinCandidatePreviewInput(string ProposalId, SkinViewOptions? View = null);
internal sealed record SkinReceiptInput(string DocumentId, string OperationId);
internal sealed record SkinEmptyInput;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(SkinNameInput))]
[JsonSerializable(typeof(SkinHistoryInput))]
[JsonSerializable(typeof(SkinImportInput))]
[JsonSerializable(typeof(SkinFootprintInput))]
[JsonSerializable(typeof(SkinPreviewEditsInput))]
[JsonSerializable(typeof(SkinProposeInput))]
[JsonSerializable(typeof(SkinProposalInput))]
[JsonSerializable(typeof(SkinAcceptInput))]
[JsonSerializable(typeof(SkinCandidatePreviewInput))]
[JsonSerializable(typeof(SkinReceiptInput))]
[JsonSerializable(typeof(SkinEmptyInput))]
[JsonSerializable(typeof(SkinCreateInput))]
[JsonSerializable(typeof(SkinDocumentInput))]
[JsonSerializable(typeof(SkinReadInput))]
[JsonSerializable(typeof(SkinEditInput))]
[JsonSerializable(typeof(SkinRestoreInput))]
[JsonSerializable(typeof(SkinExportInput))]
[JsonSerializable(typeof(SkinPreviewInput))]
[JsonSerializable(typeof(SkinOutfitInput))]
[JsonSerializable(typeof(SkinEdit[]))]
[JsonSerializable(typeof(SkinPickInput))]
[JsonSerializable(typeof(SkinSelectionInput))]
[JsonSerializable(typeof(SkinCaptureInput))]
[JsonSerializable(typeof(SkinViewOptions))]
internal sealed partial class SkinToolJsonContext : JsonSerializerContext;