// Copyright (c) Richasy. All rights reserved.

using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant;

// Adapts the native message/part mappings in Rodel.Agent.Core without importing its agent framework.
internal static class TextProviderProtocols
{
    /// <summary>Executes an Anthropic or Gemini text-only probe without offering tools or retaining conversation state.</summary>
    public static async Task<AiHttpTextResult> GenerateAsync(HttpClient http, Uri endpoint, AiProviderContext snapshot, string prompt, CancellationToken ct)
    {
        var model = snapshot;
        var anthropic = snapshot.Protocol == AiProviderProtocols.AnthropicMessages;
        AiProviderRequest body;
        Uri target;
        if (anthropic)
        {
            body = new AiAnthropicRequest
            {
                Model = model.Model,
                Messages = new(new JsonObject { ["role"] = "user", ["content"] = prompt }),
                MaxTokens = model.MaxOutputTokens,
                Temperature = model.Temperature
            };
            target = AiHttpTransport.Route(endpoint, "messages");
        }
        else
        {
            target = AiHttpTransport.Route(endpoint, GeminiRoute(model.Model, "generateContent"));
            body = new AiGeminiRequest
            {
                Contents = new(new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray(new JsonObject { ["text"] = prompt })
                }),
                GenerationConfig = model.Temperature is null && model.MaxOutputTokens is null ? null
                    : new() { Temperature = model.Temperature, MaxOutputTokens = model.MaxOutputTokens }
            };
        }

        var (Body, RequestId) = await AiHttpTransport.SendAsync(http, target, body, snapshot.Credential, ct).ConfigureAwait(false);
        var result = anthropic ? ReadAnthropic(Body) : ReadGemini(Body);
        return result with { RequestId = RequestId };
    }

    internal static string GeminiRoute(string model, string operation)
    {
        var resource = model.StartsWith("models/", StringComparison.Ordinal) || model.StartsWith("tunedModels/", StringComparison.Ordinal)
            ? model : "models/" + model;
        var segments = resource.Split('/');
        if (segments.Any(segment => segment is "." or ".." or ""))
            throw new ArgumentException("A Gemini model resource must not contain empty or traversal path segments.");
        return string.Join('/', segments.Select(Uri.EscapeDataString)) + ":" + operation;
    }

    /// <summary>Reads the bounded native model directory with duplicate and pagination-loop detection.</summary>
    public static async Task<AiHttpModelCatalogResult> ListModelsAsync(HttpClient http, Uri endpoint, string protocol, string? secret, CancellationToken ct)
    {
        var anthropic = protocol == AiProviderProtocols.AnthropicMessages;
        var result = new List<AiProviderModel>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        string? requestId = null;
        for (var page = 0; page < 100; page++)
        {
            var target = AiHttpTransport.Route(endpoint, "models");
            if (cursor is not null)
            {
                var builder = new UriBuilder(target);
                var query = target.Query.TrimStart('?');
                builder.Query = (query.Length == 0 ? string.Empty : query + "&") + (anthropic ? "after_id=" : "pageToken=") + Uri.EscapeDataString(cursor);
                target = builder.Uri;
            }

            var (Body, RequestId) = await AiHttpTransport.SendAsync(http, target, null, secret, ct).ConfigureAwait(false);
            requestId = RequestId ?? requestId;
            var items = Body[anthropic ? "data" : "models"] as JsonArray
                ?? throw new AiProtocolException("AI_PROVIDER_PROTOCOL_ERROR", "The service returned no native model directory array. Manually configured models are unchanged.");
            foreach (var item in items)
            {
                var id = item?[anthropic ? "id" : "name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(id))
                    throw new AiProtocolException("AI_PROVIDER_PROTOCOL_ERROR", "The model directory contains an entry without an identifier.");
                if (ids.Add(id))
                    result.Add(new(id, item?[anthropic ? "display_name" : "displayName"]?.GetValue<string>() ?? id));
                if (result.Count > 10000)
                    throw new AiProtocolException("AI_RESPONSE_TOO_LARGE", "The model directory exceeds the bounded discovery size. Manual model configuration remains available.");
            }

            cursor = anthropic
                ? Body["has_more"]?.GetValue<bool>() == true
                    ? Body["last_id"]?.GetValue<string>() ?? throw new AiProtocolException("AI_PROVIDER_PROTOCOL_ERROR", "The paged model directory omitted last_id.")
                    : null
                : Body["nextPageToken"]?.GetValue<string>();
            if (string.IsNullOrEmpty(cursor))
                return new([.. result], requestId);
            if (!cursors.Add(cursor))
                throw new AiProtocolException("AI_PROVIDER_PROTOCOL_ERROR", "The model directory repeated its pagination cursor.");
        }

        throw new AiProtocolException("AI_RESPONSE_TOO_LARGE", "The model directory exceeds the bounded page count. Manual model configuration remains available.");
    }

    private static AiHttpTextResult ReadAnthropic(JsonObject response)
    {
        var blocks = response["content"] as JsonArray
            ?? throw new AiProtocolException("AI_PROVIDER_PROTOCOL_ERROR", "The Anthropic response has no content array.");
        var text = new StringBuilder();
        foreach (var block in blocks)
        {
            if (block?["type"]?.ToString() is "tool_use" or "server_tool_use")
                throw UnexpectedTool();
            if (block?["type"]?.ToString() == "text")
                text.Append(block["text"]?.GetValue<string>());
        }

        var stop = response["stop_reason"]?.GetValue<string>();
        return new AiHttpTextResult
        {
            Status = stop == "refusal" ? "refused" : text.Length > 0 && stop is "end_turn" or "stop_sequence" ? "completed" : "incomplete",
            Text = text.ToString(),
            Refusal = stop == "refusal" ? text.ToString() : null,
            FinishReason = stop,
            ResponseId = response["id"]?.GetValue<string>(),
            Model = response["model"]?.GetValue<string>(),
            Usage = response["usage"] is not JsonObject usage ? null
                : new() { InputTokenCount = usage["input_tokens"]?.GetValue<long>(), OutputTokenCount = usage["output_tokens"]?.GetValue<long>() }
        };
    }

    private static AiHttpTextResult ReadGemini(JsonObject response)
    {
        var blockReason = response["promptFeedback"]?["blockReason"]?.GetValue<string>();
        var candidates = response["candidates"] as JsonArray;
        if (candidates is { Count: > 1 })
            throw new AiProtocolException("AI_PROVIDER_PROTOCOL_ERROR", "The Gemini service returned multiple candidates for a single-response text request.");
        var candidate = candidates is { Count: 1 } ? candidates[0] : null;
        if (candidate is null && string.IsNullOrEmpty(blockReason))
            throw new AiProtocolException("AI_PROVIDER_PROTOCOL_ERROR", "The Gemini response has neither a candidate nor a prompt block reason.");
        var text = new StringBuilder();
        if (candidate?["content"]?["parts"] is JsonArray parts)
        {
            foreach (var part in parts)
            {
                if (part?["functionCall"] is not null)
                    throw UnexpectedTool();
                if (part?["thought"]?.GetValue<bool>() != true && part?["text"] is { } content)
                    text.Append(content.GetValue<string>());
            }
        }

        var finish = candidate?["finishReason"]?.GetValue<string>();
        var refused = !string.IsNullOrEmpty(blockReason) || finish is "SAFETY" or "RECITATION" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII";
        return new AiHttpTextResult
        {
            Status = refused ? "refused" : text.Length > 0 && finish == "STOP" ? "completed" : "incomplete",
            Text = text.ToString(),
            Refusal = refused ? blockReason ?? finish : null,
            FinishReason = blockReason ?? finish,
            ResponseId = response["responseId"]?.GetValue<string>(),
            Model = response["modelVersion"]?.GetValue<string>(),
            Usage = response["usageMetadata"] is not JsonObject usage ? null
                : new()
                {
                    InputTokenCount = usage["promptTokenCount"]?.GetValue<long>(),
                    OutputTokenCount = usage["candidatesTokenCount"]?.GetValue<long>(),
                    TotalTokenCount = usage["totalTokenCount"]?.GetValue<long>()
                }
        };
    }

    private static AiProtocolException UnexpectedTool()
        => new("AI_UNEXPECTED_TOOL_CALL", "The service requested an unoffered tool. No tool was executed by this single-text request.");
}