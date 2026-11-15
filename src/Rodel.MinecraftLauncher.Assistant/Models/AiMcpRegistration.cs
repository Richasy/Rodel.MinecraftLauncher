// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Assistant;

internal sealed record AiMcpRegistration
{
    /// <summary>Gets the Core-owned source identity used for tool qualification.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the configured source display name.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Gets the executable selected by Core's registration validation.</summary>
    public required string ExecutablePath { get; init; }
    /// <summary>Gets the child working directory, or null to use its executable directory.</summary>
    public string? WorkingDirectory { get; init; }
    /// <summary>Gets individual process arguments, never shell command text.</summary>
    public required string[] Arguments { get; init; }
    /// <summary>Gets explicit child environment overrides; null values remove inherited entries.</summary>
    public Dictionary<string, string?>? Environment { get; init; }
    /// <summary>Gets explicit environment-variable credential references, never resolved credential values.</summary>
    public Dictionary<string, string>? CredentialEnvironment { get; init; }
}