// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Sprout.Graphics;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Rodel.MinecraftLauncher.App.Features.Skins.Rendering;

/// <summary>Retains one canvas and sends the bounded skin texels and pose directly as shader constants.</summary>
internal sealed class SkinPreviewEffect : IDisposable
{
    private const int CanvasEdge = 2048;
    private const int PixelOffset = SkinShaderScene.FloatCount + (14 * 4);
    private static readonly Guid ShaderId = new("6ad0dfc7-c2ef-4425-b6c3-1f3238866fea");
    private static readonly byte[] Bytecode = LoadShader();
    private static readonly Lock CanvasGate = new();
    private static ImageHandle? _canvas;
    private static int _canvasOwners;
    private bool _disposed;
    // Classic128 has the largest mapped surface: 14544 RGBA texels. Geometry plus pixels stays below D2D's 64 KiB limit.
    private readonly byte[] _scene = new byte[(PixelOffset + 14544) * sizeof(float)];
    private readonly Vector3[] _corners = new Vector3[112];

    internal SkinPreviewEffect(SkinRenderSnapshot primary, SkinRenderSnapshot? companion = null)
    {
        var snapshot = new SkinSceneSnapshot(primary, companion);
        var geometry = new float[SkinShaderScene.FloatCount];
        SkinShaderScene.WriteGeometry(snapshot, geometry, _corners);
        var cursor = 0;
        var pixels = snapshot.Pixels.Span;
        foreach (var quad in snapshot.Quads())
        {
            var rect = quad.Texture;
            var face = ((((int)rect.Surface.Part * 2) + (int)rect.Surface.Layer) * 6) + SkinShaderScene.FaceIndex(rect.Surface);
            geometry[144 + (face * 16) + 3] = cursor;
            for (var y = 0; y < rect.Height; y++)
            {
                for (var x = 0; x < rect.Width; x++)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(_scene.AsSpan((PixelOffset + cursor++) * sizeof(float), sizeof(float)), pixels[((rect.Y + y) * snapshot.Width) + rect.X + x]);
                }
            }
        }

        MemoryMarshal.AsBytes(geometry.AsSpan()).CopyTo(_scene);
        lock (CanvasGate)
        {
            _canvas ??= CreateCanvas();
            Surface = _canvas;
            _canvasOwners++;
        }
    }

    internal ImageHandle Surface { get; }

    /// <summary>Releases the shared preview canvas when the last open preview ends.</summary>
    public void Dispose()
    {
        ImageHandle? retired = null;
        lock (CanvasGate)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (--_canvasOwners == 0)
            {
                retired = _canvas;
                _canvas = null;
            }
        }

        retired?.Dispose();
    }

    internal CustomImageEffect Create(SkinGpuFrame frame)
    {
        var bytes = (byte[])_scene.Clone();
        void Vector(int offset, Vector4 value) => MemoryMarshal.Cast<byte, Vector4>(bytes.AsSpan(offset * sizeof(float), 16))[0] = value;
        MemoryMarshal.Cast<byte, Matrix4x4>(bytes.AsSpan(0, 64))[0] = frame.Camera.Inverse;
        Vector(16, new(frame.Camera.Eye, frame.Camera.Width));
        Vector(20, new(frame.Camera.Width, frame.Camera.Height, 0, frame.Camera.Height));
        Vector(24, frame.Background);
        Vector(28, new(frame.Texture.Size, 0, frame.Parts, frame.Outer ? 1 : 0));
        Vector(1488, new(CanvasEdge, CanvasEdge, 0, 0));
        Vector(1492, new(0, 0, frame.Pose is null ? 0 : 1, 1));
        Vector(1496, SkinShaderScene.ProjectBounds(
                frame,
                _corners,
                frame.Camera.Width,
                frame.Camera.Height,
                MemoryMarshal.Cast<byte, Vector4>(bytes.AsSpan(SkinShaderScene.FloatCount * sizeof(float), 14 * 16))));
        for (var part = 0; part < 7; part++)
        {
            Matrix4x4.Invert(frame.Pose is { } pose && part < pose.Parts.Length ? pose.Parts[part] : Matrix4x4.Identity, out var inverse);
            MemoryMarshal.Cast<byte, Matrix4x4>(bytes.AsSpan((1500 + (part * 16)) * sizeof(float), 64))[0] = inverse;
        }

        return new(ShaderId, Bytecode, bytes, "Skin walking preview");
    }

    private static byte[] LoadShader()
    {
        using var stream = typeof(SkinPreviewEffect).Assembly.GetManifestResourceStream("SkinShaders.Preview")!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static ImageHandle CreateCanvas()
    {
        var pixels = new byte[CanvasEdge * CanvasEdge * 4];
        for (var offset = 3; offset < pixels.Length; offset += 4)
            pixels[offset] = 255;
        return ImageHandle.FromDecoded(new(CanvasEdge, CanvasEdge, pixels));
    }
}