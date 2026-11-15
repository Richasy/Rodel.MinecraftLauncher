// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;

namespace Rodel.MinecraftLauncher.Assistant;

internal static class AiModelStream
{
    /// <summary>Observes a real standard provider stream and lets MEAI aggregate its messages and accounting.</summary>
    public static async Task<ChatResponse> RunAsync(AiProviderContext snapshot, Uri? endpoint, IReadOnlyList<ChatMessage> history,
        IReadOnlyList<AIFunctionDeclaration> tools, int? maximumOutput, Func<string, string, Task> delta,
        Func<AiReasoningObservation, Task> observeReasoning, CancellationToken ct)
    {
        using var client = AiChatClients.Create(snapshot, endpoint);
        var response = await ObserveAsync(client.GetStreamingResponseAsync(history, Options(snapshot, tools, maximumOutput), ct),
            delta, observeReasoning, ct).ToChatResponseAsync(ct).ConfigureAwait(false);
        return AiChatClients.Complete(snapshot, response);
    }

    /// <summary>Uses standard typed response parsing with a caller-supplied, already-budgeted schema prompt and source-generated metadata.</summary>
    public static async Task<ChatResponse<T>> RunStructuredAsync<T>(AiProviderContext snapshot, Uri? endpoint,
        IReadOnlyList<ChatMessage> history, int? maximumOutput, JsonTypeInfo<T> resultType, CancellationToken ct)
    {
        using var client = AiChatClients.Create(snapshot, endpoint);
        var options = Options(snapshot, [], maximumOutput);
        options.ResponseFormat = ChatResponseFormat.Json;
        var response = await client.GetResponseAsync(history, options, ct).ConfigureAwait(false);
        var typed = new ChatResponse<T>(response, resultType.Options);
        var text = response.Messages.LastOrDefault()?.Text.Trim();
        if (text?.StartsWith("```json", StringComparison.Ordinal) == true && text.EndsWith("```", StringComparison.Ordinal))
        {
            // MEAI parses JSON, not Markdown. Keep the original response and normalize only the parsing view.
            typed.Messages = [.. response.Messages];
            var parsing = response.Messages[^1].Clone();
            parsing.Contents = [new TextContent(text[7..^3].Trim()), .. parsing.Contents.Where(content => content is not TextContent)];
            typed.Messages[^1] = parsing;
            typed.RawRepresentation = response;
        }

        return typed;
    }

    private static ChatOptions Options(AiProviderContext snapshot, IReadOnlyList<AIFunctionDeclaration> tools, int? maximumOutput)
        => new() { Tools = [.. tools], MaxOutputTokens = maximumOutput ?? snapshot.MaxOutputTokens };

    private static async IAsyncEnumerable<ChatResponseUpdate> ObserveAsync(IAsyncEnumerable<ChatResponseUpdate> updates,
        Func<string, string, Task> delta, Func<AiReasoningObservation, Task> observeReasoning,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var update in updates.WithCancellation(ct).ConfigureAwait(false))
        {
            if (AiChatMessages.ReasoningObservation(update) is { } observation)
                await observeReasoning(observation).ConfigureAwait(false);
            foreach (var content in update.Contents)
            {
                if (content is TextContent { Text: { Length: > 0 } text })
                    await delta("text_delta", text).ConfigureAwait(false);
                else if (content is TextReasoningContent { Text: { Length: > 0 } reasoning })
                    await delta("reasoning_delta", reasoning).ConfigureAwait(false);
            }

            yield return update;
        }
    }
}