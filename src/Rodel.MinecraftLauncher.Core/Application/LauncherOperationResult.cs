// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application;

[JsonConverter(typeof(JsonStringEnumConverter<LauncherResultStatus>))]
public enum LauncherResultStatus
{
    [JsonStringEnumMemberName("succeeded")]
    Succeeded,
    [JsonStringEnumMemberName("failed")]
    Failed,
    [JsonStringEnumMemberName("canceled")]
    Canceled
}

/// <summary>Describes a failed operation, with transport details only when a network failure supplied them.</summary>
/// <param name="Code">The stable operation or provider error identifier.</param>
/// <param name="Message">The actual failure explanation retained for the caller.</param>
/// <param name="NetworkReason">The optional classified network failure, absent for ordinary business errors.</param>
/// <param name="HttpStatus">The optional HTTP response status, absent when no HTTP failure occurred.</param>
public sealed record LauncherOperationError(string Code, string Message, string? NetworkReason = null, int? HttpStatus = null);

public sealed record LauncherOperationResult(int SchemaVersion, LauncherResultStatus Status, JsonNode? Data, LauncherOperationError? Error);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LauncherOperationResult))]
internal sealed partial class LauncherOperationJsonContext : JsonSerializerContext;