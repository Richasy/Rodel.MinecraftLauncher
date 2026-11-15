// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Assistant;

internal sealed class AiLocalInferenceRuntime : IAsyncDisposable
{
    private bool _disposed;

    /// <summary>Gets whether this build includes the embedded inference implementation.</summary>
    public static bool IsSupported => false;

    internal static AiProtocolException Unavailable()
        => new("AI_LOCAL_INFERENCE_UNAVAILABLE", "Local inference is temporarily unavailable in this build. Select an online model service.");

    /// <summary>Reports unavailability without initializing an engine or changing saved model bindings.</summary>
    public Task<AiLocalInferenceStatus> StatusAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new AiLocalInferenceStatus { Explanation = Unavailable().Message });
    }

    /// <summary>Honors cancellation; this build owns no local inference resources to unload.</summary>
    public Task UnloadAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal static class AiLocalInferenceProvider
{
    /// <summary>Rejects model discovery without downloading or initializing a local runtime.</summary>
    public static Task<IReadOnlyList<string>> ListModelsAsync(AiProviderContext _, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromException<IReadOnlyList<string>>(AiLocalInferenceRuntime.Unavailable());
    }

    /// <summary>Rejects local generation without substituting an online provider.</summary>
    public static Task<AiTextResult> GenerateAsync(AiProviderContext _, string _1, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromException<AiTextResult>(AiLocalInferenceRuntime.Unavailable());
    }
}

internal static class AiLocalModelMetadata
{
    /// <summary>Leaves optional saved metadata unset while the GGUF reader is excluded from this build.</summary>
    public static int? ReadContextLength(string _) => null;
}