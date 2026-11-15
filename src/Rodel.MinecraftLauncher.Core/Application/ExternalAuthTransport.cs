// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Downloads;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

internal sealed class ExternalAuthTransport(HttpClient http)
{
    internal static Uri Address(string value)
    {
        value = value.Trim();
        if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")
            || string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
        {
            throw new LauncherOperationException("AUTH_ADDRESS_INVALID", "Enter an HTTP or HTTPS website or Yggdrasil API address.");
        }

        return uri;
    }

    internal static string Root(Uri uri) => uri.AbsoluteUri.TrimEnd('/') + "/";
    internal static string? Text(JsonNode? node, string key) => node is JsonObject obj && obj[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    internal async Task<(ExternalAuthProvider Provider, JsonObject Metadata)> DiscoverAsync(string address, CancellationToken ct)
    {
        var uri = Address(address);
        for (var step = 0; step < 6; step++)
        {
            using var response = await GetAsync(uri, ct).ConfigureAwait(false);
            var actual = response.RequestMessage?.RequestUri ?? uri;
            if (response.Headers.TryGetValues("X-Authlib-Injector-API-Location", out var values)
                && Uri.TryCreate(actual, values.First(), out var location) && location != actual)
            {
                uri = Address(location.AbsoluteUri);
                continue;
            }

            var metadata = (await ReadAsync(response, ct: ct).ConfigureAwait(false)) as JsonObject
                ?? throw InvalidResponse();
            if (metadata["meta"] is not JsonObject && metadata["skinDomains"] is not JsonArray && metadata["signaturePublickey"] is null)
                throw InvalidResponse();
            return (new(Root(actual), Text(metadata["meta"], "serverName") ?? actual.Host), metadata);
        }

        throw new LauncherOperationException("AUTH_RESPONSE_INVALID", "Authentication discovery returned too many locations.");
    }

    internal async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken ct, string? bearer = null)
    {
        for (var step = 0; step < 6; step++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Options.Set(DownloadRequests.OriginalSourceOnly, true);
            request.Headers.Accept.Add(new("application/json"));
            if (bearer is not null) request.Headers.Authorization = new("Bearer", bearer);
            var response = await SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect
                && response.Headers.Location is { } location)
            {
                var next = Address(new Uri(uri, location).AbsoluteUri);
                if (bearer is not null && next.GetLeftPart(UriPartial.Authority) != uri.GetLeftPart(UriPartial.Authority))
                {
                    response.Dispose();
                    throw new LauncherOperationException("AUTH_RESPONSE_INVALID", "An authenticated request redirected to a different origin.");
                }

                response.Dispose(); uri = next;
                continue;
            }

            return response;
        }

        throw InvalidResponse();
    }

    internal async Task<JsonObject> PostAsync(string root, string path, YggdrasilRequest payload, CancellationToken ct, string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(root), path));
        request.Headers.Accept.Add(new("application/json"));
        if (bearer is not null) request.Headers.Authorization = new("Bearer", bearer);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, ExternalAuthenticationJsonContext.Default.YggdrasilRequest), Encoding.UTF8, "application/json");
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        return (await ReadAsync(response, [payload.Password, payload.AccessToken, payload.ClientToken, bearer], ct).ConfigureAwait(false)) as JsonObject ?? throw InvalidResponse();
    }

    internal async Task<JsonObject> FormAsync(string uri, Dictionary<string, string> fields, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Accept.Add(new("application/json"));
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        return (await ReadAsync(response, [.. fields.Where(f => f.Key != "client_id").Select(f => f.Value)], ct).ConfigureAwait(false)) as JsonObject ?? throw InvalidResponse();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try { return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new LauncherOperationException("AUTH_TIMEOUT", "The authentication service did not respond before the request deadline."); }
        catch (HttpRequestException ex)
        {
            throw new LauncherOperationException(ex.HttpRequestError == HttpRequestError.SecureConnectionError ? "AUTH_TLS_ERROR" : "AUTH_NETWORK_ERROR",
                "The authentication service could not be reached.", new JsonObject { ["original"] = ex.Message });
        }
    }

    internal static async Task<JsonNode?> ReadAsync(HttpResponseMessage response, string?[]? secrets = null, CancellationToken ct = default)
    {
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        // Bound both successful and error bodies; never retain arbitrary service HTML or token echoes in diagnostics.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        string raw;
        try
        {
            await response.Content.LoadIntoBufferAsync(1024 * 1024, deadline.Token).ConfigureAwait(false);
            raw = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new LauncherOperationException("AUTH_TIMEOUT", "The authentication response body did not complete before the request deadline."); }
        catch (HttpRequestException ex)
        { throw new LauncherOperationException("AUTH_RESPONSE_INVALID", "The authentication response body could not be read.", new JsonObject { ["original"] = ex.Message }); }

        JsonNode? body;
        try { body = JsonNode.Parse(raw); }
        catch (JsonException) { body = null; }

        if (response.IsSuccessStatusCode)
            return body ?? throw InvalidResponse();
        var error = Text(body, "error");
        var message = Text(body, "errorMessage") ?? Text(body, "message") ?? Text(body, "error_description");
        var code = error switch
        {
            "authorization_pending" => "AUTH_PENDING",
            "slow_down" => "AUTH_SLOW_DOWN",
            "expired_token" => "AUTH_CODE_EXPIRED",
            "access_denied" => "AUTH_ACCESS_DENIED",
            "invalid_client" or "invalid_scope" => "AUTH_CLIENT_UNAVAILABLE",
            _ when response.StatusCode == HttpStatusCode.TooManyRequests => "AUTH_RATE_LIMITED",
            _ when message is "Invalid token." or "Invalid token" => "AUTH_LOGIN_REQUIRED",
            _ when message is "Invalid username or password." or "Invalid credentials. Invalid username or password." => "AUTH_PASSWORD_INVALID",
            _ => "AUTH_REMOTE_ERROR"
        };
        if (body is JsonValue scalar && scalar.TryGetValue<string>(out var scalarText))
            body = JsonValue.Create(Redact(scalarText, secrets ?? []));
        else if (body is not null) Scrub(body, secrets ?? []);
        raw = body?.ToJsonString() ?? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        foreach (var secret in secrets ?? [])
        {
            if (string.IsNullOrEmpty(secret)) continue;
            raw = raw.Replace(secret, "[redacted]", StringComparison.Ordinal);
            if (message is not null) message = message.Replace(secret, "[redacted]", StringComparison.Ordinal);
            if (error is not null) error = error.Replace(secret, "[redacted]", StringComparison.Ordinal);
        }

        throw new LauncherOperationException(code, message ?? "The authentication service rejected the request.",
            new JsonObject
            {
                ["original"] = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{raw}",
                ["remoteError"] = error,
                ["requestId"] = response.Headers.TryGetValues("X-Yggdralt-Req-ID", out var ids) ? ids.FirstOrDefault() : null
            });
    }

    private static void Scrub(JsonNode node, string?[] secrets)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(p => p.Key).ToArray())
            {
                if (key.Contains("token", StringComparison.OrdinalIgnoreCase) || key.Contains("password", StringComparison.OrdinalIgnoreCase) || key.Equals("device_code", StringComparison.OrdinalIgnoreCase)) obj[key] = "[redacted]";
                else if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text)) obj[key] = Redact(text, secrets);
                else if (obj[key] is { } child) Scrub(child, secrets);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue value && value.TryGetValue<string>(out var text)) array[index] = Redact(text, secrets);
                else if (array[index] is { } child) Scrub(child, secrets);
            }
        }
    }

    private static string Redact(string text, string?[] secrets)
    {
        foreach (var secret in secrets)
            if (!string.IsNullOrEmpty(secret)) text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);
        return text;
    }

    internal static LauncherOperationException InvalidResponse() => new("AUTH_RESPONSE_INVALID", "The authentication service returned an unrecognized response.");
}