// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Servers;

/// <summary>A usable SRV target, including the DNS ordering and cache information.</summary>
public sealed record ServerCandidate
{
    /// <summary>Gets the target hostname without its terminating DNS dot.</summary>
    public required string Host { get; init; }
    /// <summary>Gets the target TCP port.</summary>
    public int Port { get; init; }
    /// <summary>Gets the SRV priority.</summary>
    public int Priority { get; init; }
    /// <summary>Gets the SRV selection weight.</summary>
    public int Weight { get; init; }
    /// <summary>Gets the DNS time to live in seconds.</summary>
    public uint Ttl { get; init; }
}

/// <summary>Describes address parsing and optional SRV resolution without opening a game connection.</summary>
public record ServerResolution
{
    /// <summary>Gets the address supplied by the caller.</summary>
    public required string Address { get; init; }
    /// <summary>Gets the parsed handshake hostname, absent when resolution timed out.</summary>
    public string? Host { get; init; }
    /// <summary>Gets the parsed port, absent when resolution timed out.</summary>
    public int? Port { get; init; }
    /// <summary>Gets the selected connection hostname.</summary>
    public string? ConnectHost { get; set; }
    /// <summary>Gets the selected connection port.</summary>
    public int? ConnectPort { get; set; }
    /// <summary>Gets whether a usable SRV target was selected.</summary>
    public bool? SrvResolved { get; init; }
    /// <summary>Gets ordered SRV candidates, absent if resolution did not finish.</summary>
    public IReadOnlyList<ServerCandidate>? Candidates { get; init; }
    /// <summary>Gets the explicit DNS service-unavailable indication, when present.</summary>
    public bool? ServiceUnavailable { get; init; }
    /// <summary>Gets a recoverable DNS warning, when present.</summary>
    public string? SrvWarning { get; init; }
}

/// <summary>Describes one attempted connection in a server status query.</summary>
public sealed record ServerStatusAttempt
{
    /// <summary>Gets the attempted hostname.</summary>
    public required string Host { get; init; }
    /// <summary>Gets the attempted TCP port.</summary>
    public int Port { get; init; }
    /// <summary>Gets whether the server returned a valid status object.</summary>
    public bool Online { get; init; }
    /// <summary>Gets the failure category; successful attempts explicitly report null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ErrorCode { get; init; }
}

/// <summary>Reports a modern Java server-list ping without authenticating or joining.</summary>
public sealed record ServerStatus : ServerResolution
{
    /// <summary>Gets whether a valid server status response was received.</summary>
    public bool Online { get; set; }
    /// <summary>Gets the open, unmodified server response, including third-party fields.</summary>
    public JsonObject? Status { get; set; }
    /// <summary>Gets the measured ping round-trip time in milliseconds, when available.</summary>
    public double? LatencyMs { get; set; }
    /// <summary>Gets a ping failure after a successful status response, when present.</summary>
    public string? PingWarning { get; set; }
    /// <summary>Gets the final failure category, when present.</summary>
    public string? ErrorCode { get; set; }
    /// <summary>Gets the final failure explanation, when present.</summary>
    public string? Error { get; set; }
    /// <summary>Gets completed connection attempts, absent if no connection was attempted.</summary>
    public IReadOnlyList<ServerStatusAttempt>? Attempts { get; set; }
}

/// <summary>Confirms removal of one favorite while returning all metadata from the removed record.</summary>
public sealed record ServerFavoriteRemoval
{
    /// <summary>Gets whether the favorite was removed.</summary>
    public bool Removed { get; init; }
    /// <summary>Gets the removed favorite including its original extension data.</summary>
    public required ServerFavorite Server { get; init; }
    /// <summary>Gets dedicated clients released to ordinary local ownership by the removal.</summary>
    public IReadOnlyList<string> ReleasedClients { get; init; } = [];
}

/// <summary>Source-generated serialization for the server module's fixed reports.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ServerResolution))]
[JsonSerializable(typeof(ServerStatus))]
[JsonSerializable(typeof(ServerFavorite))]
[JsonSerializable(typeof(ServerFavoriteRemoval))]
[JsonSerializable(typeof(IReadOnlyList<ServerFavoriteEntry>))]
public sealed partial class ServerJsonContext : JsonSerializerContext;