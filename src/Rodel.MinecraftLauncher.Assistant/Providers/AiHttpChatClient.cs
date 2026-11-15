// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Assistant.AiChatMessages;

namespace Rodel.MinecraftLauncher.Assistant;

internal sealed class AiHttpChatClient(AiProviderContext connection, Uri endpoint) : IChatClient
{
    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => AiChatMessages.FinalizeResponse(await GetStreamingResponseAsync(messages, options, cancellationToken)
            .ToChatResponseAsync(cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var source = ReadAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            ChatResponseUpdate update;
            try
            {
                if (!await source.MoveNextAsync().ConfigureAwait(false)) break;
                update = source.Current;
            }
            catch (AiProtocolException error)
            {
                var details = error.Details?.DeepClone();
                if (details is not null) SecretScrubber.Scrub(details, connection.Credential);
                throw new AiProtocolException(error.Code, SecretScrubber.Excerpt(error.Message, connection.Credential, 2048), details);
            }

            yield return update;
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is not null ? null : serviceType == typeof(ChatClientMetadata)
            ? new ChatClientMetadata(connection.Protocol, new UriBuilder(endpoint.Scheme, endpoint.Host, endpoint.Port).Uri, AiChatClients.DiagnosticModel(connection))
            : serviceType.IsInstanceOfType(this) ? this : null;

    /// <inheritdoc />
    public void Dispose() { }

    private async IAsyncEnumerable<ChatResponseUpdate> ReadAsync(IEnumerable<ChatMessage> messages, ChatOptions? options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var snapshot = AiChatClients.ApplyOptions(connection, options);
        var history = messages.ToArray();
        var tools = (options?.Tools ?? []).Select(tool => tool as AIFunctionDeclaration
            ?? throw new NotSupportedException("Only declared functions are supported by this provider.")).ToArray();
        var maximumOutput = options?.MaxOutputTokens ?? snapshot.MaxOutputTokens;
        var protocol = snapshot.Protocol;
        using var http = AiHttpTransport.CreateClient(endpoint, snapshot.Credential, protocol);
        var body = AiChatWire.Build(snapshot, history, tools, maximumOutput);
        var route = protocol switch
        {
            AiProviderProtocols.OpenAiResponses => "responses",
            AiProviderProtocols.OpenAiChat => "chat/completions",
            AiProviderProtocols.AnthropicMessages => "messages",
            _ => TextProviderProtocols.GeminiRoute(snapshot.Model, "streamGenerateContent")
        };
        var target = AiHttpTransport.Route(endpoint, route);
        if (protocol == AiProviderProtocols.Gemini)
        {
            var builder = new UriBuilder(target)
            {
                Query = target.Query.Length > 0 ? target.Query.TrimStart('?') + "&alt=sse" : "alt=sse"
            };
            target = builder.Uri;
        }

        using var response = await AiHttpTransport.OpenStreamAsync(http, target, body, snapshot.Credential, ct).ConfigureAwait(false);
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var text = new StringBuilder();
        var reasoning = new StringBuilder();
        var calls = new List<FunctionCallContent>();
        var chatCalls = new SortedDictionary<int, ToolCallFragments>();
        var blocks = new SortedDictionary<int, JsonObject>();
        var inputs = new Dictionary<int, StringBuilder>();
        var geminiParts = new JsonArray();
        JsonArray? responseOutputs = null;
        UsageDetails? usage = null;
        string? finish = null;
        var ended = false;
        var status = "incomplete";
        var reasoningObserved = false;
        ChatResponseUpdate ObserveReasoning(AiReasoningObservation evidence)
        {
            reasoningObserved = true;
            return AiChatMessages.ReasoningUpdate(evidence);
        }

        ChatResponseUpdate? Emit(string kind, string? value)
        {
            if (string.IsNullOrEmpty(value))
                return null;
            var destination = kind == "text_delta" ? text : reasoning;
            destination.Append(value);
            if (text.Length + reasoning.Length > 256000)
                throw new AiProtocolException("AI_RESPONSE_TOO_LARGE", "This model response exceeded its visible-content budget.");
            return new()
            {
                MessageId = "assistant",
                Role = ChatRole.Assistant,
                Contents = kind == "text_delta" ? [new TextContent(value)] : [new TextReasoningContent(value)]
            };
        }

        await foreach (var frame in AiSse.ReadAsync(stream, ct).ConfigureAwait(false))
        {
            if (frame["error"] is JsonObject streamError) throw StreamError(streamError, snapshot.Credential);
            if (protocol == AiProviderProtocols.OpenAiChat)
            {
                if (frame["type"]?.ToString() == "$done") { ended = true; break; }

                usage = NormalizeUsage(frame["usage"] as JsonObject, "prompt_tokens", "completion_tokens", "total_tokens") ?? usage;
                if (frame["choices"] is not JsonArray choices || choices.Count == 0) continue;
                if (choices.Count != 1 || choices[0]?["index"]?.GetValue<int>() is not (null or 0))
                    throw Protocol("The chat stream returned extra choices.");
                var choice = choices[0]!;
                var change = choice["delta"] as JsonObject;
                finish = choice["finish_reason"]?.GetValue<string>() ?? finish;
                if (Emit("text_delta", change?["content"]?.GetValue<string>()) is { } textDelta) yield return textDelta;
                if (Emit("reasoning_delta", change?["reasoning_content"]?.GetValue<string>()) is { } reasoningDelta) yield return reasoningDelta;
                if (change?["refusal"] is { } refusal)
                {
                    status = "refused";
                    if (Emit("text_delta", refusal.GetValue<string>()) is { } refusalDelta) yield return refusalDelta;
                }

                if (change?["tool_calls"] is JsonArray fragments)
                {
                    foreach (var part in fragments)
                    {
                        var index = part!["index"]!.GetValue<int>();
                        if (!chatCalls.TryGetValue(index, out var entry))
                            chatCalls[index] = entry = new();
                        if (part["id"] is { } id) entry.Id = id.GetValue<string>();
                        if (part["function"]?["name"] is { } name) entry.Name = name.GetValue<string>();
                        entry.Arguments.Append(part["function"]?["arguments"]?.GetValue<string>());
                        if (entry.Arguments.Length > 64000) throw Protocol("Tool input exceeded its assembly budget.");
                    }
                }
            }
            else if (protocol == AiProviderProtocols.OpenAiResponses)
            {
                var type = frame["type"]?.ToString();
                if (type is "response.output_item.added" or "response.output_item.done" && frame["item"]?["type"]?.ToString() == "reasoning")
                {
                    yield return ObserveReasoning(new("provider_reasoning_item", type == "response.output_item.added" ? "started" : "completed")
                    {
                        ProviderItemId = frame["item"]?["id"]?.GetValue<string>(),
                        OpaqueStatePresent = frame["item"]?["encrypted_content"] is not null
                    });
                }
                else if (type == "response.output_text.delta")
                {
                    if (Emit("text_delta", frame["delta"]?.GetValue<string>()) is { } outputDelta) yield return outputDelta;
                }
                else if (type == "response.refusal.delta")
                {
                    if (Emit("text_delta", frame["delta"]?.GetValue<string>()) is { } refusalDelta) yield return refusalDelta;
                }
                else if (type is "response.reasoning_summary_text.delta" or "response.reasoning_text.delta")
                {
                    if (Emit("reasoning_delta", frame["delta"]?.GetValue<string>()) is { } reasoningDelta) yield return reasoningDelta;
                }
                else if (type is "response.completed" or "response.incomplete")
                {
                    var result = frame["response"]!.AsObject();
                    responseOutputs = result["output"]?.DeepClone().AsArray() ?? throw Protocol("The completed Responses event omitted its output.");
                    if (!reasoningObserved && responseOutputs.Any(item => item?["type"]?.ToString() == "reasoning"))
                        yield return ObserveReasoning(new("provider_reasoning_item", "completed"));
                    if (reasoning.Length == 0)
                    {
                        foreach (var output in responseOutputs)
                        {
                            if (output?["type"]?.ToString() != "reasoning") continue;
                            var content = output["content"] as JsonArray;
                            var parts = content?.Any(part => part?["type"]?.ToString() == "reasoning_text"
                                && !string.IsNullOrEmpty(part["text"]?.GetValue<string>())) == true
                                ? content : output["summary"] as JsonArray;
                            foreach (var part in parts ?? [])
                            {
                                if (part?["type"]?.ToString() is "reasoning_text" or "summary_text"
                                    && Emit("reasoning_delta", part["text"]?.GetValue<string>()) is { } fallbackReasoning)
                                {
                                    yield return fallbackReasoning;
                                }
                            }
                        }
                    }

                    if (text.Length == 0)
                    {
                        foreach (var output in responseOutputs)
                        {
                            if (output?["type"]?.ToString() == "message" && output["content"] is JsonArray contentBlocks)
                            {
                                foreach (var block in contentBlocks)
                                {
                                    if (block?["type"]?.ToString() == "output_text" && Emit("text_delta", block["text"]?.GetValue<string>()) is { } fallbackText) yield return fallbackText;
                                    else if (block?["type"]?.ToString() == "refusal" && Emit("text_delta", block["refusal"]?.GetValue<string>()) is { } fallbackRefusal) yield return fallbackRefusal;
                                }
                            }
                        }
                    }

                    usage = NormalizeUsage(result["usage"] as JsonObject, "input_tokens", "output_tokens", "total_tokens");
                    finish = result["incomplete_details"]?["reason"]?.ToString() ?? result["status"]?.ToString();
                    status = type == "response.completed" ? "completed" : "incomplete";
                    ended = true;
                    break;
                }
                else if (type is "response.failed" or "response.error" or "error")
                {
                    throw StreamError(frame["response"]?["error"] as JsonObject ?? frame, snapshot.Credential);
                }
            }
            else if (protocol == AiProviderProtocols.AnthropicMessages)
            {
                var type = frame["type"]?.ToString();
                if (type == "message_start")
                {
                    usage = NormalizeUsage(frame["message"]?["usage"] as JsonObject, "input_tokens", "output_tokens", null);
                }
                else if (type == "content_block_start")
                {
                    var index = frame["index"]!.GetValue<int>();
                    blocks[index] = frame["content_block"]!.DeepClone().AsObject();
                    if (blocks[index]["type"]?.ToString() is "thinking" or "redacted_thinking")
                        yield return ObserveReasoning(new("provider_thinking_block", "started") { Redacted = blocks[index]["type"]?.ToString() == "redacted_thinking" });
                    if (blocks[index]["type"]?.ToString() == "server_tool_use")
                        throw Protocol("Provider-hosted tools were not offered.");
                    if (Emit("text_delta", blocks[index]["text"]?.GetValue<string>()) is { } initialText) yield return initialText;
                    if (Emit("reasoning_delta", blocks[index]["thinking"]?.GetValue<string>()) is { } initialReasoning) yield return initialReasoning;
                }
                else if (type == "content_block_delta")
                {
                    var index = frame["index"]!.GetValue<int>();
                    if (!blocks.TryGetValue(index, out var block)) throw Protocol("A content delta has no matching block.");
                    var change = frame["delta"]!.AsObject();
                    var deltaType = change["type"]?.ToString();
                    if (deltaType is "text_delta" or "thinking_delta" or "signature_delta")
                    {
                        var field = deltaType == "text_delta" ? "text" : deltaType == "thinking_delta" ? "thinking" : "signature";
                        var value = change[field]?.GetValue<string>() ?? string.Empty;
                        block[field] = (block[field]?.GetValue<string>() ?? string.Empty) + value;
                        if (deltaType != "signature_delta")
                            if (Emit(deltaType == "text_delta" ? "text_delta" : "reasoning_delta", value) is { } blockDelta) yield return blockDelta;
                    }
                    else if (deltaType == "input_json_delta")
                    {
                        if (!inputs.TryGetValue(index, out var input)) inputs[index] = input = new StringBuilder();
                        input.Append(change["partial_json"]?.GetValue<string>());
                        if (input.Length > 64000) throw Protocol("Tool input exceeded its assembly budget.");
                    }
                }
                else if (type == "message_delta")
                {
                    finish = frame["delta"]?["stop_reason"]?.ToString() ?? finish;
                    var nextUsage = NormalizeUsage(frame["usage"] as JsonObject, "input_tokens", "output_tokens", null);
                    if (nextUsage is { } next)
                    {
                        usage = new()
                        {
                            InputTokenCount = next.InputTokenCount ?? usage?.InputTokenCount,
                            OutputTokenCount = next.OutputTokenCount ?? usage?.OutputTokenCount,
                            TotalTokenCount = next.TotalTokenCount ?? usage?.TotalTokenCount,
                            ReasoningTokenCount = next.ReasoningTokenCount ?? usage?.ReasoningTokenCount
                        };
                    }
                }
                else if (type == "message_stop") { ended = true; break; }
            }
            else
            {
                usage = NormalizeUsage(frame["usageMetadata"] as JsonObject, "promptTokenCount", "candidatesTokenCount", "totalTokenCount") ?? usage;
                if (frame["promptFeedback"]?["blockReason"] is { } blockReason) { finish = blockReason.ToString(); status = "refused"; }

                if (frame["candidates"] is not JsonArray { Count: > 0 } candidates) continue;
                if (candidates.Count != 1) throw Protocol("The Gemini stream returned extra candidates.");
                var candidate = candidates[0]!;
                finish = candidate["finishReason"]?.ToString() ?? finish;
                if (candidate["content"]?["parts"] is JsonArray parts)
                {
                    foreach (var part in parts)
                    {
                        geminiParts.Add(part!.DeepClone());
                        if (part["thought"]?.GetValue<bool>() == true && part["text"] is null)
                            yield return ObserveReasoning(new("provider_thought_part", "observed"));
                        if (part["text"] is { } content)
                            if (Emit(part["thought"]?.GetValue<bool>() == true ? "reasoning_delta" : "text_delta", content.GetValue<string>()) is { } partDelta) yield return partDelta;
                        if (part["functionCall"] is JsonObject function)
                        {
                            var call = Call(function["id"]?.ToString() ?? Guid.NewGuid().ToString("N"), function["name"]?.ToString(), function["args"]?.DeepClone() ?? new JsonObject());
                            SetProviderCallId(call, function["id"]?.GetValue<string>());
                            calls.Add(call);
                        }
                    }
                }

                ended = finish is not null;
            }
        }

        if (!ended)
            throw new AiProtocolException("AI_RESPONSE_INTERRUPTED", "The model stream ended before its protocol terminal marker. Partial content is retained; no partial tool input was executed.");
        if (!reasoningObserved && usage?.ReasoningTokenCount is > 0)
            yield return ObserveReasoning(new("reported_usage", "completed") { ReasoningTokens = usage.ReasoningTokenCount });
        JsonNode payload;
        if (protocol == AiProviderProtocols.OpenAiChat)
        {
            foreach (var entry in chatCalls.Values)
                calls.Add(Call(entry.Id, entry.Name, ParseArguments(entry.Arguments.ToString())));
            payload = new JsonObject { ["role"] = "assistant", ["content"] = text.Length == 0 ? null : text.ToString() };
            if (reasoning.Length > 0) payload["reasoning_content"] = reasoning.ToString();
            if (calls.Count > 0)
            {
                payload["tool_calls"] = new JsonArray(calls.Select(call => (JsonNode)new JsonObject
                {
                    ["id"] = call.CallId,
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = Arguments(call).ToJsonString() }
                }).ToArray());
            }

            if (status != "refused") status = finish == "stop" || (finish == "tool_calls" && calls.Count > 0) ? "completed" : "incomplete";
        }
        else if (protocol == AiProviderProtocols.OpenAiResponses)
        {
            payload = responseOutputs!;
            foreach (var item in responseOutputs!)
            {
                if (item?["type"]?.ToString() == "function_call")
                    calls.Add(Call(item["call_id"]?.ToString(), item["name"]?.ToString(), ParseArguments(item["arguments"]!.GetValue<string>())));
                if (item?["content"] is JsonArray content && content.Any(block => block?["type"]?.ToString() == "refusal"))
                    status = "refused";
            }
        }
        else if (protocol == AiProviderProtocols.AnthropicMessages)
        {
            foreach (var (index, block) in blocks)
            {
                if (block["type"]?.ToString() == "tool_use")
                {
                    if (inputs.TryGetValue(index, out var input)) block["input"] = ParseArguments(input.ToString());
                    calls.Add(Call(block["id"]?.ToString(), block["name"]?.ToString(), block["input"]?.DeepClone() ?? new JsonObject()));
                }
            }

            payload = new JsonArray(blocks.Values.Select(block => (JsonNode)block).ToArray());
            status = finish == "refusal" ? "refused" : finish is "end_turn" or "stop_sequence" or "tool_use" ? "completed" : "incomplete";
        }
        else
        {
            payload = geminiParts;
            status = status == "refused" || finish is "SAFETY" or "RECITATION" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII" ? "refused" : finish == "STOP" ? "completed" : "incomplete";
        }

        if (text.Length == 0 && calls.Count == 0 && status == "completed") status = "incomplete";
        var finalMessage = new ChatResponseUpdate
        {
            MessageId = "assistant",
            Role = ChatRole.Assistant,
            Contents = [.. calls]
        };
        AiChatMessages.SetPendingNative(finalMessage, new(snapshot.Protocol, snapshot.ModelId, snapshot.ServiceRevision, payload));
        yield return finalMessage;
        var completion = Complete(new ChatResponse(), status, finish);
        if (status == "completed" && calls.Count > 0) completion.FinishReason = ChatFinishReason.ToolCalls;
        yield return new()
        {
            FinishReason = completion.FinishReason,
            AdditionalProperties = completion.AdditionalProperties,
            ModelId = snapshot.Model,
            Contents = usage is null ? [] : [new UsageContent(usage)]
        };
    }

    private static JsonObject ParseArguments(string text) => JsonNode.Parse(text) as JsonObject ?? throw Protocol("Tool arguments must be a complete JSON object.");
    private static AiProtocolException Protocol(string message) => new("AI_PROVIDER_PROTOCOL_ERROR", message);
    private static UsageDetails? NormalizeUsage(JsonObject? item, string input, string output, string? total) => item is null ? null
        : new()
        {
            InputTokenCount = item[input] is null ? null : item[input]!.GetValue<long>()
                + (item["cache_read_input_tokens"]?.GetValue<long>() ?? 0) + (item["cache_creation_input_tokens"]?.GetValue<long>() ?? 0),
            CachedInputTokenCount = item["cache_read_input_tokens"]?.GetValue<long>(),
            OutputTokenCount = item[output]?.GetValue<long>(),
            TotalTokenCount = total is null ? null : item[total]?.GetValue<long>(),
            ReasoningTokenCount = (item["output_tokens_details"]?["reasoning_tokens"]
                ?? item["completion_tokens_details"]?["reasoning_tokens"] ?? item["thoughtsTokenCount"])?.GetValue<long>()
        };

    private sealed class ToolCallFragments
    {
        /// <summary>Gets or sets the provider invocation identity while fragments arrive.</summary>
        public string? Id { get; set; }
        /// <summary>Gets or sets the provider function name while fragments arrive.</summary>
        public string? Name { get; set; }
        /// <summary>Gets the bounded argument text awaiting complete JSON validation.</summary>
        public StringBuilder Arguments { get; } = new();
    }
    private static AiProtocolException StreamError(JsonObject error, string? secret)
    {
        var code = error["status"]?.ToString() ?? error["code"]?.ToString() ?? error["type"]?.ToString();
        var message = error["message"]?.ToString() ?? "The provider stream reported a failed model call.";
        return new(AiHttpTransport.IsContextLimit(code, message) ? "AI_CONTEXT_LIMIT" : "AI_PROVIDER_ERROR",
            SecretScrubber.Excerpt(message, secret), new JsonObject { ["providerCode"] = SecretScrubber.Excerpt(code ?? string.Empty, secret), ["inStream"] = true });
    }

}