// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiMcpUpdate
{
    public string? Id { get; init; }
    public required long ExpectedRevision { get; init; }
    public required string DisplayName { get; init; }
    public required string ExecutablePath { get; init; }
    public string? WorkingDirectory { get; init; }
    public bool Enabled { get; set; } = true;
    public List<string> Arguments { get; set; } = [];
    public Dictionary<string, string?> Environment { get; set; } = [];
    public Dictionary<string, string> CredentialEnvironment { get; set; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiFileRootUpdate
{
    public string? Id { get; init; }
    public required long ExpectedRevision { get; init; }
    public required string Path { get; init; }
    public string Mode { get; set; } = "read_only";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiSkillDirectoryUpdate
{
    public string? Id { get; init; }
    public required long ExpectedRevision { get; init; }
    public required string Path { get; init; }
    public bool Enabled { get; set; } = true;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AiMcpUpdate))]
[JsonSerializable(typeof(AiFileRootUpdate))]
[JsonSerializable(typeof(AiSkillDirectoryUpdate))]
public sealed partial class AiExtensionJsonContext : JsonSerializerContext;