// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Java;

/// <summary>Describes an executable's observed Java version, usability and process properties.</summary>
public sealed record JavaInstallation(string Path, int? MajorVersion, string VersionOutput, bool IsUsable, string? Architecture = null, int? Bitness = null)
{
    /// <summary>Gets the complete version reported by the runtime.</summary>
    public string? FullVersion { get; init; }
    /// <summary>Gets the runtime's default file encoding.</summary>
    public string? FileEncoding { get; init; }
    /// <summary>Gets the runtime's native platform encoding, when reported.</summary>
    public string? NativeEncoding { get; init; }
    /// <summary>Gets the runtime's standard-output encoding, when reported.</summary>
    public string? StdoutEncoding { get; init; }
    /// <summary>Gets the runtime's standard-error encoding, when reported.</summary>
    public string? StderrEncoding { get; init; }
    /// <summary>Gets the Java home reported by the executable.</summary>
    public string? JavaHome { get; init; }
    /// <summary>Gets the wrapper path through which automatic discovery found this executable.</summary>
    public string? DiscoveryPath { get; init; }
    /// <summary>Gets ancillary cache or property-probe failures without hiding an otherwise usable Java.</summary>
    public string[] Warnings { get; init; } = [];
}