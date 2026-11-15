// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant;

internal static class AiChatClients
{
    private const string ActualModelKey = "rodel.actualModelId";
    /// <summary>Identifies the local ActivitySource/Meter; this pipeline configures no exporter or external collector.</summary>
    public const string TelemetrySource = "Rodel.MinecraftLauncher.AI";

    /// <summary>Builds a self-authored provider pipeline with explicit privacy, without caching, automatic retries or function invocation.</summary>
    public static IChatClient Create(AiProviderContext snapshot, Uri? endpoint)
    {
        if (!AiProviderProtocols.IsKnown(snapshot.Protocol))
            throw new AiProtocolException("AI_PROTOCOL_NOT_IMPLEMENTED", "This service protocol is not implemented; no other protocol was substituted.");
        IChatClient provider = snapshot.Protocol == AiProviderProtocols.RodelInference
#if RODEL_LOCAL_INFERENCE
            ? new LocalClient(snapshot)
#else
            ? throw AiLocalInferenceRuntime.Unavailable()
#endif
            : new AiHttpChatClient(snapshot, endpoint ?? throw new ArgumentException("An HTTP provider requires its endpoint."));
        var telemetry = new OpenTelemetryChatClient(new DiagnosticClient(provider, snapshot), sourceName: TelemetrySource)
        {
            EnableSensitiveData = false
        };
        return new CompletionClient(telemetry, snapshot);
    }

    /// <summary>Applies temporary standard options to one captured model binding.</summary>
    public static AiProviderContext ApplyOptions(AiProviderContext snapshot, ChatOptions? options)
    {
        if (options?.ModelId is { } model && model != snapshot.Model && model != snapshot.ModelId)
            throw new ArgumentException("Capture the selected model binding before changing the provider model.");
        if (options?.ResponseFormat is ChatResponseFormatJson { Schema: not null })
            throw new NotSupportedException("This provider uses schema prompting, not native JSON-schema enforcement.");
        return snapshot with
        {
            Temperature = options?.Temperature is { } temperature ? temperature : snapshot.Temperature,
            MaxOutputTokens = options?.MaxOutputTokens ?? snapshot.MaxOutputTokens
        };
    }

    /// <summary>Restores observed-only identity aliases and binds native state after complete standard aggregation.</summary>
    public static ChatResponse Complete(AiProviderContext snapshot, ChatResponse response)
    {
        if (response.AdditionalProperties?.Remove(ActualModelKey, out var rawModel) == true && rawModel is string actualModel)
            response.ModelId = actualModel;
        foreach (var message in response.Messages)
        {
            if (message.AdditionalProperties?.Remove(ActualModelKey, out var rawMessageModel) == true && rawMessageModel is string messageModel)
                response.ModelId = messageModel;
        }

        if (snapshot.Protocol == AiProviderProtocols.RodelInference)
        {
            foreach (var message in response.Messages)
            {
                AiChatMessages.Bind(message, new(snapshot.Protocol, snapshot.ModelId, snapshot.ServiceRevision, new JsonObject()));
            }
        }

        return AiChatMessages.FinalizeResponse(response);
    }

#if RODEL_LOCAL_INFERENCE
    private sealed class LocalClient(AiProviderContext snapshot) : IChatClient
    {
        /// <inheritdoc />
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => await GetStreamingResponseAsync(messages, options, cancellationToken)
                .ToChatResponseAsync(cancellationToken).ConfigureAwait(false);

        /// <inheritdoc />
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var captured = ApplyOptions(snapshot, options);
            var runtime = captured.LocalInference ?? throw new AiProtocolException("AI_LOCAL_INFERENCE_UNAVAILABLE", "The owning Core did not supply its local inference runtime.");
            var lease = await runtime.AcquireAsync(cancellationToken).ConfigureAwait(false);
            await using var lifetime = lease.ConfigureAwait(false);
            using var client = await lease.Engine.GetChatClientAsync(captured.Model, cancellationToken, captured.ContextLength).ConfigureAwait(false);
            var effective = options?.Clone() ?? new ChatOptions();
            if (effective.ModelId == snapshot.ModelId) effective.ModelId = captured.Model;
            effective.Temperature ??= captured.Temperature is { } temperature ? (float)temperature : null;
            effective.MaxOutputTokens ??= captured.MaxOutputTokens;
            var characters = 0;
            await foreach (var update in client.GetStreamingResponseAsync(
                AiChatMessages.ForProvider(captured, [.. messages]), effective, cancellationToken).ConfigureAwait(false))
            {
                foreach (var content in update.Contents)
                {
                    if (content is FunctionCallContent { Exception: not null })
                        throw new AiProtocolException("AI_PROVIDER_PROTOCOL_ERROR", "The inference service returned invalid assembled tool input.");
                    characters += content switch
                    {
                        TextContent text => text.Text.Length,
                        TextReasoningContent reasoning => reasoning.Text?.Length ?? 0,
                        _ => 0
                    };
                }

                if (characters > 256000)
                    throw new AiProtocolException("AI_RESPONSE_TOO_LARGE", "The inference response exceeded its content budget.");
                yield return update;
            }
        }

        /// <inheritdoc />
        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is not null ? null : serviceType == typeof(ChatClientMetadata)
                ? new ChatClientMetadata(AiProviderProtocols.RodelInference, defaultModelId: DiagnosticModel(snapshot))
                : serviceType.IsInstanceOfType(this) ? this : null;
        /// <inheritdoc />
        public void Dispose() { }
    }

#endif

    private static bool IsPrivateModelReference(string? value)
        => !string.IsNullOrEmpty(value) && (Path.IsPathFullyQualified(value)
            || (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile));

    /// <summary>Uses a binding alias instead of a private model-file reference in provider telemetry metadata.</summary>
    internal static string DiagnosticModel(AiProviderContext snapshot)
        => IsPrivateModelReference(snapshot.Model) ? snapshot.ModelId : snapshot.Model;

    // Request aliases are applied before observation; real response identity is restored only after observation ends.
    private sealed class CompletionClient(IChatClient inner, AiProviderContext snapshot) : DelegatingChatClient(inner)
    {
        /// <inheritdoc />
        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Complete(snapshot, await base.GetResponseAsync(messages, ObservedOptions(options), cancellationToken).ConfigureAwait(false));

        /// <inheritdoc />
        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => base.GetStreamingResponseAsync(messages, ObservedOptions(options), cancellationToken);

        private ChatOptions? ObservedOptions(ChatOptions? options)
        {
            if (options?.ModelId is not { } model) return options;
            if (model != snapshot.Model && model != snapshot.ModelId)
                throw new ArgumentException("Capture the selected model binding before changing the provider model.");
            if (!IsPrivateModelReference(model)) return options;
            var observed = options.Clone();
            observed.ModelId = snapshot.ModelId;
            return observed;
        }
    }

    // MEAI puts exception messages on spans even when message-content capture is disabled.
    private sealed class DiagnosticClient(IChatClient inner, AiProviderContext snapshot) : DelegatingChatClient(inner)
    {
        /// <inheritdoc />
        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
                if (IsPrivateModelReference(response.ModelId))
                {
                    (response.AdditionalProperties ??= [])[ActualModelKey] = response.ModelId;
                    response.ModelId = null;
                }

                return response;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
            catch (Exception error) { throw SafeError(error, snapshot); }
        }

        /// <inheritdoc />
        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await using var source = base.GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                ChatResponseUpdate update;
                try
                {
                    if (!await source.MoveNextAsync().ConfigureAwait(false)) break;
                    update = source.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
                catch (Exception error) { throw SafeError(error, snapshot); }

                if (IsPrivateModelReference(update.ModelId))
                {
                    (update.AdditionalProperties ??= [])[ActualModelKey] = update.ModelId;
                    update.ModelId = null;
                }

                yield return update;
            }
        }
    }

    internal static AiProtocolException SafeError(Exception error, AiProviderContext snapshot)
    {
        var code = error switch
        {
            AiProtocolException protocol => protocol.Code,
#if RODEL_LOCAL_INFERENCE
            Rodel.Inference.Core.ModelLoadException load when load.Error.Kind == Rodel.Inference.InferenceErrorKind.InvalidRequest
                && AiHttpTransport.IsContextLimit("invalid_request_error", load.Error.Detail) => "AI_CONTEXT_LIMIT",
            Rodel.Inference.Core.ModelLoadException load => load.Error.Kind switch
            {
                Rodel.Inference.InferenceErrorKind.InvalidRequest => "AI_PROVIDER_OPTIONS_INVALID",
                Rodel.Inference.InferenceErrorKind.ModelNotFound => "AI_MODEL_NOT_FOUND",
                Rodel.Inference.InferenceErrorKind.Unauthorized => "AI_AUTHENTICATION_FAILED",
                Rodel.Inference.InferenceErrorKind.Overloaded => "AI_RATE_LIMITED",
                _ => "AI_INFERENCE_REQUEST_FAILED"
            },
#endif
            OperationCanceledException => "AI_SERVICE_TIMEOUT",
            HttpRequestException => "AI_SERVICE_UNREACHABLE",
            IOException => "AI_RESPONSE_INTERRUPTED",
            NotSupportedException => "AI_PROVIDER_OPTION_NOT_SUPPORTED",
            ArgumentException => "AI_PROVIDER_OPTIONS_INVALID",
            JsonException or InvalidOperationException or FormatException => snapshot.Protocol == AiProviderProtocols.RodelInference
                ? "AI_INFERENCE_REQUEST_FAILED" : "AI_PROVIDER_PROTOCOL_ERROR",
            _ => "AI_PROVIDER_REQUEST_FAILED"
        };
        var providerDetails = (error as AiProtocolException)?.Details?.DeepClone();
        var details = providerDetails as JsonObject ?? [];
        if (providerDetails is not null and not JsonObject) details["providerDetails"] = providerDetails;
        details["providerMessage"] = SecretScrubber.Excerpt(error.Message, snapshot.Credential, 2048);
#if RODEL_LOCAL_INFERENCE
        if (error is Rodel.Inference.Core.ModelLoadException inference)
        {
            details["inferenceKind"] = inference.Error.Kind.ToString();
            if (!string.IsNullOrWhiteSpace(inference.Error.Detail))
                details["inferenceDetail"] = SecretScrubber.Excerpt(inference.Error.Detail, snapshot.Credential, 4096);
        }
#endif

        SecretScrubber.Scrub(details, snapshot.Credential);
        return new(code, $"{code}: The model request failed; see its diagnostic details.", details);
    }
}