// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Assistant;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Core AI API. Execution is in-process unless the caller explicitly selects the CLI background runtime.</summary>
public sealed class AssistantService(LauncherPaths paths)
{
    /// <summary>Gets persisted service/model configuration and credential ownership operations.</summary>
    public AiServiceStore Services { get; } = new(paths);
    /// <summary>Gets immutable reference-image snapshots for explicitly selected files or pasted images.</summary>
    public AiImageStore Images { get; } = new(paths);
    /// <summary>Gets explicitly registered external MCP, file-root and skill-directory configuration.</summary>
    public AiExtensionStore Extensions { get; } = new(paths);
    /// <summary>Gets ownership-safe external client integration planning and application.</summary>
    public AiClientIntegrationService Integrations { get; } = new(paths);
    /// <summary>Starts or reopens the exact repair conversation and its idempotent first turn. Without a model, preserves the incident for later continuation.</summary>
    public async Task<AiRepairReport> StartRepairAsync(AiRepairCreate request, string? modelId = null, string? title = null, AiHostOptions? host = null, CancellationToken ct = default)
    {
        var repair = await new LauncherClient(paths).Repairs.CreateAsync(request, ct).ConfigureAwait(false);
        if (repair.ConversationId is { } existing)
        {
            var saved = await GetConversationAsync(existing, host, ct).ConfigureAwait(false);
            if (saved.Turns?.Length > 0) return repair;
        }

        var config = await Services.GetAsync(ct).ConfigureAwait(false);
        modelId ??= config.DefaultModelId;
        if (modelId is null) return repair with { NeedsModel = true };

        var conversation = await CreateConversationAsync(new() { RepairId = repair.Id!, ModelId = modelId, Title = title }, host, ct).ConfigureAwait(false);
        await SendAsync(new()
        {
            ConversationId = conversation.Id!,
            ExpectedRevision = conversation.Revision!.Value,
            ClientKey = repair.Id!,
            ModelId = modelId,
            Prompt = title ?? "分析并修复这次故障。在已授权的当前实例范围内自动完成诊断、最小可回退修改和新的运行验证；无效试验先回退。只有缺少工具无法获取的关键信息时使用 ask_user。请说明实际验证到的范围。"
        }, host, ct).ConfigureAwait(false);
        return new LauncherClient(paths).Repairs.Read(repair.Id!);
    }
    /// <summary>Resolves attributed provider/lab artwork through the anonymous public catalog and local cache; no model credential is used.</summary>
    public async Task<AiModelIdentity> GetModelIdentityAsync(string? baseUrl, string model, CancellationToken ct = default)
    {
        var catalog = AiPublicModelCatalog.For(Path.Combine(paths.Root, "ai", "model-catalog"));
        var (Provider, Lab, Svg) = await catalog.IdentityAsync(model, Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri : null, ct).ConfigureAwait(false);
        return new(Provider, Lab, Svg);
    }

    /// <summary>Serves the current-user data-root RPC endpoint until cancellation or idle shutdown.</summary>
    public Task RunHostAsync(CancellationToken ct = default) => AiRuntime.For(paths).RunAsync(ct);

    private async Task<T> ReadAsync<T>(AiHostOptions? host, AiHostOperation operation, JsonTypeInfo<T> metadata, CancellationToken ct = default)
    {
        if (host is null) return (T)await AiRuntime.For(paths).InvokeAsync(operation, ct).ConfigureAwait(false);
        var response = await new AiHostClient(paths, host).InvokeAsync(operation, ct).ConfigureAwait(false);
        // Durable records can contain future fields, explicit nulls and noncanonical values.
        // Project them at the transport read boundary just as the local store does.
        if (typeof(T) == typeof(AiChatRecord)) return (T)(object)AiChatRecord.Read(response);
        if (typeof(T) == typeof(AiConversationPage)) return (T)(object)AiConversationPage.Read(response);
        if (typeof(T) == typeof(AiEventPage)) return (T)(object)AiEventPage.Read(response);
        if (typeof(T) == typeof(AiServiceModelCatalogReport)) return (T)(object)AiProviderReports.ReadCatalog(response);
        if (typeof(T) == typeof(AiGenerationReport)) return (T)(object)AiProviderReports.ReadGeneration(response);
        return response.Deserialize(metadata) ?? throw new InvalidDataException("The AI host returned an empty read result.");
    }

    /// <summary>Inspects explicitly configured MCP registrations with open external schemas and source-specific failures.</summary>
    public Task<AiMcpCatalog> InspectMcpAsync(string? id = null, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiInspectMcpOperation(id), AiJsonContext.Default.AiMcpCatalog, ct);

    /// <summary>Creates and durably registers a conversation with its selected model and instance scope.</summary>
    public Task<AiChatRecord> CreateConversationAsync(AiChatCreate request, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiCreateConversationOperation(request), AiJsonContext.Default.AiChatRecord, ct);
    /// <summary>Reads a bounded conversation page without replaying model or tool calls.</summary>
    public Task<AiConversationPage> ListConversationsAsync(int offset = 0, int limit = 20, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiListConversationsOperation(offset, limit), AiJsonContext.Default.AiConversationPage, ct);
    /// <summary>Reads one durable conversation with sanitized turn snapshots.</summary>
    public Task<AiChatRecord> GetConversationAsync(string conversationId, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiShowConversationOperation(conversationId), AiJsonContext.Default.AiChatRecord, ct);
    /// <summary>Commits one idempotent submission and queues its frozen request for serial execution in this host.</summary>
    public Task<AiChatRecord> SendAsync(AiChatSend request, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiSendOperation(request with { ImageIds = request.ImageIds?.ToArray() }), AiJsonContext.Default.AiChatRecord, ct);
    /// <summary>Reads a turn without exposing provider protocol state or full checkpoint excerpts.</summary>
    public Task<AiChatRecord> GetTurnAsync(string conversationId, string turnId, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiShowTurnOperation(conversationId, turnId), AiJsonContext.Default.AiChatRecord, ct);
    /// <summary>Reads durable events after the supplied cursor, never replaying tool effects.</summary>
    public Task<AiEventPage> GetEventsAsync(string conversationId, string turnId, long after = 0, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiReadEventsOperation(conversationId, turnId, after), AiJsonContext.Default.AiEventPage, ct);
    /// <summary>Reads the complete ordered history of an already terminal turn through the in-process Core, without polling or replaying effects.</summary>
    public Task<AiEventPage> GetCompletedTurnEventsAsync(string conversationId, string turnId, CancellationToken ct = default)
        => ReadAsync(null, new AiReadCompletedTurnEventsOperation(conversationId, turnId), AiJsonContext.Default.AiEventPage, ct);
    /// <summary>Signals the selected turn's owner without replaying model or tool work.</summary>
    public Task<AiChatRecord> CancelTurnAsync(string conversationId, string turnId, AiHostOptions? host = null, CancellationToken ct = default)
        => CancelTurnAsync(new AiChatCancellation { ConversationId = conversationId, TurnId = turnId }, host, ct);
    /// <summary>Cancels the selected turn, optionally canceling its pending conversation queue before execution can advance.</summary>
    public Task<AiChatRecord> CancelTurnAsync(AiChatCancellation request, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiCancelOperation(request), AiJsonContext.Default.AiChatRecord, ct);
    /// <summary>Runs an existing queued message next, stopping this conversation's active turn and preserving every other pending message.</summary>
    public Task<AiChatRecord> RunQueuedTurnAsync(string conversationId, string turnId, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiRunQueuedTurnOperation(conversationId, turnId), AiJsonContext.Default.AiChatRecord, ct);
    /// <summary>Observes incomplete effects and persists verification evidence without replaying prior mutations.</summary>
    public Task<AiChatRecord> CheckTurnEffectsAsync(string conversationId, string turnId, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiCheckEffectsOperation(conversationId, turnId), AiJsonContext.Default.AiChatRecord, ct);
    /// <summary>Records acceptance of the exact remaining uncertainty, not proof of success or permission to replay.</summary>
    public Task<AiChatRecord> AcknowledgeUnknownEffectAsync(AiEffectAcknowledgement request, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiAcknowledgeEffectsOperation(request), AiJsonContext.Default.AiChatRecord, ct);
    /// <summary>Submits an explicit answer or skip for the exact waiting question; never grants operation permission.</summary>
    public Task<AiUserAnswer> RespondToUserPromptAsync(AiUserResponse request, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiRespondOperation(request with { OptionIds = [.. request.OptionIds] }), AiJsonContext.Default.AiUserAnswer, ct);

    /// <summary>Records an explicit decision for the current immutable operation/review binding.</summary>
    public Task<AiToolConfirmationReceipt> ConfirmToolAsync(AiToolConfirmation request, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiConfirmOperation(request), AiJsonContext.Default.AiToolConfirmationReceipt, ct);
    /// <summary>Reads a bounded page of an exact confirmation binding without approving it.</summary>
    public Task<AiConfirmationPage> ReadToolConfirmationAsync(AiToolConfirmationRead request, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiReadConfirmationOperation(request), AiJsonContext.Default.AiConfirmationPage, ct);
    /// <summary>Applies explicit conversation changes only at the expected revision.</summary>
    public Task<AiChatRecord> UpdateConversationAsync(AiConversationUpdate request, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiUpdateConversationOperation(request), AiJsonContext.Default.AiChatRecord, ct);
    /// <summary>Reads the current removal review without canceling turns, jobs or removing history.</summary>
    public Task<AiConversationRemovalReview> ReviewConversationRemovalAsync(string conversationId, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiReviewRemovalOperation(conversationId), AiJsonContext.Default.AiConversationRemovalReview, ct);
    /// <summary>Removes exactly reviewed history after stopping owned turns, without canceling independent jobs.</summary>
    public Task<AiConversationRemovalReceipt> RemoveConversationAsync(AiConversationRemoval request, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiRemoveConversationOperation(request), AiJsonContext.Default.AiConversationRemovalReceipt, ct);
    /// <summary>Exports portable conversation data without private provider protocol state.</summary>
    public Task<AiConversationExportReceipt> ExportConversationAsync(AiConversationExport request, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiExportConversationOperation(request), AiJsonContext.Default.AiConversationExportReceipt, ct);
    /// <summary>Reads a validated Unicode-safe page of archived context.</summary>
    public Task<AiContextPage> ReadContextArchiveAsync(AiContextRead request, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiReadArchiveOperation(request), AiJsonContext.Default.AiContextPage, ct);
    /// <summary>Lists enabled skill metadata and source-specific warnings without executing scripts.</summary>
    public Task<AiSkillCatalog> ListSkillsAsync(AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiListSkillsOperation(), AiJsonContext.Default.AiSkillCatalog, ct);
    /// <summary>Reads a source-qualified skill's instruction body.</summary>
    public Task<AiSkillReport> ReadSkillAsync(string id, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiReadSkillOperation(id), AiJsonContext.Default.AiSkillReport, ct);

    /// <summary>Observes runtime identity, ownership and storage failure without starting a model.</summary>
    public Task<AiHostStatus> GetHostAsync(AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiHostStatusOperation(), AiJsonContext.Default.AiHostStatus, ct);
    /// <summary>Observes this Core runtime's Embedded engine without loading a model or discovering an external daemon.</summary>
    public Task<AiLocalInferenceStatus> GetLocalInferenceStatusAsync(AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiLocalStatusOperation(), AiJsonContext.Default.AiLocalInferenceStatus, ct);
    /// <summary>Lists references in the inference library's configured model store without downloading weights.</summary>
    public Task<AiLocalModelCatalog> ListLocalModelsAsync(AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiLocalModelsOperation(), AiJsonContext.Default.AiLocalModelCatalog, ct);
    /// <summary>Waits for active local requests and releases only the engine and native workers owned by this runtime.</summary>
    public Task<AiLocalInferenceStatus> UnloadLocalInferenceAsync(AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiLocalUnloadOperation(), AiJsonContext.Default.AiLocalInferenceStatus, ct);

    /// <summary>Lists normalized provider model references and the captured connection without changing configured bindings.</summary>
    public Task<AiServiceModelCatalogReport> ListModelsAsync(string serviceId, AiHostOptions? host = null, CancellationToken ct = default)
        => ReadAsync(host, new AiListModelsOperation(serviceId), AiJsonContext.Default.AiServiceModelCatalogReport, ct);

    /// <summary>Discovers an unsaved online connection's models without persisting its credential or configuration.</summary>
    /// <param name="connection">Explicit protocol, endpoint and HTTP consent from the open settings form.</param>
    /// <param name="secret">Optional credential retained only for this discovery request.</param>
    /// <param name="ct">Cancels discovery without changing saved configuration.</param>
    /// <returns>The provider's actual model catalog; successful discovery does not establish generation support.</returns>
    public static async Task<AiModelCatalogReport> DiscoverModelsAsync(AiServiceUpdate connection, string? secret = null, CancellationToken ct = default)
    {
        var protocol = connection.Protocol ?? AiProtocols.OpenAiResponses;
        if (!AiProtocols.IsKnown(protocol) || protocol == AiProtocols.RodelInference)
            throw new ArgumentException("Use an online provider protocol for connection discovery; local models have a separate catalog.");
        var endpoint = AiServiceStore.NormalizeEndpoint(connection.BaseUrl, protocol, connection.AllowUnencryptedHttp ?? false)!;
        try
        {
            var result = await AiProviderRequests.ExecuteAsync(new(protocol, 0, string.Empty, string.Empty, secret, null, null), new Uri(endpoint), null, 30, ct).ConfigureAwait(false);
            return AiProviderReports.Discovery((AiModelCatalogResult)result);
        }
        catch (AiProtocolException error) { throw new LauncherOperationException(error.Code, error.Message, error.Details); }
    }

    /// <summary>Runs one foreground generation without persistent conversation or tool execution.</summary>
    public Task<AiGenerationReport> GenerateAsync(AiTextRequest request, AiHostOptions? host = null, CancellationToken ct = default)
    {
        ValidateRequest(request);
        return ReadAsync(host, new AiGenerateOperation(request), AiJsonContext.Default.AiGenerationReport, ct);
    }

    /// <summary>Validates generation settings without truncation; an image attachment may provide the input for an empty text draft.</summary>
    public static void ValidateRequest(AiTextRequest request, bool hasImages = false)
    {
        if ((!hasImages && string.IsNullOrWhiteSpace(request.Prompt)) || request.Prompt.Length > 64000)
            throw new ArgumentException("A single request needs nonempty text of at most 64000 characters. It is not silently truncated.");
        if (request.TimeoutSeconds is < 1 or > 3600)
            throw new ArgumentException("The model request timeout must be between 1 and 3600 seconds.");
        if (request.Temperature is { } temperature && !double.IsFinite(temperature))
            throw new ArgumentException("Temperature must be finite.");
        if (request.MaximumOutputTokens is <= 0)
            throw new ArgumentException("The output-token limit must be positive.");
    }
}
/// <summary>Public catalog identity and optional cached SVG; provider and model-author identities remain distinct.</summary>
/// <param name="ProviderId">An exact endpoint-host match, otherwise null.</param>
/// <param name="LabId">An unambiguous canonical model author, otherwise null.</param>
/// <param name="LogoSvg">Validated, anonymously downloaded cached artwork, otherwise null.</param>
public sealed record AiModelIdentity(string? ProviderId, string? LabId, string? LogoSvg);