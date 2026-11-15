// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Downloads;

public static class DownloadRequests
{
    internal static readonly HttpRequestOptionsKey<bool> OriginalSourceOnly = new("Rodel.OriginalSourceOnly");
    internal static readonly HttpRequestOptionsKey<bool> MirrorResponse = new("Rodel.MirrorResponse");
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
    public static async Task<HttpResponseMessage> GetBufferedAsync(HttpClient client, Uri uri, TimeSpan timeout, CancellationToken ct)
    {
        var originalOnly = false;
        while (true)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(timeout);
            HttpResponseMessage? response = null;
            HttpRequestMessage? currentRequest = null;
            var mirroredRedirect = false;
            try
            {
                response = await SendAsync(client: client, uri: uri, headersForUri: null, ct: deadline.Token, configure: request =>
                {
                    mirroredRedirect |= WasMirrored(currentRequest);
                    currentRequest = request;
                    request.Options.Set(OriginalSourceOnly, originalOnly);
                }).ConfigureAwait(false);
                await response.Content.LoadIntoBufferAsync(deadline.Token).ConfigureAwait(false);
                return response;
            }
            catch (Exception exception) when (!originalOnly && !ct.IsCancellationRequested
                && (mirroredRedirect || WasMirrored(currentRequest))
                && exception is HttpRequestException or IOException or OperationCanceledException)
            {
                // Give the original source its own bounded attempt when a mirror stalls,
                // including after headers or a redirect. Explicit cancellation never retries.
                response?.Dispose();
                originalOnly = true;
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                response?.Dispose();
                throw new HttpRequestException($"Metadata request/body exceeded {timeout}: {uri}", ex);
            }
            catch
            {
                response?.Dispose();
                throw;
            }
        }
    }

    private static bool WasMirrored(HttpRequestMessage? request) => request is not null
        && request.Options.TryGetValue(MirrorResponse, out var mirrored) && mirrored;

    public static HttpClient CreateClient(TimeSpan timeout) => new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = timeout
    };
    public static Func<Uri, IReadOnlyDictionary<string, string>> OriginHeaders(Uri origin, string? key) => uri => uri.Scheme == origin.Scheme && uri.IdnHost == origin.IdnHost && uri.Port == origin.Port ? ApiKeyHeader(key) : Empty;
    private static IReadOnlyDictionary<string, string> ApiKeyHeader(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Empty;
        if (key.Contains('\r') || key.Contains('\n'))
            throw new InvalidDataException("Configured API key contains invalid header characters.");
        return new Dictionary<string, string>
        {
            ["x-api-key"] = key
        };
    }

    /// <summary>Injected transports must disable automatic redirects. Every redirect gets a fresh request and freshly scoped headers.</summary>
    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, Uri uri, Func<Uri, IReadOnlyDictionary<string, string>>? headersForUri, Action<HttpRequestMessage>? configure = null, CancellationToken ct = default)
    {
        for (var redirects = 0; ; redirects++)
        {
            if (uri.Scheme is not ("https" or "http"))
                throw new InvalidDataException("Downloads require HTTP(S).");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Rodel.MinecraftLauncher/0.1 (+https://github.com/Richasy/Rodel.MinecraftLauncher)");
            foreach (var header in headersForUri?.Invoke(uri) ?? Empty)
                request.Headers.Add(header.Key, header.Value);
            configure?.Invoke(request);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.RequestMessage ??= request;
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308))
                return response;
            var location = response.Headers.Location;
            var redirectOrigin = response.RequestMessage?.RequestUri ?? uri;
            response.Dispose();
            if (location is null)
                throw new HttpRequestException("Redirect response has no Location header.");
            if (redirects >= 9)
                throw new HttpRequestException("Download exceeded ten redirects.");
            uri = location.IsAbsoluteUri ? location : new Uri(redirectOrigin, location);
        }
    }
}