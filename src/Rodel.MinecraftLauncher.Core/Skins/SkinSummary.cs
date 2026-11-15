// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;

namespace Rodel.MinecraftLauncher.Core.Skins;

/// <summary>Describes an editable skin without exposing texture pixels or transaction snapshots.</summary>
/// <param name="DocumentId">The identifier used to load or edit the stored document.</param>
/// <param name="Revision">The optimistic concurrency revision required by the next edit.</param>
/// <param name="DocumentSchemaVersion">The durable document format version.</param>
/// <param name="Model">The Classic/Slim character or standalone Cape model interpreting the texture.</param>
/// <param name="Width">The texture width in pixels.</param>
/// <param name="Height">The texture height in pixels.</param>
/// <param name="CanUndo">Whether an undo snapshot is available.</param>
/// <param name="CanRedo">Whether a redo snapshot is available.</param>
/// <param name="PaintLayers">The background and additional paint-layer settings.</param>
/// <param name="Faces">The model's editable UV rectangles.</param>
/// <param name="Name">The player-assigned project name.</param>
/// <param name="ModifiedAt">The most recent project publication time.</param>
/// <param name="CompanionId">An optional preview-only reference to a separately saved opposite-kind work.</param>
public sealed record SkinSummary(string DocumentId, long Revision, int DocumentSchemaVersion, SkinModel Model,
    int Width, int Height, bool CanUndo, bool CanRedo, SkinLayerInfo[] PaintLayers, SkinFaceInfo[] Faces, string Name, DateTimeOffset ModifiedAt, string? CompanionId = null);