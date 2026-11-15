// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;

namespace Rodel.MinecraftLauncher.App.Features.Content;

/// <summary>Loads optional candidate icons through the window's shared cache for native combo rows that build static widgets.</summary>
internal sealed class ContentUpdateSourceImages(Action changed) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<string> _requested = [with(StringComparer.Ordinal)];
    private readonly Dictionary<string, ImageSource> _loaded = [with(StringComparer.Ordinal)];
    internal IReadOnlyDictionary<string, ImageSource> Snapshot { get; private set; } = new Dictionary<string, ImageSource>();

    internal void Request(IEnumerable<string?> urls, ImageLoadServices? images)
    {
        if (images?.Loader is null || _lifetime.IsCancellationRequested) return;
        var pending = urls.Where(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            .Select(url => url!).Where(_requested.Add).ToArray();
        if (pending.Length > 0) _ = LoadAsync(pending, images, _lifetime.Token);
    }

    private async Task LoadAsync(string[] urls, ImageLoadServices images, CancellationToken ct)
    {
        try
        {
            await Parallel.ForEachAsync(urls, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (url, token) =>
            {
                try
                {
                    var source = ImageSource.FromUri(new Uri(url), decodePixelWidth: 128, decodePixelHeight: 128, cacheBucket: "content-icons");
                    var pixels = await images.Loader!.LoadAsync(source, new(128, 128), images.Decoder, token).ConfigureAwait(false);
                    var decoded = ImageSource.FromPixels(pixels);
                    images.Post(() =>
                    {
                        if (ct.IsCancellationRequested) return;
                        _loaded[url] = decoded;
                        Snapshot = new Dictionary<string, ImageSource>(_loaded, StringComparer.Ordinal);
                        changed();
                    });
                }
                catch (Exception)
                {
                    // A failed or cancelled optional icon retains the placeholder and never blocks source selection.
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    /// <summary>Cancels pending icon reads and prevents late UI callbacks after the update popup is disposed.</summary>
    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}