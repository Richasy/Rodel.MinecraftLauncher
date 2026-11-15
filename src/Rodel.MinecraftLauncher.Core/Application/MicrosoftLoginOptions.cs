// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>Chooses the foreground Microsoft authorization UI; all methods share the Xbox and Minecraft verification flow.</summary>
public enum MicrosoftLoginMethod
{
    /// <summary>Uses Windows Web Account Manager with the host's real parent window.</summary>
    System,
    /// <summary>Uses the system browser with a local loopback callback.</summary>
    Browser,
    /// <summary>Displays a device code through the foreground prompt receiver.</summary>
    DeviceCode
}

/// <summary>Interactive host options. Never persist this request as a background job.</summary>
public sealed record MicrosoftLoginOptions
{
    /// <summary>Overrides the public application ID for this sign-in; otherwise reads the local process or Windows user environment.</summary>
    public string? ClientId { get; init; }
    /// <summary>Selects the authorization UI. Hosts must explicitly choose System to use WAM.</summary>
    public MicrosoftLoginMethod Method { get; init; } = MicrosoftLoginMethod.Browser;
    /// <summary>Supplies the borrowed HWND of the interactive host. Required for System and never retained after the operation.</summary>
    public nint ParentWindow { get; init; }
    /// <summary>Restricts reauthentication to an existing launcher identity, rejecting a different Minecraft player.</summary>
    public string? AccountId { get; init; }
    /// <summary>Receives foreground status and one-time device-code prompts; hosts must not persist these prompts in task logs.</summary>
    public IProgress<MicrosoftLoginPrompt>? Prompt { get; init; }
}

/// <summary>Only for the foreground authentication UI; do not put device codes into task logs.</summary>
public sealed record MicrosoftLoginPrompt(string Stage, string? VerificationUri = null, string? UserCode = null);