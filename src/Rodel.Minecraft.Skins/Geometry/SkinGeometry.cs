// Copyright (c) Richasy. All rights reserved.

using System.Numerics;

namespace Rodel.Minecraft.Skins;

/// <summary>Shares immutable model geometry between ray picking, UV-edge painting and preview rendering.</summary>
public static class SkinGeometry
{
    private static readonly SkinFrame[] Cape = Build(SkinModel.Cape);
    private static readonly SkinFrame[] CapeHd = Build(SkinModel.Cape, 128);
    private static readonly SkinFrame[] Classic = Build(SkinModel.Classic);
    private static readonly SkinFrame[] Slim = Build(SkinModel.Slim);
    private static readonly SkinFrame[] ClassicHd = Build(SkinModel.Classic, 128);
    private static readonly SkinFrame[] SlimHd = Build(SkinModel.Slim, 128);

    /// <summary>Returns independently owned textured quads, including the inflated outer surface.</summary>
    public static SkinQuad[] Quads(SkinModel model, int size = 64) => [.. Frames(model, size).Select(frame => new SkinQuad(frame.Texture,
        [frame.Origin.X, frame.Origin.Y, frame.Origin.Z], [frame.Right.X, frame.Right.Y, frame.Right.Z],
        [frame.Down.X, frame.Down.Y, frame.Down.Z]))];

    /// <summary>Returns the nearest eligible surface hit; a valid ray that misses the model returns null.</summary>
    public static SkinHit? Pick(SkinDocument document, SkinPickRequest request)
    {
        document.Validate();
        return Pick(document.Model, document.Size, request.SkipTransparent ? SkinCompositor.Pixels(document) : null, request);
    }

    internal static SkinHit? Pick(SkinModel model, int size, uint[]? pixels, SkinPickRequest request)
    {
        var origin = Vector(request.Origin);
        var direction = Vector(request.Direction);
        var length = Math.Sqrt(((double)direction.X * direction.X) + ((double)direction.Y * direction.Y) + ((double)direction.Z * direction.Z));
        if (length == 0 || (request.Layer is { } layer && !Enum.IsDefined(layer)))
            throw new ArgumentException("Specify a nonzero ray direction and a valid Minecraft layer.", nameof(request));
        CheckParts(request.Parts);
        direction = new((float)(direction.X / length), (float)(direction.Y / length), (float)(direction.Z / length));
        SkinHit? result = null;
        foreach (var frame in Frames(model, size))
        {
            var surface = frame.Texture.Surface;
            if ((request.Layer is { } selected && selected != surface.Layer)
                || (request.Parts is not null && !request.Parts.Contains(surface.Part)))
            {
                continue;
            }

            var denominator = Vector3.Dot(frame.Normal, direction);
            if (Math.Abs(denominator) < 0.00001f || (request.CullBackfaces && denominator >= 0))
                continue;
            var distance = Vector3.Dot(frame.Origin - origin, frame.Normal) / denominator;
            if (!float.IsFinite(distance) || distance < 0 || (result is not null && distance >= result.Distance))
                continue;
            var position = origin + (direction * distance);
            var local = frame.Local(position);
            if (local.X < -0.5 || local.Y < -0.5 || local.X > frame.Texture.Width - 0.5 || local.Y > frame.Texture.Height - 0.5)
                continue;
            var strokeX = Math.Min(local.X, Math.BitDecrement(frame.Texture.Width - 0.5));
            var strokeY = Math.Min(local.Y, Math.BitDecrement(frame.Texture.Height - 0.5));
            var x = Math.Min((int)Math.Floor(strokeX + 0.5), frame.Texture.Width - 1);
            var y = Math.Min((int)Math.Floor(strokeY + 0.5), frame.Texture.Height - 1);
            if (request.SkipTransparent && pixels is not null && (pixels[((frame.Texture.Y + y) * size) + frame.Texture.X + x] & 255) == 0)
                continue;
            result = new(surface, x, y, strokeX, strokeY, distance,
                new(position.X, position.Y, position.Z), new(frame.Normal.X, frame.Normal.Y, frame.Normal.Z));
        }

        return result;
    }

    internal static SkinFrame[] Frames(SkinModel model, int size = 64)
    {
        SkinLayout.CheckSize(size);
        return (model, size) switch
        {
            (SkinModel.Cape, 64) => Cape,
            (SkinModel.Cape, 128) => CapeHd,
            (SkinModel.Classic, 64) => Classic,
            (SkinModel.Classic, 128) => ClassicHd,
            (SkinModel.Slim, 64) => Slim,
            (SkinModel.Slim, 128) => SlimHd,
            _ => throw new ArgumentOutOfRangeException(nameof(model))
        };
    }

    internal static SkinFrame Frame(SkinModel model, SkinSurface surface, bool logical = false, int size = 64) =>
        Frames(model, size).First(frame => frame.Texture.Surface == (logical ? surface with { Layer = SkinLayer.Base } : surface));

    internal static (SkinSurface Surface, int X, int Y) Wrap(SkinModel model, SkinSurface surface, int x, int y, int size = 64)
    {
        var point = new SkinPoint(x, y);
        for (var step = 0; step < 256; step++)
        {
            var frame = Frame(model, surface, logical: true, size: size);
            var width = frame.Texture.Width;
            var height = frame.Texture.Height;
            var px = point.X + 0.5;
            var py = point.Y + 0.5;
            if (px >= 0 && py >= 0 && px < width && py < height)
                return (surface, (int)Math.Floor(px), (int)Math.Floor(py));
            Vector3 normal;
            double overflow;
            if (px < 0) { normal = -Vector3.Normalize(frame.Right); overflow = -px; px = 0; }
            else if (px >= width) { normal = Vector3.Normalize(frame.Right); overflow = px - width; px = width; }
            else if (py < 0) { normal = -Vector3.Normalize(frame.Down); overflow = -py; py = 0; }
            else { normal = Vector3.Normalize(frame.Down); overflow = py - height; py = height; }

            var next = Frames(model, size).First(candidate => candidate.Texture.Surface.Part == surface.Part
                && candidate.Texture.Surface.Layer == SkinLayer.Base && Vector3.Dot(candidate.Normal, normal) > 0.99f);
            var edge = frame.Origin + (frame.Right * (float)(px / width)) + (frame.Down * (float)(py / height));
            point = next.Local(edge - (frame.Normal * (float)(overflow * 64 / size)));
            surface = surface with { Face = next.Texture.Surface.Face };
        }

        throw new InvalidOperationException("The brush coordinate could not be transported across the cuboid.");
    }

    internal static SkinPoint? Unfold(SkinModel model, SkinSurface from, SkinSurface to, double x, double y, int size = 64)
    {
        if (from.Part != to.Part || from.Layer != to.Layer)
            return null;
        if (from.Face == to.Face)
            return new(x, y);
        var first = Frame(model, from, logical: true, size: size);
        var second = Frame(model, to, logical: true, size: size);
        if (Math.Abs(Vector3.Dot(first.Normal, second.Normal)) > 0.01f)
            return null;
        var world = second.Point(x, y);
        var distance = Vector3.Dot(first.Origin - world, first.Normal);
        return first.Local(world + (first.Normal * distance) + (second.Normal * distance));
    }

    internal static SkinSurface Mirror(SkinSurface surface, SkinMirrorMode mode) => surface with
    {
        Part = mode == SkinMirrorMode.Part ? surface.Part : surface.Part switch
        {
            SkinPart.LeftArm => SkinPart.RightArm,
            SkinPart.RightArm => SkinPart.LeftArm,
            SkinPart.LeftLeg => SkinPart.RightLeg,
            SkinPart.RightLeg => SkinPart.LeftLeg,
            _ => surface.Part
        },
        Face = surface.Face switch { SkinFace.Right => SkinFace.Left, SkinFace.Left => SkinFace.Right, _ => surface.Face }
    };

    internal static (SkinSurface Surface, int X, int Y) MirrorPoint(
        SkinModel model, SkinSurface surface, int x, int y, SkinMirrorMode mode, SkinPart[]? parts, int size)
    {
        var target = Mirror(surface, mode);
        var face = SkinLayout.Face(model, surface, size);
        if (target.Part == surface.Part || parts is null || parts.Contains(target.Part))
            return (target, face.Width - 1 - x, y);

        target = surface with
        {
            Face = surface.Face switch
            {
                SkinFace.Front => SkinFace.Back,
                SkinFace.Back => SkinFace.Front,
                SkinFace.Right => SkinFace.Left,
                SkinFace.Left => SkinFace.Right,
                SkinFace.Top => SkinFace.Bottom,
                SkinFace.Bottom => SkinFace.Top,
                _ => throw new ArgumentOutOfRangeException(nameof(surface))
            }
        };
        // Reflect through the cuboid along the addressed face normal. Top/bottom UVs reverse
        // their depth axis (Y), while the four upright faces reverse their horizontal axis (X).
        return surface.Face is SkinFace.Top or SkinFace.Bottom
            ? (target, x, face.Height - 1 - y)
            : (target, face.Width - 1 - x, y);
    }

    internal static void CheckView(SkinViewOptions options)
    {
        if (options.Size is < 64 or > 512 || !float.IsFinite(options.Yaw) || !float.IsFinite(options.Pitch) || options.Pitch is < -89 or > 89)
            throw new ArgumentException("Preview size must be 64 through 512, with finite yaw and pitch from -89 through 89.", nameof(options));
        CheckParts(options.Parts);
    }

    internal static bool Visible(SkinSurface surface, SkinViewOptions options) =>
        (surface.Layer == SkinLayer.Base ? options.BaseVisible : options.OuterVisible)
        && (options.Parts is null || options.Parts.Contains(surface.Part));

    private static void CheckParts(SkinPart[]? parts)
    {
        if (parts is not null && parts.Any(part => !Enum.IsDefined(part)))
            throw new ArgumentException("Unknown preview body part.", nameof(parts));
    }

    private static Vector3 Vector(SkinVector vector)
    {
        if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z))
            throw new ArgumentException("Ray coordinates must be finite.", nameof(vector));
        return new(vector.X, vector.Y, vector.Z);
    }

    private static SkinFrame[] Build(SkinModel model, int textureSize = 64) => [.. SkinLayout.Faces(model, textureSize).Select(texture =>
    {
        var width = model == SkinModel.Slim ? 3f : 4f;
        var (min, max) = texture.Surface.Part switch
        {
            SkinPart.Cape => (new Vector3(-5, 8, -3.5f), new Vector3(5, 24, -2.5f)),
            SkinPart.Head => (new Vector3(-4, 24, -4), new Vector3(4, 32, 4)),
            SkinPart.Body => (new Vector3(-4, 12, -2), new Vector3(4, 24, 2)),
            SkinPart.RightArm => (new Vector3(-4 - width, 12, -2), new Vector3(-4, 24, 2)),
            SkinPart.LeftArm => (new Vector3(4, 12, -2), new Vector3(4 + width, 24, 2)),
            SkinPart.RightLeg => (new Vector3(-4, 0, -2), new Vector3(0, 12, 2)),
            SkinPart.LeftLeg => (new Vector3(0, 0, -2), new Vector3(4, 12, 2)),
            _ => throw new ArgumentOutOfRangeException(nameof(model))
        };
        if (texture.Surface.Layer == SkinLayer.Outer)
        {
            var inflate = texture.Surface.Part == SkinPart.Head ? 0.5f : 0.25f;
            min -= new Vector3(inflate);
            max += new Vector3(inflate);
        }

        var size = max - min;
        var physicalFace = model == SkinModel.Cape ? texture.Surface.Face switch
        {
            SkinFace.Front => SkinFace.Back, SkinFace.Back => SkinFace.Front,
            SkinFace.Left => SkinFace.Right, SkinFace.Right => SkinFace.Left, _ => texture.Surface.Face
        } : texture.Surface.Face;
        var (origin, right, down) = physicalFace switch
        {
            SkinFace.Front => (new Vector3(min.X, max.Y, max.Z), new Vector3(size.X, 0, 0), new Vector3(0, -size.Y, 0)),
            SkinFace.Back => (new Vector3(max.X, max.Y, min.Z), new Vector3(-size.X, 0, 0), new Vector3(0, -size.Y, 0)),
            SkinFace.Right => (new Vector3(min.X, max.Y, min.Z), new Vector3(0, 0, size.Z), new Vector3(0, -size.Y, 0)),
            SkinFace.Left => (new Vector3(max.X, max.Y, max.Z), new Vector3(0, 0, -size.Z), new Vector3(0, -size.Y, 0)),
            SkinFace.Top => (new Vector3(min.X, max.Y, min.Z), new Vector3(size.X, 0, 0), new Vector3(0, 0, size.Z)),
            SkinFace.Bottom => (new Vector3(min.X, min.Y, max.Z), new Vector3(size.X, 0, 0), new Vector3(0, 0, -size.Z)),
            _ => throw new ArgumentOutOfRangeException(nameof(model))
        };
        return new SkinFrame(texture, origin, right, down);
    })];
}