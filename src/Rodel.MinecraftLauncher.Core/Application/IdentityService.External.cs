// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Core.Application.ExternalAuthTransport;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class IdentityService
{
    private ExternalAuthTransport ExternalTransport => new(authenticationHttp ?? NetworkSettings.Client(paths, TimeSpan.FromSeconds(45)));
    private static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private string AttemptPath(string id) => Guid.TryParseExact(id, "N", out _) ? CredentialPath("external-attempt:" + id)
        : throw new ArgumentException("Invalid external login attempt identifier.");

    /// <summary>Discovers the canonical service endpoint using ALI, without sending credentials.</summary>
    public async Task<ExternalAuthProvider> DiscoverExternalAsync(string address, CancellationToken ct = default)
        => (await ExternalTransport.DiscoverAsync(address, ct).ConfigureAwait(false)).Provider;

    /// <summary>Authenticates once with a password, registering a bound/single role or retaining a protected role-selection attempt.</summary>
    public async Task<ExternalLoginResult> LoginExternalAsync(ExternalLoginOptions options, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Password);
        var expected = options.AccountId is { } id ? ExternalIdentity(id) : null;
        var address = expected?.AuthServer ?? options.Address ?? throw new ArgumentException("An authentication address is required.");
        var (provider, metadata) = await ExternalTransport.DiscoverAsync(address, ct).ConfigureAwait(false);
        if (expected is not null && provider.ApiRoot != expected.AuthServer)
            throw new LauncherOperationException("AUTH_SOURCE_CHANGED", "The account's authentication endpoint changed. Add the new source separately.");
        var response = await ExternalTransport.PostAsync(provider.ApiRoot, "authserver/authenticate", new()
        {
            Username = options.Username.Trim(),
            Password = options.Password,
            ClientToken = Guid.NewGuid().ToString("N"),
            Agent = new()
        }, ct).ConfigureAwait(false);
        _ = SessionRequest(response);
        if (response["availableProfiles"] is not (null or JsonArray) || response["selectedProfile"] is not (null or JsonObject)) throw InvalidResponse();
        var pending = NewAttempt(provider, metadata, "password", expected?.Id);
        pending["userKey"] = Digest(Text(response["user"], "id") ?? options.Username.Trim());
        pending["session"] = response;
        var attempt = Guid.NewGuid().ToString("N");
        ct.ThrowIfCancellationRequested();
        await ProtectedStore.WriteAsync(AttemptPath(attempt), pending, ct).ConfigureAwait(false);
        try { return await FinishExternalLoginAsync(attempt, ct).ConfigureAwait(false); }
        catch { File.Delete(AttemptPath(attempt)); throw; }
    }

    /// <summary>Reads a pending role-selection snapshot without exposing its tokens.</summary>
    public async Task<ExternalLoginResult> GetExternalLoginAsync(string attemptId, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(AttemptPath(attemptId), ct).ConfigureAwait(false);
        return DescribeAttempt(attemptId, ReadAttempt(attemptId));
    }

    /// <summary>Binds an offered profile and commits its independent protected identity; existing defaults are retained.</summary>
    public Task<ExternalLoginResult> SelectExternalProfileAsync(string attemptId, string profileId, CancellationToken ct = default)
        => FinishExternalLoginAsync(attemptId, ct, profileId);

    /// <summary>Discards a pending attempt and its private authorization state without removing registered identities.</summary>
    public async Task CancelExternalLoginAsync(string attemptId, CancellationToken ct = default)
    {
        var file = AttemptPath(attemptId);
        using var lease = await ResourceLease.AcquireAsync(file, ct).ConfigureAwait(false);
        File.Delete(file);
    }

    private IdentityRecord ExternalIdentity(string id)
    {
        var account = Get(id);
        if (account.Type != "external" || account.AuthServer is null) throw new ArgumentException("Select an external account.");
        return account;
    }

    private JsonObject ReadAttempt(string id)
    {
        var pending = ProtectedStore.Read(AttemptPath(id))
            ?? throw new LauncherOperationException("AUTH_ATTEMPT_UNAVAILABLE", "This sign-in attempt ended. Start sign-in again.");
        if (!DateTimeOffset.TryParse(Text(pending, "expiresAt"), out var expires) || expires <= DateTimeOffset.UtcNow)
        {
            File.Delete(AttemptPath(id));
            throw new LauncherOperationException("AUTH_CODE_EXPIRED", "The sign-in request expired. Start sign-in again.");
        }

        return pending;
    }

    private static JsonObject NewAttempt(ExternalAuthProvider provider, JsonObject metadata, string method, string? expected) => new()
    {
        ["apiRoot"] = provider.ApiRoot,
        ["name"] = provider.Name,
        ["metadata"] = metadata.DeepClone(),
        ["method"] = method,
        ["accountId"] = expected,
        ["expiresAt"] = DateTimeOffset.UtcNow.AddMinutes(10).ToString("O")
    };

    private static ExternalAuthProvider Provider(JsonObject pending) => new(Text(pending, "apiRoot")!, Text(pending, "name")!);
    private static ExternalAuthProfile Profile(JsonNode node)
    {
        if (!Guid.TryParse(Text(node, "id"), out var uuid) || string.IsNullOrWhiteSpace(Text(node, "name"))) throw InvalidResponse();
        string? skin = null;
        try
        {
            skin = ReadRoleSkinUrl(node);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or LauncherOperationException) { /* Optional texture properties do not block authentication. */ }

        return new(uuid.ToString("N"), Text(node, "name")!, skin);
    }

    private static ExternalAuthProfile[] Profiles(JsonObject pending)
    {
        var response = pending["session"];
        var profiles = (pending["profiles"] as JsonArray ?? response?["availableProfiles"] as JsonArray ?? []).Select(p => Profile(p!)).ToList();
        if (response?["selectedProfile"] is JsonObject selected && profiles.All(p => p.Id != Profile(selected).Id)) profiles.Add(Profile(selected));
        return [.. profiles.DistinctBy(p => p.Id)];
    }

    private static ExternalLoginResult DescribeAttempt(string id, JsonObject pending) => new()
    {
        AttemptId = id,
        Provider = Provider(pending),
        Profiles = Profiles(pending),
        Stage = pending["deviceCode"] is not null ? "device" : Profiles(pending).Length == 0 ? "no-roles" : "roles",
        UserCode = Text(pending, "userCode"),
        VerificationUri = Text(pending, "verificationUri"),
        VerificationUriComplete = Text(pending, "verificationUriComplete"),
        ExpiresAt = DateTimeOffset.Parse(Text(pending, "expiresAt")!, System.Globalization.CultureInfo.InvariantCulture)
    };

    private async Task<ExternalLoginResult> FinishExternalLoginAsync(string attemptId, CancellationToken ct, string? chosen = null)
    {
        using var attemptLease = await ResourceLease.AcquireAsync(AttemptPath(attemptId), ct).ConfigureAwait(false);
        var pending = ReadAttempt(attemptId);
        var profiles = Profiles(pending);
        var expected = Text(pending, "accountId") is { } accountId ? ExternalIdentity(accountId) : null;
        if (expected is not null && expected.ExternalUser != Text(pending, "userKey"))
            throw new LauncherOperationException("AUTH_IDENTITY_MISMATCH", "A different service user signed in. Add that user separately.");
        var bound = pending["session"]?["selectedProfile"] is JsonObject selected ? Profile(selected) : null;
        var role = expected is not null ? profiles.FirstOrDefault(p => p.Id == expected.Uuid)
            : chosen is not null ? profiles.FirstOrDefault(p => p.Id == chosen) : bound ?? (profiles.Length == 1 ? profiles[0] : null);
        if (expected is not null && (role is null || (bound is not null && bound.Id != expected.Uuid)))
            throw new LauncherOperationException("AUTH_PROFILE_REMOVED", "The original role is no longer available. Add another role explicitly.");
        if (chosen is not null && role is null) throw new ArgumentException("Select a profile offered by this login attempt.");
        if (role is null) return DescribeAttempt(attemptId, pending);
        JsonObject response;
        if (Text(pending, "method") == "littleskin")
            response = await ExternalTransport.PostAsync(Provider(pending).ApiRoot, "authserver/oauth", new() { Uuid = role.Id }, ct, Text(pending, "oauthToken")).ConfigureAwait(false);
        else if (bound is null)
            response = await ExternalTransport.PostAsync(Provider(pending).ApiRoot, "authserver/refresh", SessionRequest(pending["session"]!.AsObject()) with { SelectedProfile = new(role.Id, role.Name) }, ct).ConfigureAwait(false);
        else response = pending["session"]!.DeepClone().AsObject();
        var actual = response["selectedProfile"] is JsonObject actualProfile ? Profile(actualProfile) : throw InvalidResponse();
        if (actual.Id != role.Id) throw new LauncherOperationException("AUTH_IDENTITY_MISMATCH", "The service returned a different role; the original identity was retained.");
        var credentials = Credentials(pending, response);
        // Optional skin lookup must not invalidate a successful login or discard its fresh token.
        actual = await ReadProfileSkinAsync(Provider(pending).ApiRoot, actual, ct).ConfigureAwait(false);
        var summary = Summary(pending, actual, expected?.Id);
        ct.ThrowIfCancellationRequested();
        using var identityLease = await ResourceLease.AcquireAsync(FilePath, ct).ConfigureAwait(false);
        if (expected is not null) _ = ExternalIdentity(expected.Id!);
        await SaveCredentialsAsync(summary, credentials, ct).ConfigureAwait(false);
        File.Delete(AttemptPath(attemptId));
        return DescribeAttempt(attemptId, pending) with { Stage = "complete", Account = summary };
    }

    private static YggdrasilRequest SessionRequest(JsonObject response) => new()
    {
        AccessToken = Text(response, "accessToken") ?? Text(response, "minecraftToken") ?? throw InvalidResponse(),
        ClientToken = Text(response, "clientToken") ?? throw InvalidResponse()
    };

    private static JsonObject Credentials(JsonObject pending, JsonObject response)
    {
        var session = SessionRequest(response);
        return new()
        {
            ["kind"] = "external",
            ["minecraftToken"] = session.AccessToken,
            ["clientToken"] = session.ClientToken,
            ["clientId"] = string.Empty,
            ["apiRoot"] = Text(pending, "apiRoot"),
            ["metadata"] = pending["metadata"]?.DeepClone()
        };
    }

    private static IdentityRecord Summary(JsonObject pending, ExternalAuthProfile role, string? existingId = null)
    {
        var root = Text(pending, "apiRoot")!;
        var user = Text(pending, "userKey")!;
        var summary = new IdentityRecord
        {
            Id = existingId ?? ("external-" + Digest(root + "\n" + user + "\n" + role.Id)),
            Type = "external",
            Uuid = role.Id,
            Name = role.Name,
            Status = "ready",
            SkinUrl = role.SkinUrl,
            AuthServer = root,
            AuthServerName = Text(pending, "name"),
            AuthMethod = Text(pending, "method"),
            ExternalUser = user,
            VerifiedAt = DateTimeOffset.UtcNow.ToString("O")
        };
        if (summary.SkinUrl is null) summary.PreserveNull("skinUrl");
        if (summary.AuthServerName is null) summary.PreserveNull("authServerName");
        if (summary.AuthMethod is null) summary.PreserveNull("authMethod");
        return summary;
    }

    private async Task<ExternalAuthProfile> ReadProfileSkinAsync(string root, ExternalAuthProfile profile, CancellationToken ct)
    {
        try
        {
            using var response = await ExternalTransport.GetAsync(new Uri(new Uri(root), "sessionserver/session/minecraft/profile/" + profile.Id), ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NoContent
                && await ReadAsync(response, ct: ct).ConfigureAwait(false) is JsonObject body && Profile(body).Id == profile.Id)
            {
                return profile with { SkinUrl = Profile(body).SkinUrl ?? profile.SkinUrl };
            }
        }
        catch (LauncherOperationException) { }

        return profile;
    }

    /// <summary>Verifies or refreshes the selected account through its own authentication source.</summary>
    public Task<IdentityRecord> RefreshAsync(string id, CancellationToken ct = default)
        => Get(id).Type == "external" ? RefreshExternalAsync(id, ct) : RefreshMicrosoftAsync(id, ct);

    /// <summary>Refreshes an external session while retaining the original source and role on every failure.</summary>
    public async Task<IdentityRecord> RefreshExternalAsync(string id, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(FilePath, ct).ConfigureAwait(false);
        var account = ExternalIdentity(id);
        try
        {
            var old = ReadExternalCredentials(account);
            var (provider, metadata) = await ExternalTransport.DiscoverAsync(account.AuthServer!, ct).ConfigureAwait(false);
            if (provider.ApiRoot != account.AuthServer)
                throw new LauncherOperationException("AUTH_SOURCE_CHANGED", "The account's authentication endpoint changed. Add the new source separately.");
            var response = await ExternalTransport.PostAsync(account.AuthServer!, "authserver/refresh", SessionRequest(old), ct).ConfigureAwait(false);
            var role = response["selectedProfile"] is JsonObject profile ? Profile(profile) : throw InvalidResponse();
            if (role.Id != account.Uuid) throw new LauncherOperationException("AUTH_PROFILE_REMOVED", "The original role is no longer available.");
            var pending = NewAttempt(provider, metadata, account.AuthMethod ?? "password", id);
            pending["userKey"] = account.ExternalUser;
            var credentials = Credentials(pending, response);
            var summary = Summary(pending, await ReadProfileSkinAsync(account.AuthServer!, role, ct).ConfigureAwait(false), id);
            await SaveCredentialsAsync(summary, credentials, ct).ConfigureAwait(false);
            return summary;
        }
        catch (LauncherOperationException ex) when (ex.Code is "AUTH_LOGIN_REQUIRED" or "AUTH_CACHE_UNAVAILABLE" or "AUTH_PROFILE_REMOVED")
        {
            await SaveSummaryAsync(account.WithStatus(ex.Code == "AUTH_PROFILE_REMOVED" ? "profile-removed" : "login-required"), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private JsonObject ReadExternalCredentials(IdentityRecord account)
    {
        try
        {
            var credentials = ProtectedStore.Read(CredentialPath(account.Id!));
            if (Text(credentials, "kind") == "external" && Text(credentials, "apiRoot") == account.AuthServer
                && !string.IsNullOrEmpty(Text(credentials, "minecraftToken")) && !string.IsNullOrEmpty(Text(credentials, "clientToken")))
            {
                return credentials!;
            }
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or InvalidOperationException)
        { throw new LauncherOperationException("AUTH_CACHE_UNAVAILABLE", "The saved external session cannot be opened. Sign in again; the identity is retained."); }

        throw new LauncherOperationException("AUTH_LOGIN_REQUIRED", "No saved session exists for this external account. Sign in again.");
    }
}