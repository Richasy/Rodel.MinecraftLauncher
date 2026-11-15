// Copyright (c) Richasy. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant;

internal static class AiHttpTransport
{
    internal const int MaximumBodyBytes = 1024 * 1024;

    /// <summary>Creates a caller-owned client restricted to the configured credential origin, without automatic redirects.</summary>
    public static HttpClient CreateClient(Uri endpoint, string? secret, string protocol)
        => new(new CredentialHandler(endpoint, secret, protocol))
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = MaximumBodyBytes,
            DefaultRequestHeaders = { UserAgent = { new ProductInfoHeaderValue("Rodel.MinecraftLauncher", "0.1") } }
        };

    // Retains Rodel.Agent.Core's fresh-request-per-attempt and no retry after body consumption rules.
    /// <summary>Opens one streaming request without replay; the caller owns the successful response.</summary>
    public static async Task<HttpResponseMessage> OpenStreamAsync(HttpClient http, Uri endpoint, AiProviderRequest body, string? secret, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, AssistantJsonContext.Default.AiProviderRequest), Encoding.UTF8, "application/json")
        };
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return response;
        using (response)
            throw await ErrorAsync(response, secret, ct).ConfigureAwait(false);
    }

    /// <summary>Reads a bounded JSON response, retrying eligible failures only before consuming a successful body.</summary>
    public static Task<(JsonObject Body, string? RequestId)> SendAsync(HttpClient http, Uri endpoint, AiProviderRequest? body, string? secret, CancellationToken ct)
        => SendSerializedAsync(http, endpoint, body is null ? null : JsonSerializer.Serialize(body, AssistantJsonContext.Default.AiProviderRequest), secret, ct);

    /// <summary>Sends an optional provider token-count projection through the same bounded credential and retry policy.</summary>
    public static Task<(JsonObject Body, string? RequestId)> SendJsonAsync(HttpClient http, Uri endpoint, JsonObject? body, string? secret, CancellationToken ct)
        => SendSerializedAsync(http, endpoint, body?.ToJsonString(), secret, ct);

    private static async Task<(JsonObject Body, string? RequestId)> SendSerializedAsync(HttpClient http, Uri endpoint, string? body, string? secret, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, endpoint);
            if (body is not null)
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), ct).ConfigureAwait(false);
                continue;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var delay = RetryAfter(response) ?? TimeSpan.FromMilliseconds(250 * (attempt + 1));
                    if (attempt < 2 && ((int)response.StatusCode is 408 or 429 or 500 or 502 or 503 or 504)
                        && delay <= TimeSpan.FromSeconds(30))
                    {
                        response.Dispose();
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                        continue;
                    }

                    throw await ErrorAsync(response, secret, ct).ConfigureAwait(false);
                }

                var bytes = await ReadBoundedAsync(response.Content, MaximumBodyBytes, ct).ConfigureAwait(false);
                JsonObject result;
                try
                {
                    result = JsonNode.Parse(bytes) as JsonObject ?? throw new JsonException();
                }
                catch (JsonException)
                {
                    throw new AiProtocolException("AI_PROVIDER_PROTOCOL_ERROR", "The model service returned invalid JSON instead of a response object.");
                }

                if (result["error"] is { } error)
                {
                    throw new AiProtocolException("AI_PROVIDER_ERROR", SecretScrubber.Excerpt((error as JsonObject)?["message"]?.ToString() ?? error.ToJsonString(), secret),
                        new JsonObject { ["httpStatus"] = (int)response.StatusCode, ["requestId"] = RequestId(response, secret) });
                }

                return (result, RequestId(response, secret));
            }
        }
    }

    /// <summary>Appends a provider route, supplying the official OpenAI origin's API prefix while preserving explicit paths and queries.</summary>
    public static Uri Route(Uri root, string route)
    {
        var builder = new UriBuilder(root);
        var path = root.AbsolutePath.TrimEnd('/');
        if (path.Length == 0 && root.Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase))
            path = "/v1";
        var suffix = "/" + route.Trim('/');
        if (!path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            path += suffix;
        builder.Path = path;
        return builder.Uri;
    }

    /// <summary>Maps a bounded HTTP error body and headers to sanitized protocol diagnostics.</summary>
    public static async Task<AiProtocolException> ErrorAsync(HttpResponseMessage response, string? secret, CancellationToken ct)
    {
        var bytes = await ReadBoundedAsync(response.Content, 65536, ct).ConfigureAwait(false);
        var raw = Encoding.UTF8.GetString(bytes);
        string? upstreamCode = null;
        var message = raw;
        try
        {
            if (JsonNode.Parse(bytes) is JsonObject body)
            {
                var error = body["error"] as JsonObject;
                message = error?["message"]?.ToString() ?? body["message"]?.ToString() ?? raw;
                upstreamCode = error?["status"]?.ToString() ?? error?["code"]?.ToString() ?? error?["type"]?.ToString();
            }
        }
        catch (JsonException)
        {
            message = "The service returned a non-JSON error body.";
        }

        var code = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "AI_AUTHENTICATION_FAILED",
            HttpStatusCode.TooManyRequests => "AI_RATE_LIMITED",
            HttpStatusCode.NotFound => "AI_MODEL_OR_ENDPOINT_NOT_FOUND",
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => "AI_SERVICE_TIMEOUT",
            >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest => "AI_REDIRECT_REJECTED",
            _ => "AI_PROVIDER_ERROR"
        };
        if (IsContextLimit(upstreamCode, message))
            code = "AI_CONTEXT_LIMIT";
        return new AiProtocolException(code,
            $"Model service HTTP {(int)response.StatusCode}: {SecretScrubber.Excerpt(message, secret)}",
            new JsonObject
            {
                ["httpStatus"] = (int)response.StatusCode,
                ["requestId"] = RequestId(response, secret),
                ["providerCode"] = upstreamCode is null ? null : SecretScrubber.Excerpt(upstreamCode, secret),
                ["retryAfterSeconds"] = RetryAfter(response)?.TotalSeconds,
                ["redirectFollowed"] = false
            });
    }

    /// <summary>Recognizes explicit provider context failures without confusing generic request, output or tool-size errors with input capacity.</summary>
    public static bool IsContextLimit(string? code, string? message)
        => code is "context_length_exceeded" or "context_window_exceeded" or "prompt_too_long" or "input_token_limit_exceeded"
            || (code is "invalid_request_error" or "INVALID_ARGUMENT" or "invalid_argument"
                && (message?.StartsWith("prompt is too long", StringComparison.OrdinalIgnoreCase) == true
                    || message?.Contains("maximum context length", StringComparison.OrdinalIgnoreCase) == true
                    || (message?.StartsWith("request (", StringComparison.OrdinalIgnoreCase) == true
                        && message.Contains("tokens) exceeds the available context size (", StringComparison.OrdinalIgnoreCase))
                    || (message?.Contains("input token count", StringComparison.OrdinalIgnoreCase) == true
                        && message.Contains("exceeds", StringComparison.OrdinalIgnoreCase)
                        && message.Contains("maximum", StringComparison.OrdinalIgnoreCase))));

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximum, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maximum)
            throw new AiProtocolException("AI_RESPONSE_TOO_LARGE", "The model service response exceeded the bounded response size.");
        using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
                return result.ToArray();
            if (result.Length + read > maximum)
                throw new AiProtocolException("AI_RESPONSE_TOO_LARGE", "The model service response exceeded the bounded response size.");
            result.Write(buffer, 0, read);
        }
    }

    private static string? RequestId(HttpResponseMessage response, string? secret)
        => response.Headers.TryGetValues("x-request-id", out var values) ? SecretScrubber.Excerpt(values.First(), secret, 256) : null;

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (header?.Date is { } date)
            return date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : TimeSpan.Zero;
        return null;
    }

    private sealed class CredentialHandler(Uri endpoint, string? secret, string protocol) : DelegatingHandler(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseProxy = !endpoint.IsLoopback,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        MaxResponseHeadersLength = 32,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    })
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not { } destination || destination.Scheme != endpoint.Scheme || destination.IdnHost != endpoint.IdnHost || destination.Port != endpoint.Port)
                throw new AiProtocolException("AI_CREDENTIAL_ORIGIN_MISMATCH", "A model request attempted to leave its configured credential origin.");
            request.Headers.Authorization = null;
            if (protocol == AiProviderProtocols.AnthropicMessages)
            {
                request.Headers.Add("anthropic-version", "2023-06-01");
                if (!string.IsNullOrEmpty(secret))
                    request.Headers.Add("x-api-key", secret);
            }
            else if (protocol == AiProviderProtocols.Gemini)
            {
                if (!string.IsNullOrEmpty(secret))
                    request.Headers.Add("x-goog-api-key", secret);
            }
            else if (!string.IsNullOrEmpty(secret))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}