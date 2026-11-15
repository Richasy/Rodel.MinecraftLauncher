// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Stores a complete undoable document state without a second revision counter.</summary>
/// <param name="Pixels">The background pixels.</param>
/// <param name="Layers">Paint layers in bottom-to-top order.</param>
/// <param name="Model">The geometry interpreting this snapshot.</param>
/// <param name="Size">The texture width.</param>
/// <param name="BackgroundName">The display name of the protected background.</param>
/// <param name="Transaction">The transaction that produced this state.</param>
/// <param name="TransparencyMask">The optional atlas mask that hides composite pixels.</param>
public sealed record SkinSnapshot(uint[] Pixels, SkinPaintLayer[] Layers, SkinModel Model,
    int Size, string BackgroundName, SkinTransactionInfo Transaction,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] byte[]? TransparencyMask = null);