// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Graphics;
using Sprout.Reconcile;
using System.Runtime.InteropServices;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Wallpapers;

/// <summary>Retains one image handle and its GPU blur/crossfade graph across layout changes.</summary>
internal sealed partial class WallpaperScene : Control
{
    private static readonly byte[] Shader = LoadShader();
    private readonly WallpaperService _service;
    private ImageHandle? _handle;
    private ImageSource? _source;
    private ImageEffect _effect;
    private (Size Source, Size Viewport, ImageAlignment Alignment, bool Light) _key;
    private bool _disposed;

    internal WallpaperScene(
        WallpaperService service,
        ImageSource source,
        Size viewport,
        ImageAlignment alignment,
        bool light)
    {
        _service = service;
        Source = source;
        Viewport = viewport;
        Alignment = alignment;
        Light = light;
    }

    /// <summary>Gets or sets the wallpaper source whose image handle is retained until the source changes.</summary>
    public partial ImageSource Source { get; set; }
    /// <summary>Gets or sets the logical viewport used to fit the wallpaper and its effect graph.</summary>
    public partial Size Viewport { get; set; }
    /// <summary>Gets or sets the crop alignment within the available viewport.</summary>
    public partial ImageAlignment Alignment { get; set; }
    /// <summary>Gets or sets whether the scene uses the light-theme image treatment.</summary>
    public partial bool Light { get; set; }
    /// <summary>Gets or sets the reactive revision advanced when retained image loading changes.</summary>
    public partial int Revision { get; set; }

    private ImageView Body
    {
        get
        {
            _ = Revision;
            var handle = RetainedHandle();
            var image = Image(handle)
                .Stretch(ImageStretch.UniformToFill)
                .Alignment(Alignment);
            var pixels = handle.NaturalSize;
            var size = new Size(pixels.Width, pixels.Height);
            if (size.Width <= 0 || size.Height <= 0 || Viewport.Width <= 0 || Viewport.Height <= 0)
                return image;
            var key = (size, Viewport, Alignment, Light);
            if (key != _key)
            {
                _key = key;
                var scale = Math.Max(Viewport.Width / size.Width, Viewport.Height / size.Height);
                var left = (Viewport.Width - (size.Width * scale)) * Alignment.Horizontal;
                var top = (Viewport.Height - (size.Height * scale)) * Alignment.Vertical;
                float[] constants = [scale, left, top, Light ? 1f : 0f, Viewport.Width, Viewport.Height, (Viewport.Width * .8660254038f) + (Viewport.Height * .5f), 0f, Light ? 228f / 255f : 114f / 255f, Light ? 212f / 255f : 131f / 255f, Light ? 172f / 255f : 125f / 255f, 1f];
                var shader = new CustomImageEffect(
                    new Guid("5d51454b-e792-4e58-a516-980a4ff3ef64"),
                    Shader,
                    MemoryMarshal.AsBytes(constants.AsSpan())
                        .ToArray(),
                    "Launcher.WallpaperScene",
                    inputCount: 2);
                _effect = ImageEffect.PipelineEffect(ImageEffectPipeline.Create(graph =>
                        {
                            var clamped = graph.Border(graph.Source, ImageEffectBorderMode.Clamp, ImageEffectBorderMode.Clamp);
                            var blurred = graph.Blur(clamped, 10f / scale);
                            return graph.CustomShader(shader, graph.Source, blurred);
                        }));
            }

            return image.Effect(_effect);
        }
    }

    private ImageHandle RetainedHandle()
    {
        if (_handle is null || _source?.Equals(Source) != true)
        {
            Release();
            _source = Source;
            _handle = new(Source);
            _handle.Changed += ImageChanged;
        }

        return _handle;
    }

    private static byte[] LoadShader()
    {
        using var stream = typeof(WallpaperScene).Assembly.GetManifestResourceStream("WallpaperScene.cso")!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private void ImageChanged() => _service.Images?.Post(() =>
        {
            if (!_disposed)
                Revision++;
        });

    private void Release()
    {
        _handle?.Changed -= ImageChanged;
        _handle = null;
        _key = default;
    }

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        var next = (WallpaperScene)source;
        Source = next.Source;
        Viewport = next.Viewport;
        Alignment = next.Alignment;
        Light = next.Light;
    }

    protected override void OnDispose()
    {
        _disposed = true;
        Release();
        base.OnDispose();
    }
}