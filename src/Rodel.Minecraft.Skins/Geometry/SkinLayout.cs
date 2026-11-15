// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Provides the 64/128 texel atlas mapping shared by editing and rendering.</summary>
public static class SkinLayout
{
    /// <summary>Gets the supported texture widths shared by validation and host schemas.</summary>
    public static int[] SupportedSizes => [64, 128];

    /// <summary>Returns the actual texture height: square for characters and half-width for capes.</summary>
    public static int Height(SkinModel model, int size) => model == SkinModel.Cape ? size / 2 : size;

    /// <summary>Returns all valid face rectangles, including the separately mapped outer layer.</summary>
    public static SkinFaceInfo[] Faces(SkinModel model, int size = 64)
    {
        if (!Enum.IsDefined(model))
            throw new ArgumentOutOfRangeException(nameof(model));
        if (model == SkinModel.Cape) return [.. Enum.GetValues<SkinFace>().Select(face => Face(model, new() { Part = SkinPart.Cape, Layer = SkinLayer.Base, Face = face }, size))];
        return [.. Enum.GetValues<SkinPart>().Where(part => part != SkinPart.Cape).SelectMany(part =>
            Enum.GetValues<SkinLayer>().SelectMany(layer =>
                Enum.GetValues<SkinFace>().Select(face => Face(model, new() { Part = part, Layer = layer, Face = face }, size))))];
    }

    /// <summary>Resolves a semantic face to its exact atlas rectangle without guessing model or layer.</summary>
    public static SkinFaceInfo Face(SkinModel model, SkinSurface surface, int size = 64)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (!Enum.IsDefined(model) || !Enum.IsDefined(surface.Part) || !Enum.IsDefined(surface.Layer) || !Enum.IsDefined(surface.Face))
            throw new ArgumentException("Unknown skin model, part, layer or face.");
        CheckSize(size);
        var scale = size / 64;
        if (model == SkinModel.Cape != (surface.Part == SkinPart.Cape) || (model == SkinModel.Cape && surface.Layer != SkinLayer.Base))
            throw new ArgumentException("Choose a face belonging to the current character or cape model.", nameof(surface));
        var outer = surface.Layer == SkinLayer.Outer;
        var (u, v, w, h, d) = surface.Part switch
        {
            SkinPart.Cape => (0, 0, 10, 16, 1),
            SkinPart.Head => (outer ? 32 : 0, 0, 8, 8, 8),
            SkinPart.Body => (16, outer ? 32 : 16, 8, 12, 4),
            SkinPart.RightArm => (40, outer ? 32 : 16, model == SkinModel.Slim ? 3 : 4, 12, 4),
            SkinPart.LeftArm => (outer ? 48 : 32, 48, model == SkinModel.Slim ? 3 : 4, 12, 4),
            SkinPart.RightLeg => (0, outer ? 32 : 16, 4, 12, 4),
            SkinPart.LeftLeg => (outer ? 0 : 16, 48, 4, 12, 4),
            _ => throw new ArgumentOutOfRangeException(nameof(surface))
        };
        var (x, y, width, height) = surface.Face switch
        {
            SkinFace.Top => (u + d, v, w, d),
            SkinFace.Bottom => (u + d + w, v, w, d),
            SkinFace.Right => (u, v + d, d, h),
            SkinFace.Front => (u + d, v + d, w, h),
            SkinFace.Left => (u + d + w, v + d, d, h),
            SkinFace.Back => (u + d + w + d, v + d, w, h),
            _ => throw new ArgumentOutOfRangeException(nameof(surface))
        };
        return new(surface, x * scale, y * scale, width * scale, height * scale);
    }

    internal static void CheckSize(int size)
    {
        if (!SupportedSizes.Contains(size))
            throw new ArgumentOutOfRangeException(nameof(size), "Skin size must be 64 or 128.");
    }
}