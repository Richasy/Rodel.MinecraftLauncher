// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Core.Application.ExternalAuthTransport;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class IdentityService
{
    private const string LittleSkinApi = "https://littleskin.cn/api/yggdrasil/";
    private const string LittleSkinOAuth = "https://open.littleskin.cn/oauth/";

    /// <summary>Starts the registered LittleSkin application's device flow. This exposes only the user-facing code, never the private device code.</summary>
    /// <param name="accountId">An optional existing LittleSkin identity whose role must be retained.</param>
    /// <param name="clientId">An application ID, otherwise read from RODEL_LITTLESKIN_CLIENT_ID or the registered Rodel application (1645); no client secret is used.</param>
    /// <param name="ct">Cancels discovery and device-code acquisition.</param>
    public async Task<ExternalLoginResult> BeginLittleSkinAsync(string? accountId = null, string? clientId = null, CancellationToken ct = default)
    {
        clientId ??= Environment.GetEnvironmentVariable("RODEL_LITTLESKIN_CLIENT_ID") ?? "1645";
        if (string.IsNullOrWhiteSpace(clientId))
            throw new LauncherOperationException("AUTH_CLIENT_UNAVAILABLE", "This launcher has not configured its LittleSkin device-flow application.");
        if (accountId is not null && (ExternalIdentity(accountId).AuthMethod != "littleskin" || ExternalIdentity(accountId).AuthServer != LittleSkinApi))
            throw new ArgumentException("Select a LittleSkin identity.");
        var (provider, metadata) = await ExternalTransport.DiscoverAsync(LittleSkinApi, ct).ConfigureAwait(false);
        if (provider.ApiRoot != LittleSkinApi) throw new LauncherOperationException("AUTH_SOURCE_CHANGED", "LittleSkin returned a different API root.");
        var response = await ExternalTransport.FormAsync(LittleSkinOAuth + "device_code", new()
        {
            ["client_id"] = clientId,
            ["scope"] = "User.Read Yggdrasil.PlayerProfiles.Read Yggdrasil.MinecraftToken.Create Yggdrasil.Server.Join"
        }, ct).ConfigureAwait(false);
        var pending = NewAttempt(provider with { Name = "LittleSkin" }, metadata, "littleskin", accountId);
        pending["deviceCode"] = Text(response, "device_code") ?? throw InvalidResponse();
        pending["userCode"] = Text(response, "user_code") ?? throw InvalidResponse();
        pending["verificationUri"] = Address(Text(response, "verification_uri") ?? throw InvalidResponse()).AbsoluteUri;
        pending["verificationUriComplete"] = Text(response, "verification_uri_complete") is { } complete ? Address(complete).AbsoluteUri : null;
        var expires = response["expires_in"]?.GetValue<int>() ?? throw InvalidResponse();
        if (expires <= 0) throw InvalidResponse();
        var interval = Math.Max(1, response["interval"]?.GetValue<int>() ?? 5);
        pending["clientId"] = clientId; pending["interval"] = interval;
        pending["expiresAt"] = DateTimeOffset.UtcNow.AddSeconds(expires).ToString("O");
        pending["nextPollAt"] = DateTimeOffset.UtcNow.AddSeconds(interval).ToString("O");
        var attemptId = Guid.NewGuid().ToString("N");
        await ProtectedStore.WriteAsync(AttemptPath(attemptId), pending, ct).ConfigureAwait(false);
        return DescribeAttempt(attemptId, pending);
    }

    /// <summary>Performs one interval-respecting LittleSkin poll, or rereads roles after authorization. Pending and slow_down remain waiting states.</summary>
    public async Task<ExternalLoginResult> PollLittleSkinAsync(string attemptId, CancellationToken ct = default)
    {
        using (var lease = await ResourceLease.AcquireAsync(AttemptPath(attemptId), ct).ConfigureAwait(false))
        {
            var pending = ReadAttempt(attemptId);
            if (Text(pending, "method") != "littleskin") throw new ArgumentException("Select a LittleSkin login attempt.");
            if (Text(pending, "deviceCode") is { } deviceCode)
            {
                var next = DateTimeOffset.Parse(Text(pending, "nextPollAt")!, System.Globalization.CultureInfo.InvariantCulture);
                if (next > DateTimeOffset.UtcNow) await Task.Delay(next - DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
                pending = ReadAttempt(attemptId);
                var interval = pending["interval"]!.GetValue<int>();
                pending["nextPollAt"] = DateTimeOffset.UtcNow.AddSeconds(interval).ToString("O");
                await ProtectedStore.WriteAsync(AttemptPath(attemptId), pending, ct).ConfigureAwait(false);
                try
                {
                    var token = await ExternalTransport.FormAsync(LittleSkinOAuth + "token", new()
                    {
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                        ["client_id"] = Text(pending, "clientId")!,
                        ["device_code"] = deviceCode
                    }, ct).ConfigureAwait(false);
                    pending["oauthToken"] = Text(token, "access_token") ?? throw InvalidResponse();
                    pending.Remove("deviceCode");
                    pending["expiresAt"] = DateTimeOffset.UtcNow.AddMinutes(10).ToString("O");
                    await ProtectedStore.WriteAsync(AttemptPath(attemptId), pending, ct).ConfigureAwait(false);
                }
                catch (LauncherOperationException ex) when (ex.Code is "AUTH_PENDING" or "AUTH_SLOW_DOWN")
                {
                    if (ex.Code == "AUTH_SLOW_DOWN")
                    {
                        pending["interval"] = interval + 5;
                        pending["nextPollAt"] = DateTimeOffset.UtcNow.AddSeconds(interval + 5).ToString("O");
                        await ProtectedStore.WriteAsync(AttemptPath(attemptId), pending, ct).ConfigureAwait(false);
                    }

                    return DescribeAttempt(attemptId, pending);
                }
            }

            var bearer = Text(pending, "oauthToken")!;
            using var userResponse = await ExternalTransport.GetAsync(new Uri("https://littleskin.cn/api/user"), ct, bearer).ConfigureAwait(false);
            var user = await ReadAsync(userResponse, [bearer], ct).ConfigureAwait(false) as JsonObject ?? throw InvalidResponse();
            var uid = user["uid"]?.ToString();
            if (string.IsNullOrEmpty(uid)) throw InvalidResponse();
            pending["userKey"] = Digest(uid);
            using var profileResponse = await ExternalTransport.GetAsync(new Uri(LittleSkinApi + "sessionserver/session/minecraft/profile"), ct, bearer).ConfigureAwait(false);
            pending["profiles"] = (await ReadAsync(profileResponse, [bearer], ct).ConfigureAwait(false)) as JsonArray ?? throw InvalidResponse();
            await ProtectedStore.WriteAsync(AttemptPath(attemptId), pending, ct).ConfigureAwait(false);
        }

        return await FinishExternalLoginAsync(attemptId, ct).ConfigureAwait(false);
    }
}