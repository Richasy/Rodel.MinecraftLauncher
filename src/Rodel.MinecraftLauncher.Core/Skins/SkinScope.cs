// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;

namespace Rodel.MinecraftLauncher.Core.Skins;

/// <summary>Defines host-authorized candidate limits; create this outside an AI's bound MCP tool catalog.</summary>
/// <param name="PaintLayerId">The sole writable paint layer; null means the protected background. A missing named layer may be added only when AllowLayerChanges is true.</param>
/// <param name="Surfaces">The allowed model surfaces; null permits all, empty permits none.</param>
/// <param name="PixelMask">An optional atlas-sized mask intersected with the allowed surfaces.</param>
/// <param name="ReadOnly">Whether candidate creation and acceptance are prohibited.</param>
/// <param name="AllowLayerChanges">Whether the addressed candidate layer may be added or have its metadata changed; direct editing always permits layer changes throughout the bound document.</param>
/// <param name="DirectEditing">Whether the host authorizes immediate editing of all content, including adding and duplicating paint layers, in this document without a separate candidate approval.</param>
public sealed record SkinScopeRequest(string? PaintLayerId = null, SkinSurface[]? Surfaces = null,
    bool[]? PixelMask = null, bool ReadOnly = false, bool AllowLayerChanges = false, bool DirectEditing = false);

/// <summary>Identifies a durable, revocable host grant bound to one document and geometry.</summary>
/// <param name="ScopeId">The random grant ID.</param>
/// <param name="DocumentId">The only accessible project.</param>
/// <param name="Model">The model for which the grant was created.</param>
/// <param name="Size">The texture dimension for which the grant was created.</param>
/// <param name="Limits">The trusted edit limits.</param>
/// <param name="Revoked">Whether the host has revoked this grant.</param>
public sealed record SkinScope(string ScopeId, string DocumentId, SkinModel Model, int Size, SkinScopeRequest Limits, bool Revoked = false);

internal sealed record SkinProposal(string ProposalId, string ScopeId, string DocumentId, long BaseRevision,
    SkinDocument Content, string? Label, DateTimeOffset CreatedAt, bool Rejected = false);

/// <summary>Describes candidate review state and any durable acceptance result.</summary>
/// <param name="ProposalId">The candidate and acceptance operation ID.</param>
/// <param name="ScopeId">The grant used to create it.</param>
/// <param name="DocumentId">The target project.</param>
/// <param name="BaseRevision">The content revision used for generation.</param>
/// <param name="CurrentRevision">The latest observed project revision.</param>
/// <param name="State">pending, stale, accepted, rejected or revoked.</param>
/// <param name="Label">An optional description supplied by the caller.</param>
/// <param name="CreatedAt">The candidate creation time.</param>
/// <param name="Receipt">The durable result when accepted.</param>
public sealed record SkinProposalInfo(string ProposalId, string ScopeId, string DocumentId, long BaseRevision,
    long CurrentRevision, string State, string? Label, DateTimeOffset CreatedAt, SkinCommitReceipt? Receipt);