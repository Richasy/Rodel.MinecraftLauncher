// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using Rodel.Inference;

namespace Rodel.MinecraftLauncher.Assistant;

internal sealed class AiLocalInferenceRuntime(
    Func<InferenceEngineOptions, CancellationToken, Task<IAiLocalInferenceEngine>>? create = null) : IAsyncDisposable
{
    /// <summary>Gets whether this build includes the embedded inference implementation.</summary>
    public static bool IsSupported => true;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<InferenceEngineOptions, CancellationToken, Task<IAiLocalInferenceEngine>> _create = create ?? CreateSdkEngineAsync;
    private IAiLocalInferenceEngine? _engine;
    private long _created;

    /// <summary>Leases a cached engine; native resources cannot be unloaded before the request releases its lease.</summary>
    public async Task<Lease> AcquireAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_engine is null)
            {
                var engine = await _create(new InferenceEngineOptions
                {
                    Mode = InferenceMode.Embedded
                }, ct).ConfigureAwait(false);
                if (engine.Mode != InferenceMode.Embedded || engine.Endpoint is not null)
                {
                    await engine.DisposeAsync().ConfigureAwait(false);
                    throw new AiProtocolException("AI_LOCAL_INFERENCE_MODE_INVALID", "Local inference must be launcher-owned Embedded mode, never a daemon connection.");
                }

                _engine = engine;
                _created++;
            }

            return new Lease(_engine, _gate);
        }
        catch
        {
            _gate.Release();
            throw;
        }
    }

    /// <summary>Observes local ownership and initialization without starting an engine.</summary>
    public async Task<AiLocalInferenceStatus> StatusAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return new()
            {
                Initialized = _engine is not null,
                EngineInstancesCreated = _created,
                Explanation = _engine?.Explanation
            };
        }
        finally { _gate.Release(); }
    }

    /// <summary>Waits for borrowed requests, releases only this engine, and permits a later fresh load.</summary>
    public async Task UnloadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var engine = _engine;
            _engine = null;
            if (engine is not null) await engine.DisposeAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await UnloadAsync(CancellationToken.None).ConfigureAwait(false);
        _gate.Dispose();
    }

    private static async Task<IAiLocalInferenceEngine> CreateSdkEngineAsync(InferenceEngineOptions options, CancellationToken ct)
        => new SdkEngine(await InferenceEngine.CreateAsync(options, ct).ConfigureAwait(false));

    private sealed class SdkEngine(InferenceEngine engine) : IAiLocalInferenceEngine
    {
        /// <inheritdoc />
        public InferenceMode Mode => engine.Mode;
        /// <inheritdoc />
        public Uri? Endpoint => engine.Endpoint;
        /// <inheritdoc />
        public string Explanation => engine.Resolution.Explanation;
        /// <inheritdoc />
        public Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken ct) => engine.GetModelsAsync(ct);
        /// <inheritdoc />
        public Task<IChatClient> GetChatClientAsync(string model, CancellationToken ct, int? contextLength = null)
            => contextLength is { } length
                ? engine.GetChatClientWithOptionsAsync(model, new ModelLoadOptions { ContextLength = length }, ct)
                : engine.GetChatClientAsync(model, ct);
        /// <inheritdoc />
        public ValueTask DisposeAsync() => engine.DisposeAsync();
    }

    internal sealed class Lease(IAiLocalInferenceEngine engine, SemaphoreSlim gate) : IAsyncDisposable
    {
        private int _disposed;
        /// <summary>Gets the borrowed engine; releasing this lease does not dispose the cached model resource.</summary>
        public IAiLocalInferenceEngine Engine { get; } = engine;
        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}