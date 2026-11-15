// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using Microsoft.ML.Tokenizers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Assistant;

internal sealed record AiTokenMeasurement(long? EstimatedInputTokens, long? ServerInputTokens, long? ContextTokens,
    long? InputTokenLimit, string CountSource, string? LimitSource, long? CatalogContextTokens = null, long? CatalogInputTokenLimit = null,
    int? OutputTokenLimit = null, string? OutputLimitSource = null, bool ImageTokensIncluded = true)
{
    /// <summary>Gets the server count when available, otherwise the calibrated local approximation.</summary>
    public long? InputTokens => ServerInputTokens ?? EstimatedInputTokens;
    /// <summary>Compares an input-only limit separately from a shared context window and reserved output.</summary>
    public bool NearLimit(int outputTokens) => ImageTokensIncluded && ((InputTokenLimit is { } input && InputTokens >= input * .9)
        || (ContextTokens is { } context && InputTokens + outputTokens >= context * .9));
}

internal sealed record AiCountResponse([property: JsonPropertyName("input_tokens")] long? InputTokens, long? TotalTokens);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AiCountResponse))]
[JsonSerializable(typeof(AiTokenMeasurement))]
internal sealed partial class AiTokenJsonContext : JsonSerializerContext;

/// <summary>Counts the actual request with provider APIs when supported; otherwise uses a clearly labelled Tiktoken approximation.</summary>
internal sealed class AiTokenCounter(AiProviderContext snapshot, Uri? endpoint, JsonObject? catalogSnapshot = null) : IDisposable
{
    private static readonly Lazy<TiktokenTokenizer> Tokenizer = new(() => TiktokenTokenizer.CreateForEncoding("o200k_base"));
    private static readonly JsonSerializerOptions TextOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly HttpClient? _http = endpoint is null ? null : AiHttpTransport.CreateClient(endpoint, snapshot.Credential, snapshot.Protocol);
    private bool _metadataChecked;
    private bool _countUnavailable;
    private long? _context;
    private long? _catalogContext;
    private long? _catalogInput;
    private long? _inputLimit;
    private string? _limitSource;
    private int? _outputLimit;
    private string? _outputSource;

    /// <summary>Returns the local BPE count of the full input representation, including tools and native continuation state; it is not a provider-exact count.</summary>
    public static long? Estimate(AiProviderContext context, IReadOnlyList<ChatMessage> history, IReadOnlyList<AIFunctionDeclaration> tools, int? outputTokens)
    {
        try
        {
            history = [.. history.Select(message =>
            {
                if (!message.Contents.OfType<DataContent>().Any()) return message;
                var copy = message.Clone();
                copy.Contents = [.. message.Contents.Where(content => content is not DataContent)];
                return copy;
            })];
            JsonNode input;
            if (context.Protocol == AiProviderProtocols.RodelInference)
            {
                var portable = JsonSerializer.SerializeToNode(AiChatMessages.ForProvider(context, history),
                    AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IList<ChatMessage>)));
                input = new JsonObject { ["messages"] = portable, ["tools"] = new JsonArray(tools.Select(tool => (JsonNode)new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["schema"] = AiToolDeclarations.Schema(tool) }).ToArray()) };
            }
            else
            {
                var body = Wire(context, history, tools, outputTokens);
                foreach (var key in new[] { "stream", "stream_options", "max_output_tokens", "max_completion_tokens", "max_tokens", "temperature", "store", "include", "generationConfig" }) body.Remove(key);
                input = body;
            }

            return Tokenizer.Value.CountTokens(input.ToJsonString(TextOptions));
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or IOException or TypeInitializationException or System.Reflection.TargetInvocationException) { return null; }
    }

    /// <summary>Resolves output capacity from the same provider/catalog metadata used by chat, without submitting text for token counting.</summary>
    public async Task<int?> GetOutputLimitAsync(CancellationToken ct)
    {
        await ReadMetadataAsync(ct).ConfigureAwait(false);
        return _outputLimit;
    }

    private async Task ReadMetadataAsync(CancellationToken ct)
    {
        if (!_metadataChecked)
        {
            _metadataChecked = true;
            var (Context, Input, Source, MatchedProvider) = AiPublicModelCatalog.Find(snapshot.Model, endpoint, catalogSnapshot);
            _catalogContext = Context;
            _catalogInput = Input;
            _outputLimit = AiPublicModelCatalog.OutputLimit(snapshot.Model, endpoint, catalogSnapshot);
            _outputSource = _outputLimit is null ? null : "models.dev_snapshot";
            _context = snapshot.ContextLength ?? (MatchedProvider ? Context : null);
            _inputLimit = MatchedProvider ? Input : null;
            _limitSource = snapshot.ContextLength is not null ? "embedded_engine_context" : Source;
            if (_http is not null && endpoint is not null)
            {
                var route = snapshot.Protocol == AiProviderProtocols.Gemini
                    ? TextProviderProtocols.GeminiRoute(snapshot.Model, "countTokens")[..^":countTokens".Length]
                    : "models/" + Uri.EscapeDataString(snapshot.Model);
                var metadata = await OptionalRequestAsync(AiHttpTransport.Route(endpoint, route), null, ct).ConfigureAwait(false);
                var context = Positive(metadata?["context_window"] ?? metadata?["context_length"]);
                var input = Positive(metadata?["inputTokenLimit"] ?? metadata?["max_input_tokens"]);
                var output = Positive(metadata?["max_output_tokens"] ?? metadata?["outputTokenLimit"] ?? metadata?["max_tokens"]);
                if (output is > 0 and <= int.MaxValue) { _outputLimit = (int)output.Value; _outputSource = "provider_model_metadata"; }

                if (context is not null || input is not null)
                {
                    _context = context;
                    _inputLimit = input;
                    _limitSource = "provider_model_metadata";
                }
            }
        }
    }

    /// <summary>Reads server metadata once and counts each complete request; unsupported optional endpoints never block generation.</summary>
    public async Task<AiTokenMeasurement> MeasureAsync(IReadOnlyList<ChatMessage> history, IReadOnlyList<AIFunctionDeclaration> tools, int? outputTokens, double calibration, CancellationToken ct, bool calibrated = false)
    {
        var local = Estimate(snapshot, history, tools, outputTokens);
        long? estimate = local is { } count ? Math.Max(1, (long)Math.Ceiling(count * calibration)) : null;
        await ReadMetadataAsync(ct).ConfigureAwait(false);
        long? server = null;
        if (!_countUnavailable && _http is not null && endpoint is not null && snapshot.Protocol is not (AiProviderProtocols.OpenAiChat or AiProviderProtocols.RodelInference))
        {
            var wire = Wire(snapshot, history, tools, outputTokens);
            string route;
            JsonObject countBody;
            if (snapshot.Protocol == AiProviderProtocols.Gemini)
            {
                route = TextProviderProtocols.GeminiRoute(snapshot.Model, "countTokens");
                wire["model"] = snapshot.Model.StartsWith("models/", StringComparison.Ordinal) ? snapshot.Model : "models/" + snapshot.Model;
                countBody = new JsonObject { ["generateContentRequest"] = wire };
            }
            else
            {
                var responses = snapshot.Protocol == AiProviderProtocols.OpenAiResponses;
                route = responses ? "responses/input_tokens" : "messages/count_tokens";
                var fields = responses ? new[] { "model", "input", "instructions", "tools", "tool_choice", "text", "reasoning" }
                    : ["model", "messages", "system", "tools", "tool_choice", "thinking"];
                countBody = [];
                foreach (var field in fields) if (wire[field] is { } value) countBody[field] = value.DeepClone();
            }

            var response = await OptionalRequestAsync(AiHttpTransport.Route(endpoint, route), countBody, ct).ConfigureAwait(false);
            try
            {
                var counted = response?.Deserialize(AiTokenJsonContext.Default.AiCountResponse);
                server = counted?.InputTokens ?? counted?.TotalTokens;
            }
            catch (JsonException) { server = null; }

            if (server is < 0) server = null;
            _countUnavailable = server is null;
        }

        var imagesCounted = server is not null || !history.Any(message => message.Contents.OfType<DataContent>().Any());
        return new(estimate, server, _context, _inputLimit, server is null ? estimate is null ? "unavailable" : !imagesCounted ? "tiktoken_text_only_estimate"
            : calibrated ? "tiktoken_o200k_calibrated_estimate" : "tiktoken_o200k_estimate" : "provider_token_count",
            _limitSource, _catalogContext, _catalogInput, _outputLimit, _outputSource, imagesCounted);
    }

    private async Task<JsonObject?> OptionalRequestAsync(Uri uri, JsonObject? body, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try { return (await AiHttpTransport.SendJsonAsync(_http!, uri, body, snapshot.Credential, deadline.Token).ConfigureAwait(false)).Body; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (Exception error) when (error is AiProtocolException or HttpRequestException or JsonException) { return null; }
    }

    private static long? Positive(JsonNode? node) => node is JsonValue value && value.TryGetValue<long>(out var number) && number > 0 ? number : null;

    private static JsonObject Wire(AiProviderContext context, IReadOnlyList<ChatMessage> history, IReadOnlyList<AIFunctionDeclaration> tools, int? outputTokens)
        => JsonSerializer.SerializeToNode(AiChatWire.Build(context, history, tools, outputTokens), AssistantJsonContext.Default.AiProviderRequest)!.AsObject();

    /// <summary>Closes the owned optional counting/metadata HTTP transport.</summary>
    public void Dispose() => _http?.Dispose();
}