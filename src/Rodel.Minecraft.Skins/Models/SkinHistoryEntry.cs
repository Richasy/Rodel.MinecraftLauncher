// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Describes a retained history position without copying its texture buffers.</summary>
/// <param name="Index">The timeline position, valid at the observed document revision.</param>
/// <param name="Current">Whether this position is the current content.</param>
/// <param name="Model">The state's arm geometry.</param>
/// <param name="Size">The state's texture dimension.</param>
/// <param name="Transaction">The operation that produced this content.</param>
public sealed record SkinHistoryEntry(int Index, bool Current, SkinModel Model, int Size, SkinTransactionInfo Transaction);