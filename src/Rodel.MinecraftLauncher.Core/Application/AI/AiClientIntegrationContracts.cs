// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiClientIntegrationRequest
{
    public required string Client { get; init; }
    public string Action { get; set; } = "install";
    public string? ClientExecutable { get; init; }
    public string? ClientConfigRoot { get; init; }
    public string? SkillsRoot { get; init; }
    public string? InstallationRoot { get; init; }
    /// <summary>Gets the absolute independently installed CLI executable to register; it is referenced in place and never copied from the App package.</summary>
    public string? LauncherExecutable { get; init; }
    public bool SkillsOnly { get; init; }
    public bool ReplaceConflicts { get; init; }
    public string? PlanFingerprint { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(AiClientIntegrationRequest))]
public sealed partial class AiClientIntegrationJsonContext : JsonSerializerContext;