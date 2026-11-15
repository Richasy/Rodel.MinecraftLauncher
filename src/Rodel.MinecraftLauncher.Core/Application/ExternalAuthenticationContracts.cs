// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>A discovered Yggdrasil service, without account credentials.</summary>
/// <param name="ApiRoot">Canonical HTTP or HTTPS API root.</param>
/// <param name="Name">Service-supplied display name, falling back to the host.</param>
public sealed record ExternalAuthProvider(string ApiRoot, string Name);

/// <summary>A player profile offered by the selected authentication service.</summary>
/// <param name="Id">Unsigned UUID supplied by that service.</param>
/// <param name="Name">Current player name.</param>
/// <param name="SkinUrl">Optional skin texture from profile properties.</param>
public sealed record ExternalAuthProfile(string Id, string Name, string? SkinUrl = null);

/// <summary>A password sign-in request. Hosts must not persist or log this object.</summary>
public sealed record ExternalLoginOptions
{
    /// <summary>Gets the site or API address; omitted when reauthenticating a registered account.</summary>
    public string? Address { get; init; }
    /// <summary>Gets the user-supplied login identifier.</summary>
    public required string Username { get; init; }
    /// <summary>Gets the password used only for this request.</summary>
    public required string Password { get; init; }
    /// <summary>Gets the existing account whose source and role must be retained.</summary>
    public string? AccountId { get; init; }
    /// <inheritdoc/>
    public override string ToString() => "External login request (credentials redacted)";
}

/// <summary>A public sign-in snapshot. Tokens and the private device code remain DPAPI protected.</summary>
public sealed record ExternalLoginResult
{
    /// <summary>Gets the opaque attempt identifier used for polling, role selection or cancellation.</summary>
    public required string AttemptId { get; init; }
    /// <summary>Gets device, roles, no-roles or complete.</summary>
    public required string Stage { get; init; }
    /// <summary>Gets the resolved authentication source.</summary>
    public required ExternalAuthProvider Provider { get; init; }
    /// <summary>Gets the available roles, without authentication tokens.</summary>
    public IReadOnlyList<ExternalAuthProfile> Profiles { get; init; } = [];
    /// <summary>Gets the registered identity after successful completion; registration does not select it.</summary>
    public IdentityRecord? Account { get; init; }
    /// <summary>Gets the user-facing device code, when waiting for LittleSkin authorization.</summary>
    public string? UserCode { get; init; }
    /// <summary>Gets the authorization page for manual code entry.</summary>
    public string? VerificationUri { get; init; }
    /// <summary>Gets the authorization page containing the user code, if supplied by the service.</summary>
    public string? VerificationUriComplete { get; init; }
    /// <summary>Gets the actual expiry of the pending request.</summary>
    public DateTimeOffset ExpiresAt { get; init; }
}

internal sealed record YggdrasilRequest
{
    /// <summary>Gets the identifier for password authentication.</summary>
    public string? Username { get; init; }
    /// <summary>Gets the transient password.</summary>
    public string? Password { get; init; }
    /// <summary>Gets the service-issued access token for refresh or validation.</summary>
    public string? AccessToken { get; init; }
    /// <summary>Gets the associated client token.</summary>
    public string? ClientToken { get; init; }
    /// <summary>Gets whether user metadata should be returned.</summary>
    public bool RequestUser { get; init; } = true;
    /// <summary>Gets the Minecraft agent declaration for password login.</summary>
    public YggdrasilAgent? Agent { get; init; }
    /// <summary>Gets the role to bind an unbound token to.</summary>
    public YggdrasilSelectedProfile? SelectedProfile { get; init; }
    /// <summary>Gets the LittleSkin role to exchange for a Minecraft token.</summary>
    public string? Uuid { get; init; }
}

internal sealed record YggdrasilAgent
{
    /// <summary>Gets the protocol agent name.</summary>
    public string Name { get; init; } = "Minecraft";
    /// <summary>Gets the protocol agent version.</summary>
    public int Version { get; init; } = 1;
}

/// <summary>A role binding sent to the service.</summary>
/// <param name="Id">Unsigned role UUID.</param>
/// <param name="Name">Service-returned role name.</param>
internal sealed record YggdrasilSelectedProfile(string Id, string Name);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(YggdrasilRequest))]
[JsonSerializable(typeof(ExternalAuthProvider))]
[JsonSerializable(typeof(ExternalAuthProfile))]
[JsonSerializable(typeof(ExternalLoginResult))]
internal partial class ExternalAuthenticationJsonContext : JsonSerializerContext;