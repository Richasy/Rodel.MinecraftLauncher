// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;

namespace Rodel.MinecraftLauncher.Assistant;

internal static class AiLocalInferenceProvider
{
    /// <summary>Lists SDK model references under an engine lease without loading model weights.</summary>
    public static async Task<IReadOnlyList<string>> ListModelsAsync(AiProviderContext snapshot, CancellationToken ct)
    {
        var runtime = snapshot.LocalInference ?? throw new AiProtocolException("AI_LOCAL_INFERENCE_UNAVAILABLE", "The owning Core did not supply its local inference runtime.");
        var lease = await runtime.AcquireAsync(ct).ConfigureAwait(false);
        await using var lifetime = lease.ConfigureAwait(false);
        return await lease.Engine.GetModelsAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Executes a bounded, instrumented SDK text probe without offering tools or retaining a conversation.</summary>
    public static async Task<AiTextResult> GenerateAsync(AiProviderContext snapshot, string prompt, CancellationToken ct)
    {
        using var client = AiChatClients.Create(snapshot, null);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, prompt)], cancellationToken: ct).ConfigureAwait(false);
        if (response.Messages.SelectMany(message => message.Contents).Any(content => content is FunctionCallContent))
            throw new AiProtocolException("AI_UNEXPECTED_TOOL_CALL", "The service requested a tool even though this single-text request offered none. No tool was executed.");
        return new()
        {
            Status = response.FinishReason == ChatFinishReason.Stop ? "completed" : "incomplete",
            FinishReason = response.FinishReason?.ToString(),
            Text = response.Text,
            ResponseId = response.ResponseId,
            Model = response.ModelId,
            Usage = response.Usage
        };
    }
}