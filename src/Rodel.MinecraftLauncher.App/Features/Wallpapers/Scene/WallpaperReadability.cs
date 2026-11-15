// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;

namespace Rodel.MinecraftLauncher.App.Features.Wallpapers;

/// <summary>One in-flight job, one latest layout, a bounded image cache; never scans pixels during layout.</summary>
internal sealed class WallpaperReadability(WallpaperService service) : IDisposable
{
    private readonly List<WallpaperText> _texts = [];
    private readonly LinkedList<(string Path, WallpaperSample Sample)> _cache = [];
    private readonly CancellationTokenSource _lifetime = new();
    private DispatcherQueueTimer? _timer;
    private bool _busy, _dirty, _disposed;
    private int _generation, _signature;
    internal bool Active { get; set; } = true;

    internal void Register(WallpaperText text)
    {
        _texts.Add(text);
        Invalidate();
    }

    internal void Unregister(WallpaperText text)
    {
        _texts.Remove(text);
        Invalidate();
    }

    internal void Attach()
    {
        _timer = DispatcherQueue.TryGetForCurrentThread()!.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(120);
        _timer.IsRepeating = false;
        _timer.Tick += OnTick;
        service.Window.LayoutUpdated += OnLayout;
        service.Window.ActualThemeChanged += OnTheme;
        Invalidate();
    }

    private void OnTheme(object? sender, EventArgs e) => Invalidate();

    internal void Invalidate()
    {
        _signature = 0;
        _generation++;
        _dirty = true;
        if (!_busy && _timer?.IsRunning == false)
            _timer.Start();
    }

    private bool CanAnalyze => Active && service.Enabled && service.Window.IsVisible && !_disposed;

    private void OnLayout()
    {
        var hash = new HashCode();
        hash.Add(Active);
        hash.Add(service.Enabled);
        hash.Add(service.Current.Path);
        hash.Add(service.Window.ClientSize);
        foreach (var text in _texts)
        {
            if (text.Widget is { } widget)
            {
                hash.Add(widget.Bounds);
                hash.Add(widget.Text);
                hash.Add(widget.Style);
                hash.Add(text.Ink);
            }
        }

        var signature = hash.ToHashCode();
        if (signature == _signature)
            return;
        _signature = signature;
        _generation++;
        _dirty = true;
        if (!CanAnalyze)
        {
            foreach (var text in _texts)
                text.Paint = default;
            return;
        }

        if (!_busy && _timer?.IsRunning == false)
            _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_busy && _dirty && CanAnalyze)
            _ = RunAsync();
    }

    private async Task RunAsync()
    {
        if (service.Images is not { } images)
            return;
        _busy = true;
        _dirty = false;
        var generation = _generation;
        var item = service.Current;
        var viewport = service.Window.ClientSize;
        var background = LauncherTheme.Light ? Color.FromRgb(228, 212, 172) : Color.FromRgb(114, 131, 125);
        var light = LauncherTheme.Light;
        var texts = _texts.Where(text => text.Widget is not null).Take(16).ToArray();
        var regions = texts.Select(text =>
            {
                var widget = text.Widget!;
                var content = new TextContent(widget.Text, default, widget.Style, widget.Foreground);
                List<Rect> rects = [];
                for (var index = 0; index < Math.Min(192, widget.Text.Length); index++)
                {
                    if (char.IsWhiteSpace(widget.Text[index]))
                        continue;
                    foreach (var rect in service.Window.TextHitTester.HitTestRange(content, widget.Bounds, index, 1))
                    {
                        // The hit tester uses the same DWrite layout, including trimming and bidi.
                        var clipped = Rect.FromXywh(Math.Max(rect.Left, widget.Bounds.Left), Math.Max(rect.Top, widget.Bounds.Top), Math.Max(0, Math.Min(rect.Right, widget.Bounds.Right) - Math.Max(rect.Left, widget.Bounds.Left)), Math.Max(0, Math.Min(rect.Bottom, widget.Bounds.Bottom) - Math.Max(rect.Top, widget.Bounds.Top)));
                        if (!clipped.IsEmpty)
                            rects.Add(clipped);
                    }
                }

                return new WallpaperTextRegion([.. rects], text.Ink, widget.Style.FontSize >= 24f, text.Paint.IsVisible);
            }).ToArray();
        try
        {
            var result = await Task.Run(() =>
                {
                    _lifetime.Token.ThrowIfCancellationRequested();
                    var cached = _cache.FirstOrDefault(entry => entry.Path == item.Path);
                    var sample = cached.Sample;
                    if (sample is null)
                    {
                        var decoded = images.Decoder.Decode(ImageSource.FromFile(item.Path), new(768, 768));
                        sample = new(decoded.Width, decoded.Height, decoded.Pixels);
                        _cache.AddLast((item.Path, sample));
                        while (_cache.Count > 2)
                            _cache.RemoveFirst();
                    }
                    else
                    {
                        _cache.Remove(cached);
                        _cache.AddLast(cached);
                    }

                    var blurred = WallpaperScenePolicy.Blur(sample, viewport.Width, viewport.Height, _lifetime.Token);
                    var budget = Math.Max(9, 65536 / Math.Max(1, regions.Sum(region => region.Characters.Length)));
                    var paints = regions.Select(region => WallpaperContrast.Analyze(
                            sample,
                            viewport,
                            item.Alignment,
                            region,
                            background,
                            budget,
                            light,
                            blurred)).ToArray();
                    return paints;
                }, _lifetime.Token);
            if (!_disposed && generation == _generation && CanAnalyze)
            {
                for (var index = 0; index < texts.Length; index++)
                {
                    if (_texts.Contains(texts[index]))
                        texts[index].Paint = result[index];
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (!_disposed && generation == _generation && CanAnalyze)
            {
                foreach (var text in texts)
                    text.Paint = new(LauncherTheme.Light ? Color.FromArgb(77, 255, 255, 255) : Color.FromArgb(77, 0, 0, 0), 1.4f);
            }
        }
        finally
        {
            _busy = false;
            if (_dirty && CanAnalyze)
                _timer?.Start();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _timer?.Stop();
        _timer?.Tick -= OnTick;
        service.Window.LayoutUpdated -= OnLayout;
        service.Window.ActualThemeChanged -= OnTheme;
        _lifetime.Dispose();
    }
}