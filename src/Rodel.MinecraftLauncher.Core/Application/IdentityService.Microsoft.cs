// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class IdentityService
{
    private string CredentialPath(string id) => Path.Combine(paths.Root, "account-secrets", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".json");
    private string PendingCredentialPath(string clientId) => CredentialPath("pending:" + clientId);

    /// <summary>Reads the selected account and the complete stored account inventory without acquiring credentials.</summary>
    public IdentitySelection GetSelection() => new(new Configuration.SettingsService(paths).Get("accountId").Value?.GetValue<string>(), List());

    /// <summary>Selects an existing account or clears the default without changing credentials or running games.</summary>
    public async Task<IdentitySelection> SelectAsync(string? id, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: FilePath, cancellationToken: ct).ConfigureAwait(false);
        if (id is not null) _ = Get(id);
        await new Configuration.SettingsService(paths).SetAsync("accountId", id is null ? null : JsonValue.Create(id), cancellationToken: ct).ConfigureAwait(false);
        return GetSelection();
    }

    /// <summary>Authenticates through the selected Microsoft flow and atomically stores the verified profile and protected credentials.</summary>
    public async Task<IdentityRecord> LoginMicrosoftAsync(MicrosoftLoginOptions? options = null, CancellationToken ct = default)
    {
        options ??= new();
        var clientId = MicrosoftAuthentication.ResolveClientId(options.ClientId);
        using var lease = await ResourceLease.AcquireAsync(resource: FilePath, cancellationToken: ct).ConfigureAwait(false);
        JsonObject? credentials = null;
        if (options.AccountId is { } selected)
        {
            var existing = Get(selected);
            if (existing.Type != "microsoft") throw new ArgumentException("Select a Microsoft account to sign in again.");
            credentials = ReadCredentials(selected, clientId, required: false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        (IdentityRecord Summary, JsonObject Credentials) result;
        try
        {
            result = await new MicrosoftAuthentication(clientId, pendingPath: PendingCredentialPath(clientId)).SignInAsync(options, credentials, timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new LauncherOperationException("AUTH_TIMEOUT", "The sign-in deadline expired. Retry sign-in.");
        }

        if (options.AccountId is { } expected && result.Summary.Id != expected)
            throw new LauncherOperationException("AUTH_IDENTITY_MISMATCH", "A different Microsoft account was selected. Add it as a separate account instead.");
        await SaveCredentialsAsync(result.Summary, result.Credentials, ct).ConfigureAwait(false);
        File.Delete(PendingCredentialPath(clientId));
        return result.Summary;
    }

    /// <summary>Completes a retained sign-in attempt without replacing a different expected player identity.</summary>
    public async Task<IdentityRecord> RetryMicrosoftLoginAsync(string? expectedAccountId = null, CancellationToken ct = default)
    {
        var clientId = MicrosoftAuthentication.ResolveClientId();
        using var lease = await ResourceLease.AcquireAsync(resource: FilePath, cancellationToken: ct).ConfigureAwait(false);
        var pendingPath = PendingCredentialPath(clientId);
        var credentials = ProtectedStore.Read(pendingPath) ?? throw new LauncherOperationException("AUTH_LOGIN_REQUIRED", "No unfinished sign-in exists for this application. Sign in again.");
        var summary = await new MicrosoftAuthentication(clientId).RefreshAsync(credentials, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if ((expectedAccountId ?? credentials["expectedAccountId"]?.GetValue<string>()) is { } expected && summary.Id != expected)
            throw new LauncherOperationException("AUTH_IDENTITY_MISMATCH", "A different account was selected. Add it separately.");
        await SaveCredentialsAsync(summary, credentials, ct).ConfigureAwait(false);
        File.Delete(pendingPath);
        return summary;
    }

    /// <summary>Refreshes a Microsoft profile and protected credentials, retaining a recoverable account when sign-in is required.</summary>
    public async Task<IdentityRecord> RefreshMicrosoftAsync(string id, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: FilePath, cancellationToken: ct).ConfigureAwait(false);
        var summary = Get(id);
        if (summary.Type != "microsoft") return summary;
        var session = await GetMicrosoftSessionAsync(summary, reuseCachedSession: false, ct).ConfigureAwait(false);
        return session.Summary;
    }

    private async Task<(IdentityRecord Summary, JsonObject Credentials)> GetMicrosoftSessionAsync(IdentityRecord summary, bool reuseCachedSession, CancellationToken ct)
    {
        var id = summary.Id!;
        var clientId = MicrosoftAuthentication.ResolveClientId();
        try
        {
            var credentials = ReadCredentials(id, clientId)!;
            if (reuseCachedSession && CanReuseMicrosoftSession(summary, credentials)) return (summary, credentials);
            var updated = await new MicrosoftAuthentication(clientId).RefreshAsync(credentials, ct).ConfigureAwait(false);
            if (updated.Id != id) throw new LauncherOperationException("AUTH_IDENTITY_MISMATCH", "The refreshed session belongs to a different player. Sign in again.");
            ct.ThrowIfCancellationRequested();
            await SaveCredentialsAsync(updated, credentials, ct).ConfigureAwait(false);
            return (updated, credentials);
        }
        catch (LauncherOperationException ex) when (ex.Code is "AUTH_LOGIN_REQUIRED" or "AUTH_CACHE_UNAVAILABLE")
        {
            await SaveSummaryAsync(summary.WithStatus("login-required"), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static bool CanReuseMicrosoftSession(IdentityRecord summary, JsonObject credentials) =>
        summary.Status == "ready"
        && Guid.TryParseExact(summary.Uuid, "N", out _)
        && credentials["uuid"] is JsonValue uuid && uuid.TryGetValue<string>(out var player)
        && string.Equals(player, summary.Uuid, StringComparison.OrdinalIgnoreCase)
        && credentials["minecraftToken"] is JsonValue token && token.TryGetValue<string>(out var accessToken)
        && !string.IsNullOrWhiteSpace(accessToken)
        && credentials["expiresAt"] is JsonValue expiry && expiry.TryGetValue<string>(out var expiresAt)
        && DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiration)
        // Leave time for process startup and reasonable clock skew; use the service's expiry, not a fixed token lifetime.
        && expiration > DateTimeOffset.UtcNow.AddMinutes(5);

    internal async Task<Launch.GameLaunchCredentials> GetLaunchCredentialsAsync(string id, CancellationToken ct, bool useCachedExternalSession = false)
    {
        if (Get(id).Type == "external")
        {
            if (useCachedExternalSession)
            {
                if (!CanUseCachedExternalSession(id))
                    throw new LauncherOperationException("AUTH_CACHE_UNAVAILABLE", "This role has no complete cached external session and login component.");
            }
            else
            {
                await RefreshExternalAsync(id, ct).ConfigureAwait(false);
            }

            using var externalLease = await ResourceLease.AcquireAsync(FilePath, ct).ConfigureAwait(false);
            return Launch.GameLaunchCredentials.Read(ReadExternalCredentials(ExternalIdentity(id)));
        }

        // Decide and, if needed, renew under the same lease so concurrent launches see the committed session.
        using var lease = await ResourceLease.AcquireAsync(resource: FilePath, cancellationToken: ct).ConfigureAwait(false);
        var session = await GetMicrosoftSessionAsync(Get(id), reuseCachedSession: true, ct).ConfigureAwait(false);
        return Launch.GameLaunchCredentials.Read(session.Credentials);
    }

    private JsonObject? ReadCredentials(string id, string clientId, bool required = true)
    {
        try
        {
            var data = ProtectedStore.Read(CredentialPath(id));
            if (data?["clientId"]?.GetValue<string>() == clientId) return data;
        }
        catch (Exception ex) when (ex is CryptographicException or System.Text.Json.JsonException or FormatException or InvalidOperationException)
        {
            if (!required) return null;
            throw new LauncherOperationException("AUTH_CACHE_UNAVAILABLE", "The saved credentials cannot be opened. Sign in again; the account and game files are retained.");
        }

        if (!required) return null;
        throw new LauncherOperationException("AUTH_LOGIN_REQUIRED", "No saved session exists for this application and account. Sign in again.");
    }

    private async Task SaveCredentialsAsync(IdentityRecord summary, JsonObject credentials, CancellationToken ct)
    {
        var id = summary.Id!;
        var path = CredentialPath(id);
        JsonObject? previous;
        try { previous = ProtectedStore.Read(path); }
        catch (Exception ex) when (ex is CryptographicException or System.Text.Json.JsonException or FormatException or InvalidOperationException) { previous = null; }

        await ProtectedStore.WriteAsync(path, credentials, ct).ConfigureAwait(false);
        bool firstAccount;
        try { firstAccount = await SaveSummaryAsync(summary, ct).ConfigureAwait(false); }
        catch
        {
            if (previous is null) File.Delete(path);
            else await ProtectedStore.WriteAsync(path, previous, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        // A default-setting failure must retain the fully registered account and its protected session for recovery.
        await SelectFirstAccountAsync(firstAccount, id, ct).ConfigureAwait(false);
    }

    private async Task<bool> SaveSummaryAsync(IdentityRecord summary, CancellationToken ct)
    {
        var document = File.Exists(FilePath) ? JsonFiles.ReadObject(FilePath) : [];
        var entries = document["identities"] as JsonArray ?? [];
        var firstAccount = entries.Count == 0;
        var id = summary.Id!;
        var encoded = JsonSerializer.SerializeToNode(summary, ApplicationJsonContext.Default.IdentityRecord)!.AsObject();
        var existing = entries.OfType<JsonObject>().FirstOrDefault(e => e["id"]?.GetValue<string>() == id);
        if (existing is null) entries.Add((JsonNode)encoded);
        else foreach (var pair in encoded) existing[pair.Key] = pair.Value?.DeepClone();
        document["identities"] = entries.Parent is null ? entries : entries.DeepClone();
        await JsonFiles.WriteAsync(FilePath, document, ct).ConfigureAwait(false);
        return firstAccount;
    }
}