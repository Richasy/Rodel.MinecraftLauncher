// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Downloads;

/// <summary>Shared clients follow the global data-root profile; injected clients remain authoritative.</summary>
public static class NetworkSettings
{
    private static readonly ConcurrentDictionary<string, HttpClient> Clients = new();
    /// <summary>Reads a detached global network profile, or an empty profile when no settings file exists.</summary>
    public static JsonObject Read(LauncherPaths paths)
    {
        if (!File.Exists(paths.SettingsFile))
            return [];
        return JsonFiles.ReadObject(paths.SettingsFile)["network"]?.DeepClone() as JsonObject ?? [];
    }

    /// <summary>Resolves the current transfer limit within 1–64, using the caller's default when unspecified.</summary>
    public static int Concurrency(LauncherPaths paths, int fallback) => Number(Read(paths), "concurrency", fallback, 1, 64);
    /// <summary>Resolves the allowed interval without incoming download bytes, defaulting to 120 seconds.</summary>
    public static TimeSpan IdleTimeout(LauncherPaths paths) => TimeSpan.FromSeconds(Number(Read(paths), "readIdleSeconds", 120, 1, 3600));
    /// <summary>Resolves the complete metadata request deadline in seconds, bounded to 1–3600.</summary>
    public static TimeSpan MetadataTimeout(LauncherPaths paths, int fallback = 30) => TimeSpan.FromSeconds(Number(Read(paths), "metadataTimeoutSeconds", fallback, 1, 3600));
    private static int Number(JsonObject settings, string key, int fallback, int min, int max) => int.TryParse(settings[key]?.ToString(), out var value) ? Math.Clamp(value, min, max) : fallback;
    /// <summary>Returns a shared client for the current source, proxy and timeout snapshot. Callers borrow it and must not dispose it.</summary>
    public static HttpClient Client(LauncherPaths paths, TimeSpan timeout)
    {
        var settings = Read(paths);
        return Clients.GetOrAdd(paths.Root + ":" + timeout.Ticks + ":" + settings.ToJsonString(), _ =>
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(Number(settings, "connectTimeoutSeconds", 20, 1, 3600))
            };
            var proxy = settings["proxy"]?.GetValue<string>();
            if (string.Equals(proxy, "direct", StringComparison.OrdinalIgnoreCase))
                handler.UseProxy = false;
            else if (!string.IsNullOrWhiteSpace(proxy) && Uri.TryCreate(proxy, UriKind.Absolute, out var proxyUri))
                handler.Proxy = new WebProxy(proxyUri);
            return new HttpClient(new MirrorHandler(settings) { InnerHandler = handler })
            {
                Timeout = TimeSpan.FromSeconds(Number(settings, "requestTimeoutSeconds", (int)timeout.TotalSeconds, 1, 3600))
            };
        });
    }

    internal sealed class MirrorHandler(JsonObject settings) : DelegatingHandler
    {
        private readonly JsonObject _mirrors = DownloadSources.Mirrors(settings);

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var original = request.RequestUri!;
            if (request.Method == HttpMethod.Get && (!request.Options.TryGetValue(DownloadRequests.OriginalSourceOnly, out var originalOnly) || !originalOnly))
            {
                foreach (var pair in _mirrors.OrderByDescending(x => x.Key.Length))
                {
                    // Only explicit absolute base prefixes ending in '/' match; never rewrite unrelated origins.
                    if (!pair.Key.EndsWith('/') || !Uri.TryCreate(pair.Key, UriKind.Absolute, out var source) || !original.AbsoluteUri.StartsWith(source.AbsoluteUri, StringComparison.Ordinal) || !Uri.TryCreate(pair.Value?.ToString(), UriKind.Absolute, out var destination) || destination.Scheme is not ("http" or "https"))
                        continue;
                    var mapped = new Uri(destination.AbsoluteUri.TrimEnd('/') + "/" + original.AbsoluteUri[source.AbsoluteUri.Length..]);
                    using var copy = new HttpRequestMessage(request.Method, mapped);
                    foreach (var header in request.Headers)
                    {
                        if (!header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && !header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) && !header.Key.Equals("x-api-key", StringComparison.OrdinalIgnoreCase))
                            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }

                    try
                    {
                        request.Options.Set(DownloadRequests.MirrorResponse, true);
                        var response = await base.SendAsync(copy, ct).ConfigureAwait(false);
                        if (response.IsSuccessStatusCode || (int)response.StatusCode is >= 300 and < 400)
                        {
                            response.RequestMessage = copy;
                            return response;
                        }

                        response.Dispose();
                    }
                    catch (HttpRequestException)
                    {
                    }

                    request.Options.Set(DownloadRequests.MirrorResponse, false);
                    break; // Unavailable mirror falls back to the original source.
                }
            }

            return await base.SendAsync(request, ct).ConfigureAwait(false);
        }
    }
}