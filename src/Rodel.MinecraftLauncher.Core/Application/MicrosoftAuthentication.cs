// Copyright (c) Richasy. All rights reserved.

using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

internal sealed class MicrosoftAuthentication(string clientId, HttpClient? http = null, string? pendingPath = null)
{
    private static readonly string[] Scopes = ["XboxLive.SignIn", "XboxLive.offline_access"];
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly HttpClient _http = http ?? SharedHttp;
    internal static string ResolveClientId(string? supplied = null)
    {
        var processValue = supplied is null ? Environment.GetEnvironmentVariable("RODEL_MICROSOFT_CLIENT_ID") : null;
        var userValue = supplied is null && processValue is null && OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("RODEL_MICROSOFT_CLIENT_ID", EnvironmentVariableTarget.User)
            : null;
        return ResolveClientId(supplied, processValue, userValue);
    }

    internal static string ResolveClientId(string? supplied, string? processValue, string? userValue)
    {
        var value = supplied ?? processValue ?? userValue;
        if (value is null)
        {
            using var stream = typeof(MicrosoftAuthentication).Assembly.GetManifestResourceStream("Rodel.MicrosoftClientId");
            if (stream is not null)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                value = reader.ReadToEnd().Trim();
            }
        }

        if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
            throw new LauncherOperationException("AUTH_NOT_CONFIGURED", "Configure RODEL_MICROSOFT_CLIENT_ID with your application's client ID. Offline identities remain available.");
        return parsed.ToString();
    }

    internal async Task<(IdentityRecord Summary, JsonObject Credentials)> SignInAsync(MicrosoftLoginOptions options, JsonObject? previous, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (options.Method == MicrosoftLoginMethod.System && options.ParentWindow == 0)
            throw new LauncherOperationException("AUTH_WINDOW_REQUIRED", "System sign-in requires the application's parent window. Use browser or device-code sign-in from the CLI.");
        var persisted = previous?.DeepClone().AsObject() ?? [];
        try
        {
            var application = CreateApplication(options.Method);
            ConfigureCache(application, persisted);
            AuthenticationResult result;
            options.Prompt?.Report(new("microsoft"));
            if (options.Method == MicrosoftLoginMethod.DeviceCode)
            {
                if (options.Prompt is null) throw new LauncherOperationException("AUTH_INTERACTION_REQUIRED", "Device sign-in needs a foreground prompt receiver.");
                result = await application.AcquireTokenWithDeviceCode(Scopes, code =>
                {
                    options.Prompt.Report(new("device-code", code.VerificationUrl, code.UserCode));
                    return Task.CompletedTask;
                }).ExecuteAsync(ct).ConfigureAwait(false);
            }
            else
            {
                var request = application.AcquireTokenInteractive(Scopes).WithPrompt(Microsoft.Identity.Client.Prompt.SelectAccount);
                if (options.ParentWindow != 0) request = request.WithParentActivityOrWindow(options.ParentWindow);
                result = await request.ExecuteAsync(ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            var credentials = persisted;
            credentials["clientId"] = clientId;
            credentials["homeAccountId"] = result.Account.HomeAccountId.Identifier;
            credentials["method"] = options.Method.ToString();
            credentials["expectedAccountId"] = options.AccountId;
            if (pendingPath is not null) await ProtectedStore.WriteAsync(pendingPath, credentials, ct).ConfigureAwait(false);
            var summary = await ExchangeAsync(result.AccessToken, credentials, options.Prompt, ct).ConfigureAwait(false);
            return (summary, credentials);
        }
        catch (MsalException ex) { throw Translate(ex, ct); }
    }

    internal async Task<IdentityRecord> RefreshAsync(JsonObject credentials, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var method = credentials["method"]?.GetValue<string>() == nameof(MicrosoftLoginMethod.System) ? MicrosoftLoginMethod.System : MicrosoftLoginMethod.Browser;
            var application = CreateApplication(method);
            ConfigureCache(application, credentials);
            var account = (await application.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault(a => a.HomeAccountId.Identifier == credentials["homeAccountId"]?.GetValue<string>()) ?? throw new LauncherOperationException("AUTH_LOGIN_REQUIRED", "The saved Microsoft session is unavailable. Sign in again.");
            ct.ThrowIfCancellationRequested();
            var result = await application.AcquireTokenSilent(Scopes, account).ExecuteAsync(ct).ConfigureAwait(false);
            return await ExchangeAsync(result.AccessToken, credentials, null, ct).ConfigureAwait(false);
        }
        catch (MsalException ex) { throw Translate(ex, ct); }
    }

    private IPublicClientApplication CreateApplication(MicrosoftLoginMethod method)
    {
        var builder = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, "consumers")
            .WithRedirectUri("http://localhost");
        // MSAL supplies WAM's broker redirect. The loopback URI is used only by browser sign-in.
        if (method == MicrosoftLoginMethod.System)
            builder = builder.WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { Title = "RMCL" });
        return builder.Build();
    }

    private static void ConfigureCache(IPublicClientApplication app, JsonObject credentials)
    {
        app.UserTokenCache.SetBeforeAccess(args =>
        {
            if (credentials["msalCache"]?.GetValue<string>() is { } cache)
                args.TokenCache.DeserializeMsalV3(Convert.FromBase64String(cache));
        });
        app.UserTokenCache.SetAfterAccess(args =>
        {
            if (args.HasStateChanged) credentials["msalCache"] = Convert.ToBase64String(args.TokenCache.SerializeMsalV3());
        });
    }

    internal async Task<IdentityRecord> ExchangeAsync(string accessToken, JsonObject credentials, IProgress<MicrosoftLoginPrompt>? prompt, CancellationToken ct)
    {
        prompt?.Report(new("xbox"));
        var xbox = await SendAsync("https://user.auth.xboxlive.com/user/authenticate", new JsonObject
        {
            ["Properties"] = new JsonObject { ["AuthMethod"] = "RPS", ["SiteName"] = "user.auth.xboxlive.com", ["RpsTicket"] = "d=" + accessToken },
            ["RelyingParty"] = "http://auth.xboxlive.com",
            ["TokenType"] = "JWT"
        }, null, "xbox", ct).ConfigureAwait(false);
        var hash = Required(xbox["DisplayClaims"]?["xui"]?[0], "uhs");
        var xsts = await SendAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new JsonObject
        {
            ["Properties"] = new JsonObject { ["SandboxId"] = "RETAIL", ["UserTokens"] = new JsonArray(Required(xbox, "Token")) },
            ["RelyingParty"] = "rp://api.minecraftservices.com/",
            ["TokenType"] = "JWT"
        }, null, "xsts", ct).ConfigureAwait(false);
        if (Required(xsts["DisplayClaims"]?["xui"]?[0], "uhs") != hash)
            throw new LauncherOperationException("AUTH_IDENTITY_MISMATCH", "Xbox returned a different account identity. Sign in again.");
        prompt?.Report(new("minecraft"));
        var minecraft = await SendAsync("https://api.minecraftservices.com/authentication/login_with_xbox", new JsonObject
        {
            ["identityToken"] = "XBL3.0 x=" + hash + ";" + Required(xsts, "Token")
        }, null, "minecraft", ct).ConfigureAwait(false);
        var token = Required(minecraft, "access_token");
        prompt?.Report(new("profile"));
        // A successful profile response is required; do not infer ownership from Microsoft consent.
        JsonObject profile;
        try { profile = await SendAsync("https://api.minecraftservices.com/minecraft/profile", null, token, "profile", ct).ConfigureAwait(false); }
        catch (LauncherOperationException ex) when (ex.Code == "AUTH_PROFILE_UNAVAILABLE")
        {
            var entitlements = await SendAsync("https://api.minecraftservices.com/entitlements/mcstore", null, token, "entitlements", ct).ConfigureAwait(false);
            var hasEntitlement = entitlements["items"] is JsonArray items && items.Count > 0;
            throw new LauncherOperationException(hasEntitlement ? "AUTH_PROFILE_REQUIRED" : "AUTH_GAME_ACCESS_UNAVAILABLE",
                hasEntitlement ? "The account has game access but no Java player profile. Create the profile on minecraft.net and retry."
                    : "No playable Java profile or game entitlement was returned. Check the selected account, purchase or subscription status, then retry.",
                new JsonObject { ["stage"] = "profile", ["hasEntitlements"] = hasEntitlement });
        }

        var uuid = Required(profile, "id");
        if (!Guid.TryParseExact(uuid, "N", out _)) throw new LauncherOperationException("AUTH_INVALID_RESPONSE", "Minecraft returned an invalid player identifier.");
        credentials["minecraftToken"] = token;
        credentials["expiresAt"] = DateTimeOffset.UtcNow.AddSeconds(minecraft["expires_in"]?.GetValue<int>() ?? 3600).ToString("O");
        credentials["uuid"] = uuid;
        credentials["xuid"] = xsts["DisplayClaims"]?["xui"]?[0]?["xid"]?.DeepClone();
        var skin = profile["skins"]?.AsArray().OfType<JsonObject>().FirstOrDefault(s => s["state"]?.GetValue<string>() == "ACTIVE")?["url"];
        var summary = new IdentityRecord
        {
            Id = "microsoft-" + uuid,
            Name = Required(profile, "name"),
            Type = "microsoft",
            Uuid = uuid,
            Status = "ready",
            VerifiedAt = DateTimeOffset.UtcNow.ToString("O"),
            SkinUrl = skin is JsonValue value && value.TryGetValue<string>(out var url) ? url : null
        };
        if (summary.SkinUrl is null)
            summary.AdditionalData["skinUrl"] = JsonSerializer.SerializeToElement(skin, ApplicationJsonContext.Default.JsonNode);
        return summary;
    }

    private async Task<JsonObject> SendAsync(string url, JsonObject? payload, string? bearer, string stage, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(payload is null ? HttpMethod.Get : HttpMethod.Post, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (bearer is not null) request.Headers.Authorization = new("Bearer", bearer);
        if (payload is not null) request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await SendRequestAsync(request, stage, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        JsonObject data;
        try { data = JsonNode.Parse(text)?.AsObject() ?? []; }
        catch (System.Text.Json.JsonException) { throw new LauncherOperationException("AUTH_INVALID_RESPONSE", $"The {stage} service returned an unreadable response."); }

        if (!response.IsSuccessStatusCode)
        {
            var code = stage == "profile" && response.StatusCode == System.Net.HttpStatusCode.NotFound ? "AUTH_PROFILE_UNAVAILABLE"
                : response.StatusCode == System.Net.HttpStatusCode.Forbidden ? "AUTH_ACCESS_DENIED"
                : response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "AUTH_LOGIN_REQUIRED" : "AUTH_SERVICE_ERROR";
            throw new LauncherOperationException(code, $"The {stage} service could not complete sign-in. Check the account's Java edition access and Xbox settings, or retry later.",
                new JsonObject { ["stage"] = stage, ["httpStatus"] = (int)response.StatusCode, ["xboxError"] = data["XErr"]?.DeepClone() });
        }

        return data;
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, string stage, CancellationToken ct)
    {
        try { return await _http.SendAsync(request, ct).ConfigureAwait(false); }
        catch (HttpRequestException error)
        {
            var host = request.RequestUri!.Host;
            var secureConnection = error.HttpRequestError == HttpRequestError.SecureConnectionError;
            throw new LauncherOperationException(secureConnection ? "AUTH_TLS_ERROR" : "AUTH_NETWORK_ERROR",
                secureConnection
                    ? $"The {stage} authentication service could not establish a secure connection to {host}. Check the network, proxy and system clock, then retry."
                    : $"The {stage} authentication service at {host} could not be reached. Check the network and proxy, then retry.",
                new JsonObject
                {
                    ["stage"] = stage,
                    ["host"] = host,
                    ["networkReason"] = error.HttpRequestError.ToString(),
                    ["httpStatus"] = error.StatusCode is { } status ? (int)status : null,
                    ["causeType"] = error.GetBaseException().GetType().Name,
                    ["causeHResult"] = error.GetBaseException().HResult
                });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LauncherOperationException("AUTH_TIMEOUT", "The authentication service timed out. Retry the saved sign-in.", new JsonObject { ["stage"] = stage });
        }
    }

    private static string Required(JsonNode? data, string key) => data?[key]?.GetValue<string>() is { Length: > 0 } value ? value
        : throw new LauncherOperationException("AUTH_INVALID_RESPONSE", $"The authentication response is missing {key}.");
    internal static Exception Translate(MsalException error, CancellationToken ct) => ct.IsCancellationRequested || error.ErrorCode == MsalError.AuthenticationCanceledError
        ? new OperationCanceledException("Microsoft sign-in was canceled.", ct)
        : new LauncherOperationException(error is MsalUiRequiredException ? "AUTH_LOGIN_REQUIRED"
                : error.ErrorCode == "wam_runtime_init_failed" ? "AUTH_BROKER_UNAVAILABLE" : "AUTH_MICROSOFT_FAILED",
            "Microsoft sign-in could not complete. Retry, use browser sign-in if the system broker is unavailable, or check application registration.",
            new JsonObject { ["providerCode"] = error.ErrorCode });
}