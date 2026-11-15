// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>The exact tool identity frozen into a durable mutation intent.</summary>
[JsonDerivedType(typeof(AiExternalToolIdentity))]
[JsonDerivedType(typeof(AiManagedFileIdentity))]
internal abstract record AiToolSourceIdentity;

/// <summary>The external registration, tool and schema hashes used to reject replay under a changed program.</summary>
/// <param name="SourceId">The registered external source.</param>
/// <param name="RegistrationHash">The complete original registration fingerprint.</param>
/// <param name="ActualName">The original server tool name.</param>
/// <param name="SchemaHash">The actual offered schema fingerprint.</param>
internal sealed record AiExternalToolIdentity(string SourceId, string RegistrationHash, string ActualName, string SchemaHash) : AiToolSourceIdentity;

/// <summary>The packaged file tool identity and optional exact source root frozen at invocation.</summary>
/// <param name="SourceId">The packaged component identity.</param>
/// <param name="ActualName">The actual tool name.</param>
/// <param name="ComponentSha256">The validated executable hash.</param>
/// <param name="SchemaSha256">The effective advertised schema hash.</param>
internal sealed record AiManagedFileIdentity(string SourceId, string ActualName, string ComponentSha256, string SchemaSha256) : AiToolSourceIdentity
{
    /// <summary>Gets the actual selected source root only after root selection, absent from catalog identity.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RootPath { get; init; }
}

internal static class AiToolSourceIdentities
{
    internal static JsonNode? Snapshot(AiToolSourceIdentity? identity)
        => identity is null ? null : JsonSerializer.SerializeToNode(identity, AiEffectJsonContext.Default.AiToolSourceIdentity);
}