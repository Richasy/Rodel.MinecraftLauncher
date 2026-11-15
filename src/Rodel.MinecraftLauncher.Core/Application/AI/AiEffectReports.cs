// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.GameBridge;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>The observation captured before an effect, independently of execution attribution.</summary>
[JsonDerivedType(typeof(AiToolTargetReference))]
[JsonDerivedType(typeof(AiSettingsObservation))]
internal abstract record AiTargetObservation;

/// <summary>Uncoerced target fields from an open tool request; these are references, not validated effects.</summary>
/// <param name="InstanceId">The supplied instance reference.</param>
/// <param name="ReviewId">The supplied review reference.</param>
/// <param name="JobId">The supplied independent job reference.</param>
/// <param name="SessionId">The supplied game-session reference.</param>
internal sealed record AiToolTargetReference(JsonNode? InstanceId, JsonNode? ReviewId, JsonNode? JobId, JsonNode? SessionId) : AiTargetObservation
{
    /// <summary>Gets the distinction between a target reference and a state readback.</summary>
    public string Kind { get; } = "tool_target_reference";
}

/// <summary>The exact selected configuration keys observed at one instant, retaining open setting values.</summary>
/// <param name="InstanceId">The original tool's instance reference, or null for global settings.</param>
/// <param name="Overrides">Only affected overrides, including explicit null and arbitrary extension values.</param>
/// <param name="ObservedUtc">The actual observation timestamp.</param>
internal sealed record AiSettingsObservation(JsonNode? InstanceId, JsonObject Overrides, string ObservedUtc) : AiTargetObservation;

/// <summary>Evidence of an effect; subclasses distinguish state observation from proof of actor or completion.</summary>
[JsonDerivedType(typeof(AiRepairEffectObservation))]
[JsonDerivedType(typeof(AiFileEffectObservation))]
[JsonDerivedType(typeof(AiSkinEffectObservation))]
[JsonDerivedType(typeof(AiUnverifiedEffectObservation))]
[JsonDerivedType(typeof(AiSettingsEffectObservation))]
[JsonDerivedType(typeof(AiGameEffectObservation))]
internal abstract record AiEffectObservation
{
    /// <summary>Gets whether the desired state was actually established by this observation, when applicable.</summary>
    [JsonIgnore]
    public virtual bool? DesiredState => null;
}

/// <summary>A retained native game-operation receipt, with dispatch resolution separate from gameplay-goal observation.</summary>
/// <param name="Status">Whether the exact receipt was available for observation.</param>
/// <param name="DesiredStateObserved">Whether an applied time or health edit includes authoritative state readback.</param>
/// <param name="EffectResolved">Whether the native operation has a known terminal outcome.</param>
/// <param name="Operation">The original operation receipt, when available.</param>
internal sealed record AiGameEffectObservation(string Status, bool DesiredStateObserved, bool EffectResolved, GameOperation? Operation) : AiEffectObservation
{
    /// <summary>Gets the durable Core receipt source rather than an inferred model outcome.</summary>
    public string Source { get; } = "core_game_operation_receipt";
    /// <inheritdoc />
    [JsonIgnore]
    public override bool? DesiredState => DesiredStateObserved;
    /// <summary>Gets the distinction between native dispatch completion, gameplay readback and disk persistence.</summary>
    public string Note { get; } = "An applied receipt proves this native dispatch or input lease completed; time/health edits additionally contain authoritative readback. Read fresh game state and native command feedback to verify a gameplay objective. It does not prove disk persistence. Never replay queued or unknown effects.";
}

/// <summary>An authoritative stored repair receipt, not inferred success.</summary>
/// <param name="DesiredStateObserved">Whether this exact dispatch has known durable evidence.</param>
/// <param name="RepairId">The original incident reference.</param>
/// <param name="Plan">The complete public trial snapshot.</param>
internal sealed record AiRepairEffectObservation(bool DesiredStateObserved, JsonNode RepairId, AiRepairPlanReport Plan) : AiEffectObservation
{
    /// <summary>Gets the observation lifecycle status.</summary>
    public string Status { get; } = "observed";
    /// <summary>Gets the authoritative source without implying repair success.</summary>
    public string Source { get; } = "core_repair_receipt";
    /// <inheritdoc />
    [JsonIgnore]
    public override bool? DesiredState => DesiredStateObserved;
    /// <summary>Gets the interpretation and non-replay boundary of a tagged run or stored write receipt.</summary>
    public string Note { get; } = "A durable plan receipt or tagged verification run resolves dispatch uncertainty, never proves repair success. Continue observing the same run; do not replay writes or launch another game.";
}

/// <summary>The actual file-byte readback after a guarded transaction.</summary>
/// <param name="DesiredStateObserved">Whether the actual hash matches the requested bytes.</param>
/// <param name="Sha256">The observed hash.</param>
/// <param name="BackupPath">The original persisted backup value.</param>
/// <param name="ExecutionAttribution">The original stored actor-attribution evidence.</param>
internal sealed record AiFileEffectObservation(bool DesiredStateObserved, string Sha256, JsonNode? BackupPath, JsonNode ExecutionAttribution) : AiEffectObservation
{
    /// <summary>Gets the observation lifecycle status.</summary>
    public string Status { get; } = "observed";
    /// <summary>Gets the actual byte-readback source.</summary>
    public string Source { get; } = "scoped_file_byte_hash";
    /// <inheritdoc />
    [JsonIgnore]
    public override bool? DesiredState => DesiredStateObserved;
    /// <summary>Gets the distinction between observing bytes and identifying their writer.</summary>
    public string Note { get; } = "Current bytes are an observation, not proof of which actor committed them. Retain the backup for conflicts.";
}

/// <summary>An actual skin candidate-store observation.</summary>
/// <param name="Candidate">The candidate descriptor, distinct from an acceptance receipt.</param>
internal sealed record AiSkinEffectObservation(Skins.SkinProposalInfo Candidate) : AiEffectObservation
{
    /// <summary>Gets the observation lifecycle status.</summary>
    public string Status { get; } = "observed";
    /// <summary>Gets the actual candidate-store source.</summary>
    public string Source { get; } = "core_skin_candidate";
    /// <summary>Gets the distinction between observing a candidate and accepting it.</summary>
    public string Note { get; } = "Candidate state is not acceptance; only its atomically stored receipt proves acceptance.";
}

/// <summary>An explicit requirement for further tool-specific evidence.</summary>
/// <param name="ExecutionAttribution">The original execution-attribution value.</param>
internal sealed record AiUnverifiedEffectObservation(JsonNode ExecutionAttribution) : AiEffectObservation
{
    /// <summary>Gets the requirement for further domain-specific observation.</summary>
    public string Status { get; } = "requires_tool_specific_observation";
    /// <summary>Gets an explicitly unknown desired-state outcome.</summary>
    public bool? DesiredStateObserved { get; }
    /// <summary>Gets the recovery route without claiming completion from a transport receipt.</summary>
    public string Note { get; } = "Use the recorded Core result, job or operation reference for further observation. A transport result is not a claim that a background task or user goal completed.";
}

/// <summary>Actual configuration readback without attributing it to an unproven writer.</summary>
/// <param name="DesiredStateObserved">Whether selected keys match the requested values and removals.</param>
/// <param name="ExecutionAttribution">The original execution-attribution value.</param>
/// <param name="Observed">The selected open setting values actually read.</param>
internal sealed record AiSettingsEffectObservation(bool DesiredStateObserved, JsonNode ExecutionAttribution, AiSettingsObservation Observed) : AiEffectObservation
{
    /// <summary>Gets the observation lifecycle status.</summary>
    public string Status { get; } = "observed";
    /// <summary>Gets the actual configuration readback source.</summary>
    public string Source { get; } = "core_settings_readback";
    /// <inheritdoc />
    [JsonIgnore]
    public override bool? DesiredState => DesiredStateObserved;
    /// <summary>Gets the distinction between matching values and proving a particular dispatch wrote them.</summary>
    public string Note { get; } = "Current state is evidence of the desired configuration, not proof which actor wrote it. Unchanged state does not prove a call never executed.";
}