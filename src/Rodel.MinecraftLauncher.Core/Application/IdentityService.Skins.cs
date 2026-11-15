// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Core.Application.ExternalAuthTransport;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class IdentityService
{
    /// <summary>Queries an online role's current skin and updates only its stored skin URL, without renewing credentials or changing login status.</summary>
    /// <remarks>Uses a usable cached Minecraft token or the public UUID profile for Microsoft accounts, and the original source for external roles. Network failures retain the stored identity. A concurrent login, skin update or removal takes precedence over this query.</remarks>
    /// <param name="id">The registered identity to refresh; offline identities are returned unchanged.</param>
    /// <param name="ct">Cancels the optional query and prevents a late response from being committed.</param>
    /// <returns>The current stored identity after the query, including any newer concurrent update.</returns>
    public async Task<IdentityRecord> RefreshSkinAsync(string id, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        IdentityRecord account;
        using (await ResourceLease.AcquireAsync(FilePath, deadline.Token).ConfigureAwait(false))
            account = Get(id);
        if (account.Type is not ("microsoft" or "external")) return account;
        if (!Guid.TryParseExact(account.Uuid, "N", out _)) throw InvalidResponse();

        var skin = account.Type == "microsoft"
            ? await ReadMicrosoftSkinAsync(account, deadline.Token).ConfigureAwait(false)
            : await ReadRoleSkinAsync(new Uri(Address(account.AuthServer!), "sessionserver/session/minecraft/profile/" + account.Uuid), account.Uuid!, deadline.Token).ConfigureAwait(false);
        deadline.Token.ThrowIfCancellationRequested();

        // Never hold the registry lease across optional network I/O: selection, login and removal stay responsive.
        using var lease = await ResourceLease.AcquireAsync(FilePath, deadline.Token).ConfigureAwait(false);
        var current = Get(id);
        if (current.Type != account.Type || current.Uuid != account.Uuid || current.AuthServer != account.AuthServer
            || current.VerifiedAt != account.VerifiedAt || current.SkinUrl != account.SkinUrl)
        {
            return current;
        }

        if (current.SkinUrl == skin) return current;
        var additional = new Dictionary<string, JsonElement>(current.AdditionalData);
        additional.Remove("skinUrl");
        var updated = current with { SkinUrl = skin, AdditionalData = additional };
        if (skin is null) updated.PreserveNull("skinUrl");
        await SaveSummaryAsync(updated, deadline.Token).ConfigureAwait(false);
        return updated;
    }

    private async Task<string?> ReadMicrosoftSkinAsync(IdentityRecord account, CancellationToken ct)
    {
        JsonObject? credentials = null;
        try { credentials = ProtectedStore.Read(CredentialPath(account.Id!)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or InvalidOperationException or IOException or UnauthorizedAccessException)
        { /* Public skin metadata does not require a usable login cache. */ }

        if (credentials is not null && CanReuseMicrosoftSession(account, credentials))
        {
            using var response = await ExternalTransport.GetAsync(new("https://api.minecraftservices.com/minecraft/profile"), ct, Text(credentials, "minecraftToken")).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
            {
                var profile = await ReadAsync(response, [Text(credentials, "minecraftToken")], ct).ConfigureAwait(false) as JsonObject ?? throw InvalidResponse();
                if (!string.Equals(Text(profile, "id"), account.Uuid, StringComparison.OrdinalIgnoreCase) || profile["skins"] is not JsonArray skins)
                    throw InvalidResponse();
                var active = skins.OfType<JsonObject>().FirstOrDefault(value => Text(value, "state") == "ACTIVE");
                return ValidateSkinUrl(active is null ? null : Text(active, "url") ?? throw InvalidResponse(), microsoft: true);
            }
        }

        return ValidateSkinUrl(await ReadRoleSkinAsync(new("https://sessionserver.mojang.com/session/minecraft/profile/" + account.Uuid), account.Uuid!, ct).ConfigureAwait(false), microsoft: true);
    }

    private async Task<string?> ReadRoleSkinAsync(Uri address, string uuid, CancellationToken ct)
    {
        using var response = await ExternalTransport.GetAsync(address, ct).ConfigureAwait(false);
        var profile = await ReadAsync(response, ct: ct).ConfigureAwait(false) as JsonObject ?? throw InvalidResponse();
        if (!string.Equals(Profile(profile).Id, uuid, StringComparison.OrdinalIgnoreCase)) throw InvalidResponse();
        return ReadRoleSkinUrl(profile);
    }

    private static string? ReadRoleSkinUrl(JsonNode profile)
    {
        if (profile["properties"] is null) return null;
        if (profile["properties"] is not JsonArray properties) throw InvalidResponse();
        var property = properties.OfType<JsonObject>().FirstOrDefault(value => Text(value, "name") == "textures");
        if (property is null) return null;
        var encoded = Text(property, "value") ?? throw InvalidResponse();
        var texture = JsonNode.Parse(Convert.FromBase64String(encoded)) as JsonObject ?? throw InvalidResponse();
        if (texture["textures"] is not JsonObject textures) throw InvalidResponse();
        if (textures["SKIN"] is null) return null;
        return ValidateSkinUrl(Text(textures["SKIN"], "url") ?? throw InvalidResponse());
    }

    private static string? ValidateSkinUrl(string? value, bool microsoft = false)
    {
        if (value is null) return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0
            || (microsoft && !uri.Host.Equals("textures.minecraft.net", StringComparison.OrdinalIgnoreCase)))
        {
            throw InvalidResponse();
        }

        return microsoft ? new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri.AbsoluteUri : uri.AbsoluteUri;
    }
}