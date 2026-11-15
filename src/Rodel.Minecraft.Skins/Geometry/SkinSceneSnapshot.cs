// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Packs one editable work and an optional opposite-kind reference for rendering, independently of either saved document.</summary>
public sealed class SkinSceneSnapshot
{
    private readonly SkinQuad[] _quads;
    private readonly uint[] _pixels;

    /// <summary>Creates a frozen scene without resizing, compositing or changing either work's original texture.</summary>
    public SkinSceneSnapshot(SkinRenderSnapshot primary, SkinRenderSnapshot? companion = null)
    {
        if (companion is not null && primary.Model == SkinModel.Cape == (companion.Model == SkinModel.Cape))
            throw new ArgumentException("Pair a character skin with a cape, not two works of the same kind.", nameof(companion));
        Width = primary.Size + (companion?.Size ?? 0);
        Height = Math.Max(primary.Height, companion?.Height ?? 0);
        _pixels = new uint[Width * Height];
        Copy(primary, 0);
        if (companion is not null) Copy(companion, primary.Size);
        _quads = [.. primary.Quads(), .. companion?.Quads().Select(quad => quad with { Texture = quad.Texture with { X = quad.Texture.X + primary.Size } }) ?? []];

        void Copy(SkinRenderSnapshot snapshot, int left)
        {
            for (var y = 0; y < snapshot.Height; y++)
                snapshot.Pixels.Span.Slice(y * snapshot.Size, snapshot.Size).CopyTo(_pixels.AsSpan((y * Width) + left));
        }
    }

    /// <summary>Gets the packed rendering atlas width; it is never a saved work's export dimension.</summary>
    public int Width { get; }
    /// <summary>Gets the packed rendering atlas height.</summary>
    public int Height { get; }
    /// <summary>Gets borrowed immutable scene pixels, retaining both sources' exact RGBA.</summary>
    public ReadOnlyMemory<uint> Pixels => _pixels;
    /// <summary>Returns independently owned character and cape faces referencing the packed scene atlas.</summary>
    public SkinQuad[] Quads() => [.. _quads.Select(quad => quad with { Origin = [.. quad.Origin], Right = [.. quad.Right], Down = [.. quad.Down] })];

    /// <summary>Picks only the editable work, refusing a hit hidden behind an opaque reference surface.</summary>
    public static SkinHit? Pick(SkinRenderSnapshot primary, SkinRenderSnapshot? companion, SkinPickRequest request, bool companionOuterVisible = true)
    {
        var hit = primary.Pick(request);
        if (hit is null || companion is null) return hit;
        var blocker = companion.Pick(request with { Parts = null, Layer = companionOuterVisible ? null : SkinLayer.Base, SkipTransparent = true });
        return blocker is not null && blocker.Distance < hit.Distance ? null : hit;
    }
}