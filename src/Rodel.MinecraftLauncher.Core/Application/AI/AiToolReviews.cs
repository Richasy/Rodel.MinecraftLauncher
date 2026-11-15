// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Launch;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>A frozen operation review, distinct from its persisted approval document and from permission to execute.</summary>
internal abstract record AiToolReview;

/// <summary>A real tool's externally observed payload, retained without imposing another domain's schema.</summary>
/// <param name="Observation">The complete observed MCP payload, not a Core-authored fixed receipt.</param>
internal sealed record AiObservedToolReview(JsonObject Observation) : AiToolReview;

/// <summary>The exact typed content review checked before tool dispatch.</summary>
/// <param name="Review">The content subsystem's frozen review and drift result.</param>
internal sealed record AiContentToolReview(ContentReview Review) : AiToolReview;

/// <summary>Permission to execute the exact operation arguments, without pretending to freeze domain state.</summary>
/// <param name="Name">The offered tool name.</param>
/// <param name="Arguments">The immutable open arguments under that tool's actual schema.</param>
internal sealed record AiOperationPermissionReview(string Name, JsonObject Arguments) : AiToolReview
{
    /// <summary>Gets the review's authority boundary.</summary>
    public string Kind { get; } = "operation_permission";
}

/// <summary>The explicit unconstrained CLI-operation boundary displayed before approval.</summary>
/// <param name="DataRoot">The launcher data root bound to dispatch.</param>
/// <param name="Arguments">The exact proposed CLI argument data, before command validation.</param>
internal sealed record AiExecuteReview(string DataRoot, JsonNode Arguments) : AiToolReview
{
    /// <summary>Gets the unconstrained command review kind.</summary>
    public string Kind { get; } = "unconstrained_launcher_command";
    /// <summary>Gets the filesystem and process authority explicitly disclosed to the player.</summary>
    public string Scope { get; } = "This command may read/write outside instance scope or start independent work. File/network effects are not sandboxed.";
    /// <summary>Gets the exact frozen fields and what they do not freeze.</summary>
    public string ReviewBoundary { get; } = "Only the exact CLI argument array and data root are frozen. Input files and remote state may change; this is not a frozen domain review.";
    /// <summary>Gets the current conversation permission policy without creating another approval gate.</summary>
    public string Authorization { get; } = "Uses the conversation's current operation permission; safe mode requests a decision unless this tool is already allowed.";
    /// <summary>Gets whether automatic replay is permitted; always false.</summary>
    public bool AutomaticMutationReplay { get; }
}

/// <summary>The selected game-process identity, preserving unreadable persisted values without coercion.</summary>
internal sealed record AiProcessStopReview : AiToolReview
{
    /// <summary>Gets the recognized session identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }
    /// <summary>Gets the recognized owning instance.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstanceId { get; init; }
    /// <summary>Gets the recognized process identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ProcessId { get; init; }
    /// <summary>Gets the recorded process creation identity.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProcessStartUtc { get; init; }
    /// <summary>Gets the observed session lifecycle state.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }
    /// <summary>Gets the recorded game directory.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GameDirectory { get; init; }
    /// <summary>Gets or sets explicit nulls or noncanonical original values for these same six selected fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];

    internal static AiProcessStopReview Capture(SessionStatus session)
    {
        var additional = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        void Preserve(string name, bool missing)
        {
            if (missing)
            {
                additional[name] = session.AdditionalData.TryGetValue(name, out var original)
                ? original.Clone() : JsonSerializer.SerializeToElement((string?)null, AiJsonContext.Default.String);
            }
        }

        Preserve("id", session.Id is null);
        Preserve("instanceId", session.InstanceId is null);
        Preserve("processId", session.ProcessId is null);
        Preserve("processStartUtc", session.ProcessStartUtc is null);
        Preserve("status", session.Status is null);
        Preserve("gameDirectory", session.GameDirectory is null);
        return new()
        {
            Id = session.Id,
            InstanceId = session.InstanceId,
            ProcessId = session.ProcessId,
            ProcessStartUtc = session.ProcessStartUtc,
            Status = session.Status,
            GameDirectory = session.GameDirectory,
            AdditionalData = additional
        };
    }
}

/// <summary>The exact proposed file access shown by the host.</summary>
/// <param name="ToolInvocationId">The original tool invocation requesting access.</param>
/// <param name="Path">The actual local target.</param>
/// <param name="ParentPath">The parent offered for a reusable grant.</param>
/// <param name="Mode">The requested read_only or read_write mode.</param>
internal sealed record AiPathAccessRequest(string ToolInvocationId, string Path, string? ParentPath, string Mode);

/// <summary>A separate review for host file access, not reusable operation permission.</summary>
/// <param name="Access">The exact path and mode being reviewed.</param>
internal sealed record AiPathAccessReview(AiPathAccessRequest Access) : AiToolReview
{
    /// <summary>Gets the path-access review kind.</summary>
    public string Kind { get; } = "path_access";
}

/// <summary>One actually resolved private endpoint bound to a single web call.</summary>
/// <param name="Url">The validated absolute URL.</param>
/// <param name="Host">The resolved host.</param>
/// <param name="Port">The requested endpoint port.</param>
/// <param name="Addresses">The frozen sorted DNS result.</param>
internal sealed record AiWebAccessTarget(string Url, string Host, int Port, string[] Addresses);

/// <summary>The exact private endpoints reviewed for one web invocation.</summary>
/// <param name="Targets">Resolved private targets; redirects cannot enlarge them.</param>
internal sealed record AiWebAccessReview(AiWebAccessTarget[] Targets) : AiToolReview
{
    /// <summary>Gets the single-call network review kind.</summary>
    public string Type { get; } = "one_call_private_web_access";
    /// <summary>Gets the explicit non-standing grant boundary.</summary>
    public string Scope { get; } = "Only these exact endpoints for this tool invocation. Redirects and later calls receive no standing private-network grant.";
}

internal static class AiToolReviews
{
    /// <summary>Materializes only the exact approval-document snapshot at its persistence/fingerprint boundary.</summary>
    internal static JsonObject Write(AiToolReview review) => review switch
    {
        AiObservedToolReview observed => observed.Observation.DeepClone().AsObject(),
        AiContentToolReview content => JsonSerializer.SerializeToNode(content.Review, ContentJsonContext.Review)!.AsObject(),
        AiExecuteReview execute => JsonSerializer.SerializeToNode(execute, AiApprovalJsonContext.Default.AiExecuteReview)!.AsObject(),
        AiProcessStopReview process => JsonSerializer.SerializeToNode(process, AiApprovalJsonContext.Default.AiProcessStopReview)!.AsObject(),
        AiOperationPermissionReview permission => JsonSerializer.SerializeToNode(permission, AiApprovalJsonContext.Default.AiOperationPermissionReview)!.AsObject(),
        AiPathAccessReview path => JsonSerializer.SerializeToNode(path, AiApprovalJsonContext.Default.AiPathAccessReview)!.AsObject(),
        AiWebAccessReview web => JsonSerializer.SerializeToNode(web, AiApprovalJsonContext.Default.AiWebAccessReview)!.AsObject(),
        _ => throw new InvalidOperationException("Unsupported AI review kind.")
    };
}