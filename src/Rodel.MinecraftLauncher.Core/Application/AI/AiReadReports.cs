// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Assistant;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Inspection results for explicitly registered external MCP programs.</summary>
public sealed record AiMcpCatalog
{
    /// <summary>Gets ready, disabled or failed inspection results in registration order.</summary>
    public required AiMcpDescription[] Items { get; init; }
    /// <summary>Gets additional transport diagnostics without discarding future host fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A skill's instruction snapshot; a catalog omits the body until explicitly read.</summary>
public sealed record AiSkillReport
{
    /// <summary>Gets the source-qualified skill identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the frontmatter name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the frontmatter description.</summary>
    public required string Description { get; init; }
    /// <summary>Gets product or user ownership.</summary>
    public required string Source { get; init; }
    /// <summary>Gets the instruction content hash.</summary>
    public required string Version { get; init; }
    /// <summary>Gets all frontmatter entries, including unknown keys.</summary>
    public required Dictionary<string, string> Metadata { get; init; }
    /// <summary>Gets the full instruction text only when explicitly requested.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Body { get; init; }
    /// <summary>Gets additional transport diagnostics without changing the skill metadata.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The enabled skill snapshot and source-specific indexing warnings.</summary>
public sealed record AiSkillCatalog
{
    /// <summary>Gets skill metadata without instruction bodies.</summary>
    public required AiSkillReport[] Items { get; init; }
    /// <summary>Gets heterogeneous source errors without changing their diagnostic data.</summary>
    public required JsonArray Warnings { get; init; }
    /// <summary>Gets the extension revision used for this snapshot.</summary>
    public long ConfigurationRevision { get; init; }
    /// <summary>Gets whether explicitly registered user directories are supported.</summary>
    public bool UserDirectoriesImplemented { get; init; } = true;
    /// <summary>Gets whether any skill script was executed; always false.</summary>
    public bool ScriptsExecuted { get; init; }
    /// <summary>Gets additional host fields not represented by the catalog contract.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A bounded read of one skill reference, never executable code.</summary>
public sealed record AiSkillReference
{
    /// <summary>Gets the owning skill identifier.</summary>
    public required string SkillId { get; init; }
    /// <summary>Gets the relative reference path.</summary>
    public required string Path { get; init; }
    /// <summary>Gets the reference content hash.</summary>
    public required string Version { get; init; }
    /// <summary>Gets the reference text.</summary>
    public required string Body { get; init; }
    /// <summary>Gets whether scripts were executed; always false.</summary>
    public bool ScriptsExecuted { get; init; }
}

/// <summary>An extension configuration read, retaining noncanonical and unknown persisted fields.</summary>
public sealed record AiExtensionReport
{
    /// <summary>Gets the recognized document schema version.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SchemaVersion { get; init; }
    /// <summary>Gets the recognized optimistic-concurrency revision.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Revision { get; init; }
    /// <summary>Gets independently configured MCP registrations.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonArray? Mcp { get; init; }
    /// <summary>Gets explicitly authorized file roots.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonArray? FileRoots { get; init; }
    /// <summary>Gets registered skill directories.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonArray? SkillDirectories { get; init; }
    /// <summary>Gets unknown, null or noncanonical persisted values unchanged.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }

    internal static AiExtensionReport Read(JsonObject value) => new()
    {
        SchemaVersion = StoredReportFields.Value<int>(value, "schemaVersion"),
        Revision = StoredReportFields.Value<long>(value, "revision"),
        Mcp = AiReportFields.Array(value, "mcp"),
        FileRoots = AiReportFields.Array(value, "fileRoots"),
        SkillDirectories = AiReportFields.Array(value, "skillDirectories"),
        AdditionalData = StoredReportFields.Remaining(value)
    };
}

/// <summary>A durable conversation or turn snapshot, with lossless preservation of evolving record fields.</summary>
public sealed record AiChatRecord
{
    /// <summary>Gets the record identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }
    /// <summary>Gets the record schema version when recognized.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SchemaVersion { get; init; }
    /// <summary>Gets the conversation revision when present.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Revision { get; init; }
    /// <summary>Gets the owning conversation for a turn.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConversationId { get; init; }
    /// <summary>Gets the selected turn when cancellation is still being signaled rather than returning a terminal record.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TurnId { get; init; }
    /// <summary>Gets the last observed event sequence when a foreground caller waits for this turn.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? EventCursor { get; init; }
    /// <summary>Gets the saved conversation title.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }
    /// <summary>Gets pending, prompt, model or manual title provenance independently of the turn's lifecycle.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TitleSource { get; init; }
    /// <summary>Gets the turn's lifecycle status.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }
    /// <summary>Gets the provider's final stop reason, including output truncation, without treating it as a transport error.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FinishReason { get; init; }
    /// <summary>Gets the original user prompt without truncation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Prompt { get; init; }
    /// <summary>Gets the explicitly submitted reference-image metadata, not source paths or binary provider content.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiChatImage[]? Images { get; init; }
    /// <summary>Gets the configured model binding identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ModelId { get; init; }
    /// <summary>Gets the selected instance identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstanceId { get; init; }
    /// <summary>Gets the game session that owns this temporary conversation or game-originated turn.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GameSessionId { get; init; }
    /// <summary>Gets the host-created skin grant bound to this conversation or turn.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SkinScopeId { get; init; }
    /// <summary>Gets the incident authorizing only reversible repair tools in this conversation or turn.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RepairId { get; init; }
    /// <summary>Gets the recorded permission mode, not a new authorization.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; init; }
    /// <summary>Gets host_owned or client_owned as recorded by the submitting runtime.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ownership { get; init; }
    /// <summary>Gets the record creation timestamp.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreatedUtc { get; init; }
    /// <summary>Gets the last committed update timestamp.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UpdatedUtc { get; init; }
    /// <summary>Gets the persisted goal identity used to prevent replay of an uncertain mutation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GoalId { get; init; }
    /// <summary>Gets the caller's idempotency key.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientKey { get; init; }
    /// <summary>Gets the exact committed request fingerprint.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestFingerprint { get; init; }
    /// <summary>Gets the earlier unsuccessful turn selected for retry.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RetryOf { get; init; }
    /// <summary>Gets the owning runtime process identifier as recorded at submission.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RuntimeProcessId { get; init; }
    /// <summary>Gets the owning process start timestamp used to reject recycled process identifiers.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuntimeStartedUtc { get; init; }
    /// <summary>Gets the permission revision captured by this turn.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? OperationRevision { get; init; }
    /// <summary>Gets whether authorization came from the conversation or this explicit turn.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TurnAuthorization { get; init; }
    /// <summary>Gets the captured file-access choice without granting any new access.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? FileAccessEnabled { get; init; }
    /// <summary>Gets the captured network-access choice without granting any new access.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NetworkAccessEnabled { get; init; }
    /// <summary>Gets whether tools were enabled for the submitted turn.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ToolsEnabled { get; init; }
    /// <summary>Gets the committed public answer, if any.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Answer { get; init; }
    /// <summary>Gets the frozen question awaiting explicit user input, independently of operation approvals.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiUserPrompt? PendingUserPrompt { get; init; }
    /// <summary>Gets heterogeneous durable tool intents and results without interpreting them as authorization.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonArray? Intents { get; init; }
    /// <summary>Gets unresolved effect evidence without treating uncertainty as failure or permission to replay.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonArray? UnknownEffects { get; init; }
    /// <summary>Gets independent job references retained by the turn.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonArray? JobIds { get; init; }
    /// <summary>Gets heterogeneous operation/provider error data.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Error { get; init; }
    /// <summary>Gets sanitized turn snapshots included by conversation detail reads.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiChatRecord[]? Turns { get; init; }
    /// <summary>Gets evolving durable fields, including grants, intents and unknown-effect evidence, without normalization.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }

    internal static AiChatRecord Read(JsonObject value) => new()
    {
        Id = StoredReportFields.Text(value, "id"),
        SchemaVersion = StoredReportFields.Value<int>(value, "schemaVersion"),
        Revision = StoredReportFields.Value<long>(value, "revision"),
        ConversationId = StoredReportFields.Text(value, "conversationId"),
        TurnId = StoredReportFields.Text(value, "turnId"),
        EventCursor = StoredReportFields.Value<long>(value, "eventCursor"),
        Title = StoredReportFields.Text(value, "title"),
        TitleSource = StoredReportFields.Text(value, "titleSource"),
        Status = StoredReportFields.Text(value, "status"),
        FinishReason = StoredReportFields.Text(value, "finishReason"),
        Prompt = StoredReportFields.Text(value, "prompt"),
        Images = AiReportFields.Images(value, "images"),
        ModelId = StoredReportFields.Text(value, "modelId"),
        InstanceId = StoredReportFields.Text(value, "instanceId"),
        GameSessionId = StoredReportFields.Text(value, "gameSessionId"),
        SkinScopeId = StoredReportFields.Text(value, "skinScopeId"),
        RepairId = StoredReportFields.Text(value, "repairId"),
        Mode = StoredReportFields.Text(value, "mode"),
        Ownership = StoredReportFields.Text(value, "ownership"),
        CreatedUtc = StoredReportFields.Text(value, "createdUtc"),
        UpdatedUtc = StoredReportFields.Text(value, "updatedUtc"),
        GoalId = StoredReportFields.Text(value, "goalId"),
        ClientKey = StoredReportFields.Text(value, "clientKey"),
        RequestFingerprint = StoredReportFields.Text(value, "requestFingerprint"),
        RetryOf = StoredReportFields.Text(value, "retryOf"),
        RuntimeProcessId = StoredReportFields.Value<int>(value, "runtimeProcessId"),
        RuntimeStartedUtc = StoredReportFields.Text(value, "runtimeStartedUtc"),
        OperationRevision = StoredReportFields.Value<long>(value, "operationRevision"),
        TurnAuthorization = StoredReportFields.Text(value, "turnAuthorization"),
        FileAccessEnabled = StoredReportFields.Value<bool>(value, "fileAccessEnabled"),
        NetworkAccessEnabled = StoredReportFields.Value<bool>(value, "networkAccessEnabled"),
        ToolsEnabled = StoredReportFields.Value<bool>(value, "toolsEnabled"),
        Answer = StoredReportFields.Text(value, "answer"),
        PendingUserPrompt = AiReportFields.UserPrompt(value),
        Intents = AiReportFields.Array(value, "intents"),
        UnknownEffects = AiReportFields.Array(value, "unknownEffects"),
        JobIds = AiReportFields.Array(value, "jobIds"),
        Error = AiReportFields.Node(value, "error"),
        Turns = AiReportFields.Records(value, "turns"),
        AdditionalData = StoredReportFields.Remaining(value)
    };
}

/// <summary>A stable page of persisted conversations, including unreadable-record diagnostics.</summary>
public sealed record AiConversationPage
{
    /// <summary>Gets the selected conversation snapshots.</summary>
    public required AiChatRecord[] Items { get; init; }
    /// <summary>Gets the requested zero-based offset.</summary>
    public int Offset { get; init; }
    /// <summary>Gets the requested maximum count.</summary>
    public int Limit { get; init; }
    /// <summary>Gets the number of known conversation records.</summary>
    public int Total { get; init; }
    /// <summary>Gets whether another page exists.</summary>
    public bool HasMore { get; init; }
    /// <summary>Gets additional transport diagnostics without discarding future page fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }

    internal static AiConversationPage Read(JsonObject value) => new()
    {
        Items = AiReportFields.Records(value, "items") ?? throw new InvalidDataException("Missing conversation page items."),
        Offset = value["offset"]!.GetValue<int>(),
        Limit = value["limit"]!.GetValue<int>(),
        Total = value["total"]!.GetValue<int>(),
        HasMore = value["hasMore"]!.GetValue<bool>(),
        AdditionalData = AiReportFields.Additional(value, "offset", "limit", "total", "hasMore")
    };
}

/// <summary>Public turn snapshots ordered by durable creation time.</summary>
public sealed record AiTurnCatalog
{
    /// <summary>Gets sanitized turn snapshots and unreadable-record diagnostics.</summary>
    public required AiChatRecord[] Items { get; init; }
}

/// <summary>A committed event with an open, event-kind-specific payload.</summary>
public sealed record AiChatEvent
{
    /// <summary>Gets the durable event sequence.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Sequence { get; init; }
    /// <summary>Gets the event kind.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; init; }
    /// <summary>Gets the event-kind-specific payload without imposing a closed schema.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Data { get; init; }
    /// <summary>Gets remaining durable envelope fields and heterogeneous payloads unchanged.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }

    internal static AiChatEvent Read(JsonObject value) => new()
    {
        Sequence = StoredReportFields.Value<long>(value, "sequence"),
        Kind = StoredReportFields.Text(value, "kind"),
        Data = AiReportFields.Object(value, "data"),
        AdditionalData = StoredReportFields.Remaining(value)
    };
}

/// <summary>A bounded durable-event page; observation never replays an operation.</summary>
public sealed record AiEventPage
{
    /// <summary>Gets committed events after the requested cursor.</summary>
    public required AiChatEvent[] Items { get; init; }
    /// <summary>Gets the next durable-event cursor.</summary>
    public long NextSequence { get; init; }
    /// <summary>Gets whether more committed events remain.</summary>
    public bool HasMore { get; init; }
    /// <summary>Gets additional transport diagnostics without discarding future page fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }

    internal static AiEventPage Read(JsonObject value) => new()
    {
        Items = [.. value["items"]!.AsArray().Select(item => AiChatEvent.Read(item!.AsObject()))],
        NextSequence = value["nextSequence"]!.GetValue<long>(),
        HasMore = value["hasMore"]!.GetValue<bool>(),
        AdditionalData = AiReportFields.Additional(value, "items", "nextSequence", "hasMore")
    };
}

/// <summary>The current Core AI runtime identity and ownership, without starting a model.</summary>
public sealed record AiHostStatus
{
    /// <summary>Gets the RPC protocol version.</summary>
    public int ProtocolVersion { get; init; }
    /// <summary>Gets the runtime process identifier.</summary>
    public int ProcessId { get; init; }
    /// <summary>Gets the data-root identity hash.</summary>
    public required string RootIdentity { get; init; }
    /// <summary>Gets whether this process supports dynamic code.</summary>
    public bool DynamicCodeSupported { get; init; }
    /// <summary>Gets whether a generation owns the runtime gate.</summary>
    public bool ActiveTextRequest { get; init; }
    /// <summary>Gets the foreground/owned-turn lifetime contract.</summary>
    public string Ownership { get; init; } = "foreground-connection-or-owned-chat-turn";
    /// <summary>Gets whether durable conversations are implemented.</summary>
    public bool PersistentConversations { get; init; } = true;
    /// <summary>Gets cli-background or in-process-core hosting.</summary>
    public required string Hosting { get; init; }
    /// <summary>Gets a storage failure code, or null if no failure was recorded.</summary>
    public string? ChatStorageFailure { get; init; }
    /// <summary>Gets additional host diagnostics, including deferred credential-cleanup warnings.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>References in the embedded engine's configured model store, without loading weights.</summary>
public sealed record AiLocalModelCatalog
{
    /// <summary>Gets the engine ownership mode.</summary>
    public string Mode { get; init; } = "embedded";
    /// <summary>Gets whether an external service is needed; always false.</summary>
    public bool ExternalServiceRequired { get; init; }
    /// <summary>Gets model references supplied by the engine.</summary>
    public required AiLocalModelReference[] Models { get; init; }
    /// <summary>Gets additional transport diagnostics without changing model references.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A local model reference, not a provider's open raw response.</summary>
public sealed record AiLocalModelReference
{
    /// <summary>Gets the model store reference.</summary>
    public required string Id { get; init; }
    /// <summary>Gets its display label.</summary>
    public required string DisplayName { get; init; }
}

/// <summary>A bounded read of an exact tool confirmation binding.</summary>
public sealed record AiConfirmationPage
{
    /// <summary>Gets the invocation identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the stored binding fingerprint without normalizing its JSON value.</summary>
    public required JsonNode Fingerprint { get; init; }
    /// <summary>Gets the stored decision state without normalizing its JSON value.</summary>
    public required JsonNode State { get; init; }
    /// <summary>Gets this page of the original confirmation document.</summary>
    public required string Text { get; init; }
    /// <summary>Gets the character offset.</summary>
    public int Offset { get; init; }
    /// <summary>Gets the continuation offset, or null at the end.</summary>
    public int? NextOffset { get; init; }
    /// <summary>Gets whether the document has more text.</summary>
    public bool HasMore { get; init; }
    /// <summary>Gets the complete document's character count.</summary>
    public int TotalCharacters { get; init; }
    /// <summary>Gets additional transport diagnostics without changing the exact confirmation text.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The exact conversation deletion review; independent job lifetimes are not changed by removal.</summary>
public sealed record AiConversationRemovalReview
{
    /// <summary>Gets the conversation selected for removal.</summary>
    public required string ConversationId { get; init; }
    /// <summary>Gets its persisted revision without normalizing noncanonical data.</summary>
    public required JsonNode Revision { get; init; }
    /// <summary>Gets all observed turn identities in their original order.</summary>
    public required JsonArray TurnIds { get; init; }
    /// <summary>Gets independent job references that survive history deletion.</summary>
    public required string[] IndependentJobIds { get; init; }
    /// <summary>Gets whether removal must first stop an active turn.</summary>
    public bool StopsActiveTurn { get; init; }
    /// <summary>Gets whether independent jobs remain running; always true.</summary>
    public bool IndependentJobsAreNotCanceled { get; init; } = true;
    /// <summary>Gets the exact identity/revision/job binding required for removal.</summary>
    public required string Fingerprint { get; init; }
    /// <summary>Gets additional transport diagnostics without changing the deletion binding.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

internal static class AiReportFields
{
    internal static AiUserPrompt? UserPrompt(JsonObject value)
    {
        if (value["pendingUserPrompt"] is not JsonObject node) return null;
        AiUserPrompt? prompt;
        try { prompt = node.Deserialize(AiUserPromptJsonContext.Default.AiUserPrompt); }
        catch (JsonException) { return null; }

        if (!JsonNode.DeepEquals(node, JsonSerializer.SerializeToNode(prompt, AiUserPromptJsonContext.Default.AiUserPrompt))) return null;
        value.Remove("pendingUserPrompt");
        return prompt;
    }

    internal static AiChatImage[]? Images(JsonObject value, string name)
    {
        if (value[name] is not JsonArray array) return null;
        AiChatImage[]? images;
        try { images = array.Deserialize(AiChatJsonContext.Default.AiChatImageArray); }
        catch (JsonException) { return null; }

        if (!JsonNode.DeepEquals(array, JsonSerializer.SerializeToNode(images, AiChatJsonContext.Default.AiChatImageArray))) return null;
        value.Remove(name);
        return images;
    }

    internal static Dictionary<string, JsonElement> Additional(JsonObject value, params string[] projected)
    {
        foreach (var name in projected) value.Remove(name);
        return StoredReportFields.Remaining(value);
    }

    internal static JsonNode? Node(JsonObject value, string name)
    {
        var node = value[name];
        if (node is not null) value.Remove(name);
        return node;
    }

    internal static AiChatRecord[]? Records(JsonObject value, string name)
    {
        if (value[name] is not JsonArray array || array.Any(item => item is not JsonObject)) return null;
        value.Remove(name);
        return [.. array.Select(item => AiChatRecord.Read(item!.AsObject()))];
    }

    internal static JsonObject? Object(JsonObject value, string name)
    {
        if (value[name] is not JsonObject item) return null;
        value.Remove(name);
        return item;
    }

    internal static JsonArray? Array(JsonObject value, string name)
    {
        if (value[name] is not JsonArray array) return null;
        value.Remove(name);
        return array;
    }
}