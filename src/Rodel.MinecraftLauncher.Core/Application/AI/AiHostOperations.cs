// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>A closed Core operation, serialized only when an entry explicitly selects the pipe host.</summary>
internal abstract record AiHostOperation
{
    internal abstract string Method { get; }
}

/// <summary>A conversation operation whose lease and recovery scope are supplied by its concrete request.</summary>
/// <param name="ConversationId">The conversation requiring an operation lease, if any.</param>
/// <param name="TurnId">The observed or controlled turn, if any.</param>
internal abstract record AiChatOperation(string? ConversationId = null, string? TurnId = null) : AiHostOperation;

/// <summary>Creates a conversation from one captured set of binding options.</summary>
/// <param name="Request">The original typed creation options.</param>
internal sealed record AiCreateConversationOperation(AiChatCreate Request) : AiChatOperation { internal override string Method => "chat.create"; }
/// <summary>Reads a bounded page without replaying any conversation.</summary>
/// <param name="Offset">The requested zero-based page offset.</param>
/// <param name="Limit">The maximum number of records.</param>
internal sealed record AiListConversationsOperation(int Offset, int Limit) : AiChatOperation { internal override string Method => "chat.list"; }
/// <summary>Reads the named durable conversation.</summary>
/// <param name="Id">The conversation identifier.</param>
internal sealed record AiShowConversationOperation(string Id) : AiChatOperation(Id) { internal override string Method => "chat.show"; }
/// <summary>Applies one revision-checked conversation update.</summary>
/// <param name="Request">The exact typed update.</param>
internal sealed record AiUpdateConversationOperation(AiConversationUpdate Request) : AiChatOperation(Request.ConversationId) { internal override string Method => "chat.update"; }
/// <summary>Reads the exact deletion review without removing history.</summary>
/// <param name="Id">The reviewed conversation.</param>
internal sealed record AiReviewRemovalOperation(string Id) : AiChatOperation(Id) { internal override string Method => "chat.review_remove"; }
/// <summary>Exports the explicitly selected conversation.</summary>
/// <param name="Request">The destination and overwrite choice.</param>
internal sealed record AiExportConversationOperation(AiConversationExport Request) : AiChatOperation(Request.ConversationId) { internal override string Method => "chat.export"; }
/// <summary>Removes exactly the confirmed history binding.</summary>
/// <param name="Request">The original removal confirmation.</param>
internal sealed record AiRemoveConversationOperation(AiConversationRemoval Request) : AiChatOperation(Request.ConversationId) { internal override string Method => "chat.remove"; }
/// <summary>Observes one turn without replaying it.</summary>
/// <param name="Id">The owning conversation.</param>
/// <param name="Turn">The observed turn.</param>
internal sealed record AiShowTurnOperation(string Id, string Turn) : AiChatOperation(Id, Turn) { internal override string Method => "chat.turn"; }
/// <summary>Reads the durable event cursor for a turn.</summary>
/// <param name="Id">The owning conversation.</param>
/// <param name="Turn">The observed turn.</param>
/// <param name="After">The last committed cursor seen by the caller.</param>
/// <param name="Limit">The bounded event count.</param>
internal sealed record AiReadEventsOperation(string Id, string Turn, long After, int Limit = 100) : AiChatOperation(Id, Turn) { internal override string Method => "chat.events"; }
internal sealed record AiReadCompletedTurnEventsOperation(string Id, string Turn) : AiChatOperation(Id, Turn) { internal override string Method => "chat.completed-events"; }
/// <summary>Reads bounded original archived context.</summary>
/// <param name="Request">The exact archive and character range.</param>
internal sealed record AiReadArchiveOperation(AiContextRead Request) : AiChatOperation(Request.ConversationId) { internal override string Method => "chat.archive"; }
/// <summary>Observes unresolved effects without replaying operations.</summary>
/// <param name="Id">The owning conversation.</param>
/// <param name="Turn">The stopped turn.</param>
internal sealed record AiCheckEffectsOperation(string Id, string Turn) : AiChatOperation(Id, Turn) { internal override string Method => "chat.effects_check"; }
/// <summary>Records the user's exact acceptance of remaining uncertainty.</summary>
/// <param name="Request">The exact effect fingerprint and explicit decision.</param>
internal sealed record AiAcknowledgeEffectsOperation(AiEffectAcknowledgement Request) : AiChatOperation(Request.ConversationId, Request.TurnId) { internal override string Method => "chat.effects_acknowledge"; }
/// <summary>Answers one frozen user question.</summary>
/// <param name="Request">The captured answer, not operation permission.</param>
internal sealed record AiRespondOperation(AiUserResponse Request) : AiChatOperation(Request.ConversationId, Request.TurnId) { internal override string Method => "chat.respond"; }
/// <summary>Decides the exact current tool authorization binding.</summary>
/// <param name="Request">The explicit scope and fingerprint decision.</param>
internal sealed record AiConfirmOperation(AiToolConfirmation Request) : AiChatOperation(Request.ConversationId, Request.TurnId) { internal override string Method => "chat.confirm"; }
/// <summary>Reads the original confirmation document without deciding it.</summary>
/// <param name="Request">The confirmation identifier and bounded range.</param>
internal sealed record AiReadConfirmationOperation(AiToolConfirmationRead Request) : AiChatOperation(Request.ConversationId, Request.TurnId) { internal override string Method => "chat.confirmation"; }
/// <summary>Commits an idempotent turn submission.</summary>
/// <param name="Request">The captured turn content, model and ownership options.</param>
internal sealed record AiSendOperation(AiChatSend Request) : AiChatOperation(Request.ConversationId) { internal override string Method => "chat.send"; }
/// <summary>Signals the selected turn and optional owned queue for cancellation.</summary>
/// <param name="Request">The explicit cancellation scope.</param>
internal sealed record AiCancelOperation(AiChatCancellation Request) : AiChatOperation(Request.ConversationId, Request.TurnId) { internal override string Method => "chat.cancel"; }
/// <summary>Promotes one queued turn and stops only its conversation's current execution.</summary>
/// <param name="Id">The conversation owning the queued message.</param>
/// <param name="Turn">The queued turn to run next without resubmitting its prompt.</param>
internal sealed record AiRunQueuedTurnOperation(string Id, string Turn) : AiChatOperation(Id, Turn) { internal override string Method => "chat.run_queued"; }
/// <summary>Inspects the selected configured MCP source.</summary>
/// <param name="Id">The optional source filter.</param>
internal sealed record AiInspectMcpOperation(string? Id) : AiHostOperation { internal override string Method => "mcp.inspect"; }
/// <summary>Reads the available product skill catalog.</summary>
internal sealed record AiListSkillsOperation : AiHostOperation { internal override string Method => "skill.list"; }
/// <summary>Reads one explicitly selected skill.</summary>
/// <param name="Id">The source-qualified skill identifier.</param>
internal sealed record AiReadSkillOperation(string Id) : AiHostOperation { internal override string Method => "skill.read"; }
/// <summary>Observes Core runtime identity and storage state.</summary>
internal sealed record AiHostStatusOperation : AiHostOperation { internal override string Method => "status"; }
/// <summary>Observes Embedded inference without loading a model.</summary>
internal sealed record AiLocalStatusOperation : AiHostOperation { internal override string Method => "local.status"; }
/// <summary>Lists references in the local model store.</summary>
internal sealed record AiLocalModelsOperation : AiHostOperation { internal override string Method => "local.models"; }
/// <summary>Releases only this runtime's Embedded inference resources.</summary>
internal sealed record AiLocalUnloadOperation : AiHostOperation { internal override string Method => "local.unload"; }
/// <summary>Discovers models through the selected saved connection.</summary>
/// <param name="ServiceId">The configured service identifier.</param>
internal sealed record AiListModelsOperation(string ServiceId) : AiHostOperation { internal override string Method => "models"; }
/// <summary>Runs one isolated foreground generation.</summary>
/// <param name="Request">The captured typed generation request.</param>
internal sealed record AiGenerateOperation(AiTextRequest Request) : AiHostOperation { internal override string Method => "generate"; }

/// <summary>The scalar source/skill identifier at the RPC boundary.</summary>
/// <param name="Id">The requested identifier, optionally absent for catalog inspection.</param>
internal sealed record AiIdQuery(string? Id);
/// <summary>The conversation identifier at the RPC boundary.</summary>
/// <param name="ConversationId">The selected conversation.</param>
internal sealed record AiConversationQuery(string ConversationId);
/// <summary>The exact turn reference at the RPC boundary.</summary>
/// <param name="ConversationId">The owning conversation.</param>
/// <param name="TurnId">The selected turn.</param>
internal sealed record AiTurnQuery(string ConversationId, string TurnId);
/// <summary>The conversation paging parameters at the RPC boundary.</summary>
/// <param name="Offset">The requested offset.</param>
/// <param name="Limit">The requested maximum count.</param>
internal sealed record AiConversationListQuery(int? Offset = null, int? Limit = null);
/// <summary>The exact durable event cursor and optional page limit at the RPC boundary.</summary>
/// <param name="ConversationId">The owning conversation.</param>
/// <param name="TurnId">The selected turn.</param>
/// <param name="After">The last observed cursor.</param>
/// <param name="Limit">An explicit nondefault page limit; omission retains the established wire shape.</param>
internal sealed record AiEventsQuery(string ConversationId, string TurnId, long? After = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Limit = null);
/// <summary>The selected configured provider at the RPC boundary.</summary>
/// <param name="ServiceId">The saved service identity.</param>
internal sealed record AiModelsQuery(string ServiceId);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AiIdQuery))]
[JsonSerializable(typeof(AiConversationQuery))]
[JsonSerializable(typeof(AiTurnQuery))]
[JsonSerializable(typeof(AiConversationListQuery))]
[JsonSerializable(typeof(AiEventsQuery))]
[JsonSerializable(typeof(AiModelsQuery))]
internal sealed partial class AiHostRequestJsonContext : JsonSerializerContext;

/// <summary>Owns the one method-to-request mapping at the actual RPC boundary, not the in-process execution path.</summary>
internal static class AiHostOperations
{
    internal static AiHostOperation Read(string method, JsonNode? data)
    {
        T ReadData<T>(System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> metadata)
            => (data ?? new JsonObject()).Deserialize(metadata) ?? throw new ArgumentException("Missing AI request.");
        return method switch
        {
            "chat.create" => new AiCreateConversationOperation(ReadData(AiChatJsonContext.Default.AiChatCreate)),
            "chat.list" => List(ReadData(AiHostRequestJsonContext.Default.AiConversationListQuery)),
            "chat.show" => new AiShowConversationOperation(ReadData(AiHostRequestJsonContext.Default.AiConversationQuery).ConversationId),
            "chat.update" => new AiUpdateConversationOperation(ReadData(AiChatJsonContext.Default.AiConversationUpdate)),
            "chat.review_remove" => new AiReviewRemovalOperation(ReadData(AiHostRequestJsonContext.Default.AiConversationQuery).ConversationId),
            "chat.export" => new AiExportConversationOperation(ReadData(AiChatJsonContext.Default.AiConversationExport)),
            "chat.remove" => new AiRemoveConversationOperation(ReadData(AiChatJsonContext.Default.AiConversationRemoval)),
            "chat.turn" => Turn(ReadData(AiHostRequestJsonContext.Default.AiTurnQuery)),
            "chat.events" => Events(ReadData(AiHostRequestJsonContext.Default.AiEventsQuery)),
            "chat.archive" => new AiReadArchiveOperation(ReadData(AiContextJsonContext.Default.AiContextRead)),
            "chat.effects_check" => Effects(ReadData(AiHostRequestJsonContext.Default.AiTurnQuery)),
            "chat.effects_acknowledge" => new AiAcknowledgeEffectsOperation(ReadData(AiEffectJsonContext.Default.AiEffectAcknowledgement)),
            "chat.respond" => new AiRespondOperation(ReadData(AiUserPromptJsonContext.Default.AiUserResponse)),
            "chat.confirm" => new AiConfirmOperation(ReadData(AiApprovalJsonContext.Default.AiToolConfirmation)),
            "chat.confirmation" => new AiReadConfirmationOperation(ReadData(AiApprovalJsonContext.Default.AiToolConfirmationRead)),
            "chat.send" => new AiSendOperation(ReadData(AiChatJsonContext.Default.AiChatSend)),
            "chat.cancel" => new AiCancelOperation(ReadData(AiChatJsonContext.Default.AiChatCancellation)),
            "chat.run_queued" => RunQueued(ReadData(AiHostRequestJsonContext.Default.AiTurnQuery)),
            "mcp.inspect" => new AiInspectMcpOperation(ReadData(AiHostRequestJsonContext.Default.AiIdQuery).Id),
            "skill.list" => new AiListSkillsOperation(),
            "skill.read" => new AiReadSkillOperation(ReadData(AiHostRequestJsonContext.Default.AiIdQuery).Id ?? throw new ArgumentException("Missing skill id.")),
            "status" => new AiHostStatusOperation(),
            "local.status" => new AiLocalStatusOperation(),
            "local.models" => new AiLocalModelsOperation(),
            "local.unload" => new AiLocalUnloadOperation(),
            "models" => new AiListModelsOperation(ReadData(AiHostRequestJsonContext.Default.AiModelsQuery).ServiceId),
            "generate" => new AiGenerateOperation(ReadData(AiJsonContext.Default.AiTextRequest)),
            _ => throw new LauncherOperationException(method.StartsWith("chat.", StringComparison.Ordinal) ? "AI_CHAT_METHOD_UNIMPLEMENTED" : "AI_HOST_METHOD_UNKNOWN", "The requested AI host method is not implemented.")
        };
    }

    internal static JsonNode? Write(AiHostOperation operation) => operation switch
    {
        AiCreateConversationOperation item => JsonSerializer.SerializeToNode(item.Request, AiChatJsonContext.Default.AiChatCreate),
        AiListConversationsOperation item => JsonSerializer.SerializeToNode(new AiConversationListQuery(item.Offset, item.Limit), AiHostRequestJsonContext.Default.AiConversationListQuery),
        AiShowConversationOperation item => Conversation(item.Id),
        AiUpdateConversationOperation item => JsonSerializer.SerializeToNode(item.Request, AiChatJsonContext.Default.AiConversationUpdate),
        AiReviewRemovalOperation item => Conversation(item.Id),
        AiExportConversationOperation item => JsonSerializer.SerializeToNode(item.Request, AiChatJsonContext.Default.AiConversationExport),
        AiRemoveConversationOperation item => JsonSerializer.SerializeToNode(item.Request, AiChatJsonContext.Default.AiConversationRemoval),
        AiShowTurnOperation item => Turn(item.Id, item.Turn),
        AiReadEventsOperation item => JsonSerializer.SerializeToNode(new AiEventsQuery(item.Id, item.Turn, item.After, item.Limit == 100 ? null : item.Limit), AiHostRequestJsonContext.Default.AiEventsQuery),
        AiReadArchiveOperation item => JsonSerializer.SerializeToNode(item.Request, AiContextJsonContext.Default.AiContextRead),
        AiCheckEffectsOperation item => Turn(item.Id, item.Turn),
        AiAcknowledgeEffectsOperation item => JsonSerializer.SerializeToNode(item.Request, AiEffectJsonContext.Default.AiEffectAcknowledgement),
        AiRespondOperation item => JsonSerializer.SerializeToNode(item.Request, AiUserPromptJsonContext.Default.AiUserResponse),
        AiConfirmOperation item => JsonSerializer.SerializeToNode(item.Request, AiApprovalJsonContext.Default.AiToolConfirmation),
        AiReadConfirmationOperation item => JsonSerializer.SerializeToNode(item.Request, AiApprovalJsonContext.Default.AiToolConfirmationRead),
        AiSendOperation item => JsonSerializer.SerializeToNode(item.Request, AiChatJsonContext.Default.AiChatSend),
        AiCancelOperation item => JsonSerializer.SerializeToNode(item.Request, AiChatJsonContext.Default.AiChatCancellation),
        AiRunQueuedTurnOperation item => Turn(item.Id, item.Turn),
        AiInspectMcpOperation item => Id(item.Id),
        AiReadSkillOperation item => Id(item.Id),
        AiListModelsOperation item => JsonSerializer.SerializeToNode(new AiModelsQuery(item.ServiceId), AiHostRequestJsonContext.Default.AiModelsQuery),
        AiGenerateOperation item => JsonSerializer.SerializeToNode(item.Request, AiJsonContext.Default.AiTextRequest),
        AiListSkillsOperation or AiHostStatusOperation or AiLocalStatusOperation or AiLocalModelsOperation or AiLocalUnloadOperation => null,
        _ => throw new InvalidOperationException("Unknown Core AI operation.")
    };

    private static AiListConversationsOperation List(AiConversationListQuery query) => new(query.Offset ?? 0, query.Limit ?? 20);
    private static AiShowTurnOperation Turn(AiTurnQuery query) => new(query.ConversationId, query.TurnId);
    private static AiRunQueuedTurnOperation RunQueued(AiTurnQuery query) => new(query.ConversationId, query.TurnId);
    private static AiReadEventsOperation Events(AiEventsQuery query) => new(query.ConversationId, query.TurnId, query.After ?? 0, query.Limit ?? 100);
    private static AiCheckEffectsOperation Effects(AiTurnQuery query) => new(query.ConversationId, query.TurnId);
    private static JsonNode? Conversation(string id) => JsonSerializer.SerializeToNode(new AiConversationQuery(id), AiHostRequestJsonContext.Default.AiConversationQuery);
    private static JsonNode? Turn(string id, string turn) => JsonSerializer.SerializeToNode(new AiTurnQuery(id, turn), AiHostRequestJsonContext.Default.AiTurnQuery);
    private static JsonNode? Id(string? id) => JsonSerializer.SerializeToNode(new AiIdQuery(id), AiHostRequestJsonContext.Default.AiIdQuery);
}