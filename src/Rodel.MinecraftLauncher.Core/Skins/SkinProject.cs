// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Skins;

internal sealed record SkinProject(
    SkinDocument Document,
    string Name,
    DateTimeOffset ModifiedAt,
    SkinCommitReceipt[] Receipts,
    string? CompanionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IsCapePreviewSkin = false);

/// <summary>Records the durable result of accepting one frozen candidate, atomically with its document.</summary>
/// <param name="OperationId">The candidate identifier used for idempotent lookup.</param>
/// <param name="DocumentId">The project that owns the result.</param>
/// <param name="BaseRevision">The revision reviewed by the player.</param>
/// <param name="Revision">The committed revision, unchanged for a no-op.</param>
/// <param name="Changed">Whether content changed.</param>
/// <param name="CommittedAt">The UTC publication time.</param>
public sealed record SkinCommitReceipt(string OperationId, string DocumentId, long BaseRevision, long Revision, bool Changed, DateTimeOffset CommittedAt);

/// <summary>Describes a discoverable project without loading texture buffers into a UI list.</summary>
/// <param name="DocumentId">The durable project ID.</param>
/// <param name="Name">The player-assigned project name.</param>
/// <param name="Model">The current character or cape model.</param>
/// <param name="Size">The texture width; cape height is half this value.</param>
/// <param name="Revision">The current content revision.</param>
/// <param name="ModifiedAt">The most recent project publication time.</param>
/// <param name="CompanionId">The optional opposite-kind work used only as a preview reference.</param>
public sealed record SkinProjectInfo(string DocumentId, string Name, SkinModel Model, int Size, long Revision, DateTimeOffset ModifiedAt, string? CompanionId = null);