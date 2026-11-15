// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Authorizes reversible repairs of one instance and one recorded failed launch, without granting general file or launcher operations.</summary>
public sealed record AiRepairCreate
{
    /// <summary>Gets the instance whose files and settings may be repaired.</summary>
    public required string InstanceId { get; init; }
    /// <summary>Gets the original game run; it remains fixed when verification starts new runs.</summary>
    public string? SessionId { get; init; }
    /// <summary>Gets a failed preparation task when no game run was created.</summary>
    public string? JobId { get; init; }
}

/// <summary>Requests an evidence-backed, minimal repair plan. Planning never changes game files.</summary>
public sealed record AiRepairPlan
{
    internal static readonly string[] Kinds = ["disable_mod", "java", "memory", "dependencies"];

    /// <summary>Gets the incident authorized by the player.</summary>
    public required string RepairId { get; init; }
    /// <summary>Gets disable_mod, java, memory or dependencies.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets an installed mod file name or an existing Java executable, according to Kind.</summary>
    public string? Target { get; init; }
    /// <summary>Gets the proposed maximum heap in MiB for a memory repair.</summary>
    public int? MemoryMb { get; init; }
    /// <summary>Gets the model's explanation linking this trial to the observed evidence; it grants no permission.</summary>
    public required string Reason { get; init; }
}

/// <summary>Identifies an incident without permitting caller-selected instances, paths or launch arguments.</summary>
public sealed record AiRepairRead
{
    /// <summary>Gets the durable incident identifier.</summary>
    public required string RepairId { get; init; }
}

/// <summary>Applies, verifies or restores exactly one frozen repair plan.</summary>
public sealed record AiRepairAction
{
    /// <summary>Gets the authorized incident.</summary>
    public required string RepairId { get; init; }
    /// <summary>Gets the frozen plan whose actual effects must be inspected before reuse.</summary>
    public required string PlanId { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AiRepairCreate))]
[JsonSerializable(typeof(AiRepairPlan))]
[JsonSerializable(typeof(AiRepairRead))]
[JsonSerializable(typeof(AiRepairAction))]
[JsonSerializable(typeof(AiRepairReport))]
[JsonSerializable(typeof(AiRepairPlanReport))]
public sealed partial class AiRepairJsonContext : JsonSerializerContext;