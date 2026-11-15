// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant;

internal static class AiProviderRequests
{
    /// <summary>Lists models or executes an isolated text probe, enforcing its deadline and sanitizing provider failures.</summary>
    public static async Task<AiProviderResult> ExecuteAsync(AiProviderContext snapshot, Uri? endpoint, string? prompt, int timeoutSeconds, CancellationToken ct)
    {
        var secret = snapshot.Credential;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var timer = Stopwatch.StartNew();
        try
        {
            if (!AiProviderProtocols.IsKnown(snapshot.Protocol))
                throw new AiProtocolException("AI_PROTOCOL_NOT_IMPLEMENTED", "This service protocol is not implemented; no other protocol or service was substituted.");
            using var http = snapshot.Protocol == AiProviderProtocols.RodelInference ? null
                : AiHttpTransport.CreateClient(endpoint ?? throw new ArgumentException("An HTTP provider requires its endpoint."), secret, snapshot.Protocol);
            AiProviderResult result;
            if (snapshot.Protocol == AiProviderProtocols.RodelInference)
            {
                result = prompt is null
                    ? ModelList(await AiLocalInferenceProvider.ListModelsAsync(snapshot, deadline.Token).ConfigureAwait(false))
                    : await AiLocalInferenceProvider.GenerateAsync(snapshot, prompt, deadline.Token).ConfigureAwait(false);
            }
            else
            {
                ArgumentNullException.ThrowIfNull(endpoint);
                ArgumentNullException.ThrowIfNull(http);
                if (snapshot.Protocol is AiProviderProtocols.AnthropicMessages or AiProviderProtocols.Gemini)
                {
                    result = prompt is null
                        ? await TextProviderProtocols.ListModelsAsync(http, endpoint, snapshot.Protocol, secret, deadline.Token).ConfigureAwait(false)
                        : await TextProviderProtocols.GenerateAsync(http, endpoint, snapshot, prompt, deadline.Token).ConfigureAwait(false);
                }
                else if (prompt is null)
                {
                    var (Body, RequestId) = await AiHttpTransport.SendAsync(http, AiHttpTransport.Route(endpoint, "models"), null, secret, deadline.Token).ConfigureAwait(false);
                    if (Body["data"] is not JsonArray models)
                        throw new AiProtocolException("AI_PROVIDER_PROTOCOL_ERROR", "The model listing has no data array. Manually configured models remain unchanged.");
                    var ids = models.Select(model => model?["id"]?.GetValue<string>() ?? throw new AiProtocolException("AI_PROVIDER_PROTOCOL_ERROR", "The model listing contains an item without a model identifier."));
                    result = new AiHttpModelCatalogResult(ModelList(ids).Models, RequestId);
                }
                else
                {
                    var responses = snapshot.Protocol == AiProviderProtocols.OpenAiResponses;
                    var model = snapshot;
                    AiProviderRequest body = responses ? new AiOpenAiResponsesRequest
                    {
                        Model = model.Model,
                        Input = JsonValue.Create(prompt)!,
                        Temperature = model.Temperature,
                        MaxOutputTokens = model.MaxOutputTokens
                    }
                    : new AiOpenAiChatRequest
                    {
                        Model = model.Model,
                        Messages = new(new JsonObject { ["role"] = "user", ["content"] = prompt }),
                        N = 1,
                        Temperature = model.Temperature,
                        MaxCompletionTokens = model.MaxOutputTokens
                    };
                    var (Body, RequestId) = await AiHttpTransport.SendAsync(http, AiHttpTransport.Route(endpoint, responses ? "responses" : "chat/completions"), body, secret, deadline.Token).ConfigureAwait(false);
                    result = (responses ? ReadResponses(Body) : ReadChat(Body)) with { RequestId = RequestId };
                }
            }

            string? Clean(string? value) => value is null ? null : SecretScrubber.Scrub(value, secret);
            // Scrub only provider-owned text, never normalized status or host-owned flags.
            result = result switch
            {
                AiHttpTextResult text => text with
                {
                    Text = Clean(text.Text)!,
                    Refusal = Clean(text.Refusal),
                    FinishReason = Clean(text.FinishReason),
                    ResponseId = Clean(text.ResponseId),
                    Model = Clean(text.Model),
                    RequestId = Clean(text.RequestId)
                },
                AiTextResult text => text with
                {
                    Text = Clean(text.Text)!,
                    FinishReason = Clean(text.FinishReason),
                    ResponseId = Clean(text.ResponseId),
                    Model = Clean(text.Model)
                },
                AiModelCatalogResult catalog => catalog with
                {
                    Models = [.. catalog.Models.Select(model => new AiProviderModel(Clean(model.Id)!, Clean(model.DisplayName)!))]
                },
                _ => throw new InvalidOperationException("Unsupported provider result.")
            };
            return result with { ElapsedMilliseconds = timer.ElapsedMilliseconds };
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (Exception) when (deadline.IsCancellationRequested)
        {
            throw new AiProtocolException("AI_SERVICE_TIMEOUT", "The model request exceeded its configured deadline. No service, protocol or model was substituted.");
        }
        catch (AiProtocolException error)
        {
            var details = error.Details?.DeepClone();
            if (details is not null)
                SecretScrubber.Scrub(details, secret);
            throw new AiProtocolException(error.Code, SecretScrubber.Excerpt(error.Message, secret, 2048), details);
        }
#if RODEL_LOCAL_INFERENCE
        catch (Rodel.Inference.Core.ModelLoadException error)
        {
            throw AiChatClients.SafeError(error, snapshot);
        }
#endif
        catch (HttpRequestException error)
        {
            throw new AiProtocolException("AI_SERVICE_UNREACHABLE", "The model service could not be reached or its connection failed.", new JsonObject { ["networkReason"] = error.HttpRequestError.ToString(), ["httpStatus"] = error.StatusCode is { } status ? (int)status : null });
        }
        catch (IOException)
        {
            throw new AiProtocolException("AI_RESPONSE_INTERRUPTED", "The model response ended during transfer. The request was not replayed.");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            throw new AiProtocolException(snapshot.Protocol == AiProviderProtocols.RodelInference ? "AI_INFERENCE_REQUEST_FAILED" : "AI_PROVIDER_PROTOCOL_ERROR",
                snapshot.Protocol == AiProviderProtocols.RodelInference ? SecretScrubber.Excerpt(error.Message, secret, 1024) : "The model service returned an invalid or unsupported response shape.");
        }
    }

    private static AiModelCatalogResult ModelList(IEnumerable<string> ids)
        => new([.. ids.Distinct(StringComparer.Ordinal).Select(id => new AiProviderModel(id, id))]);

    // Follows Rodel.Agent.Core's Chat response/refusal/usage mapping, without fabricating response IDs.
    private static AiHttpTextResult ReadChat(JsonObject response)
    {
        if (response["choices"] is not JsonArray { Count: 1 } choices || choices[0]?["message"] is not JsonObject message)
            throw new AiProtocolException("AI_PROVIDER_PROTOCOL_ERROR", "The service returned no single assistant choice for this n=1 request.");
        if (message["tool_calls"] is JsonArray { Count: > 0 } || message["function_call"] is not null)
            throw new AiProtocolException("AI_UNEXPECTED_TOOL_CALL", "The service requested an unoffered tool. No tool was executed.");
        var finish = choices[0]?["finish_reason"]?.GetValue<string>();
        var refusal = message["refusal"]?.GetValue<string>();
        var text = message["content"] switch
        {
            JsonValue value => value.GetValue<string>(),
            JsonArray blocks => string.Concat(blocks.Where(block => block?["type"]?.ToString() == "text").Select(block => block?["text"]?.GetValue<string>())),
            null => string.Empty,
            _ => throw new AiProtocolException("AI_PROVIDER_PROTOCOL_ERROR", "The assistant content is not text.")
        };
        return new AiHttpTextResult
        {
            Status = !string.IsNullOrEmpty(refusal) || finish == "content_filter" ? "refused" : finish == "stop" ? "completed" : "incomplete",
            Text = text,
            Refusal = refusal,
            FinishReason = finish,
            ResponseId = response["id"]?.GetValue<string>(),
            Model = response["model"]?.GetValue<string>(),
            Usage = Usage(response["usage"] as JsonObject, "prompt_tokens", "completion_tokens")
        };
    }

    private static AiHttpTextResult ReadResponses(JsonObject response)
    {
        if (response["status"]?.ToString() == "failed")
            throw new AiProtocolException("AI_PROVIDER_ERROR", "The Responses service reported a failed generation.");
        if (response["output"] is not JsonArray output)
            throw new AiProtocolException("AI_PROVIDER_PROTOCOL_ERROR", "The Responses result has no output array. output_text is an SDK convenience property, not the REST response shape.");
        var text = new StringBuilder();
        var refusals = new StringBuilder();
        foreach (var item in output)
        {
            var type = item?["type"]?.ToString();
            if (type is "function_call" or "custom_tool_call")
                throw new AiProtocolException("AI_UNEXPECTED_TOOL_CALL", "The service requested an unoffered tool. No tool was executed.");
            if (type != "message" || item?["content"] is not JsonArray content)
                continue;
            foreach (var block in content)
            {
                if (block?["type"]?.ToString() == "output_text")
                    text.Append(block["text"]?.GetValue<string>());
                else if (block?["type"]?.ToString() == "refusal")
                    refusals.Append(block["refusal"]?.GetValue<string>());
            }
        }

        var status = response["status"]?.GetValue<string>();
        return new AiHttpTextResult
        {
            Status = refusals.Length > 0 ? "refused" : status == "completed" ? "completed" : "incomplete",
            Text = text.ToString(),
            Refusal = refusals.Length > 0 ? refusals.ToString() : null,
            FinishReason = response["incomplete_details"]?["reason"]?.GetValue<string>() ?? status,
            ResponseId = response["id"]?.GetValue<string>(),
            Model = response["model"]?.GetValue<string>(),
            Usage = Usage(response["usage"] as JsonObject, "input_tokens", "output_tokens")
        };
    }

    private static UsageDetails? Usage(JsonObject? usage, string input, string output) => usage is null ? null
        : new()
        {
            InputTokenCount = usage[input]?.GetValue<long>(),
            OutputTokenCount = usage[output]?.GetValue<long>(),
            TotalTokenCount = usage["total_tokens"]?.GetValue<long>()
        };
}