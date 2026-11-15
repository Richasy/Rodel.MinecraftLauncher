// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using Rodel.Inference;

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>The SDK/native-engine boundary; scheduling ownership remains in the calling Core runtime.</summary>
internal interface IAiLocalInferenceEngine : IAsyncDisposable
{
    /// <summary>Gets the actual mode selected by the model operation layer.</summary>
    public InferenceMode Mode { get; }
    /// <summary>Gets the attached daemon endpoint, or null for a locally hosted engine.</summary>
    public Uri? Endpoint { get; }
    /// <summary>Gets the model layer's explanation of its ownership decision.</summary>
    public string Explanation { get; }
    /// <summary>Lists available references without downloading or loading model weights.</summary>
    public Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken ct);
    /// <summary>Creates a borrowed chat client which does not own the underlying engine.</summary>
    public Task<IChatClient> GetChatClientAsync(string model, CancellationToken ct, int? contextLength = null);
}