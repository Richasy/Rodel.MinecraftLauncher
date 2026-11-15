// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Reactive;

namespace Rodel.MinecraftLauncher.App.Features.Wallpapers;

/// <summary>Samples the caption's cover-mapped scene through the window's bounded image decoder.</summary>
internal sealed class CaptionBackgroundSampler(ImageLoadServices services, Func<WallpaperItem> wallpaper) : IDisposable
{
    internal static void Attach(Window window, Func<bool> isHome, Func<WallpaperItem> wallpaper)
    {
        var sampler = new CaptionBackgroundSampler(window.Images, wallpaper);
        var effect = new Effect(() =>
            {
                sampler.Request(window, isHome());
                window.Colors = window.Colors with
                {
                    ContrastBackground = sampler.Background.Value
                };
            });
        void OnLayout() => sampler.Request(window, isHome());
        window.LayoutUpdated += OnLayout;
        window.Closed += (_, _) =>
        {
            window.LayoutUpdated -= OnLayout;
            effect.Dispose();
            sampler.Dispose();
        };
    }
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<ImageSource, Task<DecodedImage>> _images = [];
    private CancellationTokenSource? _pending;
    private SampleRequest? _last;
    internal Signal<Color?> Background { get; } = new(null);

    internal void Request(Window window, bool home)
    {
        using var theme = Sprout.Theming.ThemeScope.Push(window.Theme);
        if (window.Theme.Colors.HighContrast is not null)
        {
            _pending?.Cancel();
            _last = null;
            return;
        }

        var item = wallpaper();
        var request = new SampleRequest(
            item.Source,
            item.Alignment,
            LauncherTheme.Light,
            home,
            window.FlowDirection == FlowDirection.RightToLeft,
            window.ClientSize,
            window.TitleBarHeight.Epx,
            LauncherMaterials.Shade,
            LauncherMaterials.HorizontalShade,
            LauncherTheme.PageShade);
        if (_last == request || request.Size.Width <= 0 || request.Size.Height <= 0)
            return;
        _last = request;
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _ = SampleAsync(request, _pending.Token);
    }

    private async Task SampleAsync(SampleRequest request, CancellationToken cancellation)
    {
        try
        {
            await Task.Delay(120, cancellation);
            if (!_images.TryGetValue(request.Source, out var image))
            {
                image = ImageColorAnalysis.DecodeSampleAsync(request.Source, services, new ImagePixelSize(256, 256), _lifetime.Token);
                if (_images.Count >= 2)
                    _images.Remove(_images.Keys.First());
                _images.Add(request.Source, image);
            }

            var sample = await image.WaitAsync(cancellation);
            var background = await Task.Run(() => Analyze(sample, request), cancellation);
            services.Post(() =>
                {
                    if (!cancellation.IsCancellationRequested && _last == request)
                        Background.Value = background;
                });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || _lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            System.Diagnostics.Trace.TraceWarning("Caption background sampling failed: {0}", exception);
            services.Post(() =>
                {
                    if (!cancellation.IsCancellationRequested)
                        Background.Value = null;
                });
        }
    }

    private static Color Analyze(DecodedImage image, SampleRequest request)
    {
        var box = Rect.FromXywh(0, 0, request.Size.Width, request.Size.Height);
        var scale = Math.Max(box.Width / image.Width, box.Height / image.Height);
        var destination = Rect.FromXywh((box.Width - (image.Width * scale)) * request.Alignment.Horizontal, (box.Height - (image.Height * scale)) * request.Alignment.Vertical, image.Width * scale, image.Height * scale);
        var captionWidth = Math.Min(138f, box.Width);
        var left = request.Rtl ? 0 : box.Width - captionWidth;
        var region = Rect.FromXywh((left - destination.Left) * image.Width / destination.Width, -destination.Top * image.Height / destination.Height, captionWidth * image.Width / destination.Width, request.Height * image.Height / destination.Height);
        var color = ImageColorAnalysis.Analyze(image, region).AverageColor;
        var x = (left + (captionWidth / 2f)) / box.Width;
        var y = request.Height / 2f / box.Height;
        if (request.Home)
        {
            double red = color.R, green = color.G, blue = color.B;
            var axis = ((x * box.Width * .8660254) + ((1 - y) * box.Height * .5)) / ((box.Width * .8660254) + (box.Height * .5));
            WallpaperScenePolicy.Composite(
                request.Light,
                axis,
                ref red,
                ref green,
                ref blue);
            color = new Color((byte)Math.Round(red), (byte)Math.Round(green), (byte)Math.Round(blue), 255);
        }
        else
        {
            color = Over(color, Gradient(request.Shade, x, y));
            color = Over(color, Gradient(request.HorizontalShade, x, y));
            color = Over(color, request.PageShade);
        }

        return color;
    }

    private static Color Gradient(Brush brush, float x, float y)
    {
        if (brush.AsLinear is not { } gradient)
            return brush.AsSolid ?? Colors.Transparent;
        var dx = gradient.End.X - gradient.Start.X;
        var dy = gradient.End.Y - gradient.Start.Y;
        var offset = Math.Clamp((((x - gradient.Start.X) * dx) + ((y - gradient.Start.Y) * dy)) / ((dx * dx) + (dy * dy)), 0f, 1f);
        var stops = gradient.Stops;
        for (var i = 1; i < stops.Count; i++)
        {
            if (offset <= stops[i].Offset)
            {
                return Mix(stops[i - 1].Color.Literal ?? Colors.Transparent, stops[i].Color.Literal ?? Colors.Transparent, (offset - stops[i - 1].Offset) / (stops[i].Offset - stops[i - 1].Offset));
            }
        }

        return stops[^1].Color.Literal ?? Colors.Transparent;
    }

    internal static Color Mix(Color source, Color target, float amount) => ColorMath.Lerp(source, target, amount);

    private static Color Over(Color background, Color overlay) => Mix(background, overlay with { A = 255 }, overlay.A / 255f);

    /// <summary>Cancels pending analysis and releases this window's sampling lifetime.</summary>
    public void Dispose()
    {
        _lifetime.Cancel();
        _pending?.Cancel();
        _pending?.Dispose();
        _lifetime.Dispose();
    }

    private sealed record SampleRequest(
        ImageSource Source,
        ImageAlignment Alignment,
        bool Light,
        bool Home,
        bool Rtl,
        Size Size,
        float Height,
        Brush Shade,
        Brush HorizontalShade,
        Color PageShade);
}