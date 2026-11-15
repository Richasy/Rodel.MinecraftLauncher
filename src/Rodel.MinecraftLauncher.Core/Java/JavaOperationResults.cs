// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Java;

/// <summary>A newly registered Java resource and the probe observed before publication.</summary>
[JsonDerivedType(typeof(InstalledJavaRegistration))]
[JsonDerivedType(typeof(ImportedJavaRegistration))]
public abstract record JavaRegistrationResult
{
    /// <summary>Gets the registration identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets whether runtime files are owned by the launcher.</summary>
    public bool Managed { get; init; }
    /// <summary>Gets the resolved executable path.</summary>
    public required string JavaPath { get; init; }
    /// <summary>Gets the owned runtime directory, or null for an external association.</summary>
    public string? RuntimeDirectory { get; init; }
    /// <summary>Gets actual probe warnings.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
    /// <summary>Gets the detected Java major version.</summary>
    public int? MajorVersion { get; init; }
    /// <summary>Gets the complete detected version.</summary>
    public string? FullVersion { get; init; }
    /// <summary>Gets diagnostic output from the version probe.</summary>
    public string? VersionOutput { get; init; }
    /// <summary>Gets whether the selected executable completed the probe successfully.</summary>
    public bool IsUsable { get; init; }
}

/// <summary>A registered official runtime, including its unmodified provider release declaration.</summary>
public sealed record InstalledJavaRegistration : JavaRegistrationResult
{
    /// <summary>Gets the requested Mojang runtime component.</summary>
    public required string Component { get; init; }
    /// <summary>Gets the provider platform key.</summary>
    public required string Architecture { get; init; }
    /// <summary>Gets the original provider version object without narrowing its extensible schema.</summary>
    public JsonNode? Version { get; init; }
}

/// <summary>A registered local runtime or archive import.</summary>
public sealed record ImportedJavaRegistration : JavaRegistrationResult
{
    /// <summary>Gets the original executable, directory or archive selected by the caller.</summary>
    public required string Source { get; init; }
}

/// <summary>Reports removal of a registration independently from retention of unrelated files.</summary>
/// <param name="Id">Removed registration identifier.</param>
/// <param name="Removed">Whether the registration was removed.</param>
/// <param name="Managed">Whether the removed registration owned runtime files.</param>
/// <param name="Warnings">Directory cleanup warnings.</param>
public sealed record JavaRemovalResult(string Id, bool Removed, bool Managed, IReadOnlyList<string> Warnings);

/// <summary>Source-generated metadata for Java operation results and their flat wire representations.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JavaRegistrationResult))]
[JsonSerializable(typeof(JavaRemovalResult))]
public sealed partial class JavaOperationJsonContext : JsonSerializerContext;