// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Stores semantic history information; hosts translate kinds and supply trusted provenance.</summary>
/// <param name="Kind">The invariant operation kind, such as create, stroke or import.</param>
/// <param name="Source">The host-assigned source, such as manual, ai or system.</param>
/// <param name="Label">An optional user-provided description, not execution evidence.</param>
public sealed record SkinTransactionInfo(string Kind, string Source = "manual", string? Label = null);