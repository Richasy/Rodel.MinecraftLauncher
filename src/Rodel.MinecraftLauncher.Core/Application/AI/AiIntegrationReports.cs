// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>The observed outcome for one integration file, independently of other files and client registration.</summary>
public sealed record AiIntegrationFileOutcome
{
    /// <summary>Gets the exact destination processed.</summary>
    public required string Path { get; init; }
    /// <summary>Gets the performed, preserved or failed action.</summary>
    public required string Status { get; init; }
    /// <summary>Gets heterogeneous filesystem or client error details when processing failed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Error { get; init; }
}

/// <summary>The actual external-client registration observation, not inferred installation success.</summary>
public sealed record AiIntegrationMcpOutcome
{
    /// <summary>Gets the actual registration action or failure state.</summary>
    public required string Status { get; init; }
    /// <summary>Gets whether the desired registration was observed.</summary>
    public bool Registered { get; init; }
    /// <summary>Gets heterogeneous client failure information.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Error { get; init; }
    /// <summary>Gets open failure evidence, retaining an explicitly null payload on a failed client operation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Details { get; init; }
}

/// <summary>Per-file and client receipts for an exact integration plan, including partial completion.</summary>
public sealed record AiIntegrationReceipt
{
    /// <summary>Gets completed or partial.</summary>
    public required string Status { get; init; }
    /// <summary>Gets per-file outcomes in plan order.</summary>
    public required AiIntegrationFileOutcome[] Files { get; init; }
    /// <summary>Gets the independently observed client registration outcome.</summary>
    public required AiIntegrationMcpOutcome Mcp { get; init; }
    /// <summary>Gets the manual registration export for the planned stable launcher.</summary>
    public required AiIntegrationExport ConfigurationExport { get; init; }
    /// <summary>Gets whether removal retained the executable bridge.</summary>
    public bool BridgeRetained { get; init; }
}

/// <summary>An observed integration file and the exact proposed ownership-safe action.</summary>
public sealed record AiIntegrationFile
{
    /// <summary>Gets the absolute destination path.</summary>
    public required string Path { get; init; }
    /// <summary>Gets the currently observed hash, or null if absent.</summary>
    public string? CurrentSha256 { get; init; }
    /// <summary>Gets the product content hash.</summary>
    public required string ProductSha256 { get; init; }
    /// <summary>Gets the proposed action, including conflict and preserve_modified.</summary>
    public required string Action { get; init; }
    /// <summary>Gets whether this file belongs to the retained executable bridge.</summary>
    public bool Bridge { get; init; }
}

/// <summary>The explicit MCP registration export; producing it does not register a client.</summary>
public sealed record AiIntegrationExport
{
    /// <summary>Gets the stable launcher command.</summary>
    public required string Command { get; init; }
    /// <summary>Gets standard MCP stdio arguments bound to one data root.</summary>
    public required string[] Args { get; init; }
    /// <summary>Gets the external client's server registration name.</summary>
    public string ServerName { get; init; } = "rodel-minecraft";
    /// <summary>Gets whether the export itself registers the client; always false.</summary>
    public bool Registered { get; init; }
}

/// <summary>A frozen integration observation and plan; the fingerprint binds every field before mutation.</summary>
public sealed record AiIntegrationPlan
{
    /// <summary>Gets the plan schema version.</summary>
    public int SchemaVersion { get; init; } = 1;
    /// <summary>Gets the external client identifier.</summary>
    public required string Client { get; init; }
    /// <summary>Gets its discovered executable, or null.</summary>
    public string? ClientExecutable { get; init; }
    /// <summary>Gets its observed version, or null.</summary>
    public string? ClientVersion { get; init; }
    /// <summary>Gets install or remove.</summary>
    public required string Action { get; init; }
    /// <summary>Gets the selected client configuration root.</summary>
    public required string ConfigRoot { get; init; }
    /// <summary>Gets the selected skill root.</summary>
    public required string SkillsRoot { get; init; }
    /// <summary>Gets the dedicated integration installation directory.</summary>
    public required string InstallationRoot { get; init; }
    /// <summary>Gets the selected independently installed CLI, or an empty value when it is still required.</summary>
    public required string LauncherExecutable { get; init; }
    /// <summary>Gets the selected executable hash frozen into the reviewed registration plan.</summary>
    public string? LauncherSha256 { get; init; }
    /// <summary>Gets the launcher data root bound to this plan.</summary>
    public required string DataRoot { get; init; }
    /// <summary>Gets the previous persisted binding without normalizing unknown JSON values.</summary>
    public JsonNode? PreviousDataRoot { get; init; }
    /// <summary>Gets whether the data-root binding changed.</summary>
    public bool BindingChanged { get; init; }
    /// <summary>Gets the previous persisted client root without normalization.</summary>
    public JsonNode? PreviousConfigRoot { get; init; }
    /// <summary>Gets whether removal from the old client scope is required first.</summary>
    public bool ClientScopeChanged { get; init; }
    /// <summary>Gets per-file ownership-safe actions.</summary>
    public required AiIntegrationFile[] Files { get; init; }
    /// <summary>Gets the registration action, including conflict and inspection_failed.</summary>
    public required string McpAction { get; init; }
    /// <summary>Gets the currently observed registration fingerprint, or null.</summary>
    public string? McpCurrentFingerprint { get; init; }
    /// <summary>Gets heterogeneous client-inspection failure details, or null.</summary>
    public JsonObject? InspectionError { get; init; }
    /// <summary>Gets the user's explicit conflict-replacement choice.</summary>
    public bool ReplaceConflicts { get; init; }
    /// <summary>Gets the evidence supporting registration ownership.</summary>
    public required string McpOwnershipEvidence { get; init; }
    /// <summary>Gets the previous attempt state as persisted.</summary>
    public JsonNode? LastAttemptState { get; init; }
    /// <summary>Gets heterogeneous previous per-file outcomes as persisted.</summary>
    public JsonNode? LastOutcomes { get; init; }
    /// <summary>Gets the previous client operation receipt as persisted.</summary>
    public JsonNode? LastMcpResult { get; init; }
    /// <summary>Gets any durable in-flight client intent without inferring completion.</summary>
    public JsonNode? PendingMcpDispatch { get; init; }
    /// <summary>Gets a manual registration export.</summary>
    public required AiIntegrationExport ConfigurationExport { get; init; }
    /// <summary>Gets whether the exact desired registration was actually observed.</summary>
    public bool McpRegistered { get; init; }
    /// <summary>Gets whether removal preserves the executable bridge.</summary>
    public bool BridgeRetainedOnRemoval { get; init; } = true;
    /// <summary>Gets the hash binding this observation; omitted only while computing that hash.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Fingerprint { get; init; }
}