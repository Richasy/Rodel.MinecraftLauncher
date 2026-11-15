// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.GameBridge;

internal sealed class BridgeConnection(Stream stream, Func<BridgeRpc, CancellationToken, Task<JsonElement>>? receive = null) : IAsyncDisposable
{
    private readonly CancellationTokenSource _closed = new();
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BridgeRpc>> _pending = new();
    private Task? _reader;
    private readonly Lock _lifetime = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active;
    private bool _disposing;
    private readonly HashSet<Task> _incoming = [];
    private int _receiving;
    internal bool Connected => !_closed.IsCancellationRequested;

    internal void Start() => _reader = ReadAsync();

    private async Task ReadAsync()
    {
        try
        {
            while (!_closed.IsCancellationRequested)
            {
                var response = await FramedJsonPipe.ReadAsync(stream, BridgeWireJsonContext.Default.BridgeRpc, "GAME_BRIDGE", _closed.Token).ConfigureAwait(false);
                if (response.Jsonrpc == "2.0" && response.Method is not null && response.Id.Length is > 0 and <= 64
                    && response.Result is null && response.Error is null)
                {
                    if (Volatile.Read(ref _receiving) >= 8) throw new InvalidDataException("Too many game requests are pending.");
                    lock (_incoming)
                    {
                        _incoming.RemoveWhere(task => task.IsCompleted);
                        _incoming.Add(ReceiveAsync(response));
                    }

                    continue;
                }

                if (response.Jsonrpc != "2.0" || response.Method is not null || response.Id.Length > 64 || (response.Result is null) == (response.Error is null))
                    throw new InvalidDataException("Invalid bridge response.");
                if (_pending.TryRemove(response.Id, out var completion)) completion.TrySetResult(response);
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or JsonException or LauncherOperationException or InvalidDataException or ObjectDisposedException)
        {
            _closed.Cancel();
            foreach (var completion in _pending.Values)
                completion.TrySetException(new LauncherOperationException("GAME_BRIDGE_DISCONNECTED", "The game bridge disconnected; edits are never replayed automatically."));
            _pending.Clear();
        }
    }

    private async Task ReceiveAsync(BridgeRpc request)
    {
        var admitted = Interlocked.Increment(ref _receiving) <= 8;
        try
        {
            BridgeRpc response;
            try
            {
                if (!admitted) throw new LauncherOperationException("GAME_BRIDGE_BUSY", "Too many game requests are pending.");
                if (receive is null) throw new LauncherOperationException("GAME_BRIDGE_METHOD_UNKNOWN", "This host does not accept game requests.");
                response = new() { Id = request.Id, Result = await receive(request, _closed.Token).ConfigureAwait(false) };
            }
            catch (Exception error)
            {
                response = new() { Id = request.Id, Error = new() { Code = -32000, Message = error is LauncherOperationException failure ? failure.Code : "GAME_AI_REQUEST_FAILED" } };
            }

            await _send.WaitAsync(_closed.Token).ConfigureAwait(false);
            try { await FramedJsonPipe.WriteAsync(stream, response, BridgeWireJsonContext.Default.BridgeRpc, "GAME_BRIDGE", _closed.Token).ConfigureAwait(false); }
            finally { _send.Release(); }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally { Interlocked.Decrement(ref _receiving); }
    }

    internal async Task<JsonElement> CallAsync(string method, JsonElement? parameters, CancellationToken ct)
    {
        lock (_lifetime)
        {
            if (_disposing || !Connected) throw new LauncherOperationException("GAME_BRIDGE_DISCONNECTED", "The game bridge is offline.");
            if (_active >= 32) throw new LauncherOperationException("GAME_BRIDGE_BUSY", "The game bridge request queue is full.");
            _active++;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, _closed.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        var request = new BridgeRpc { Id = Guid.NewGuid().ToString("N"), Method = method, Params = parameters };
        var completion = new TaskCompletionSource<BridgeRpc>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.Id] = completion;
        try
        {
            await _send.WaitAsync(deadline.Token).ConfigureAwait(false);
            try { await FramedJsonPipe.WriteAsync(stream, request, BridgeWireJsonContext.Default.BridgeRpc, "GAME_BRIDGE", deadline.Token).ConfigureAwait(false); }
            finally { _send.Release(); }

            var response = await completion.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            if (response.Error is { } error) throw new LauncherOperationException(error.Message, "The game bridge refused this request.");
            return response.Result!.Value;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LauncherOperationException("GAME_BRIDGE_TIMEOUT", "The game did not finish the request before its deadline; no edit was replayed.");
        }
        finally
        {
            _pending.TryRemove(request.Id, out _);
            lock (_lifetime) { if (--_active == 0 && _disposing) _drained.TrySetResult(); }
        }
    }

    /// <summary>Disconnects this transport without stopping or restarting the game process.</summary>
    public async ValueTask DisposeAsync()
    {
        lock (_lifetime)
        {
            if (_disposing) return;
            _disposing = true;
            if (_active == 0) _drained.TrySetResult();
        }

        _closed.Cancel();
        await stream.DisposeAsync().ConfigureAwait(false);
        if (_reader is not null) await _reader.ConfigureAwait(false);
        Task[] incoming;
        lock (_incoming) incoming = [.. _incoming];
        await Task.WhenAll(incoming).ConfigureAwait(false);
        await _drained.Task.ConfigureAwait(false);
        _send.Dispose();
        _closed.Dispose();
    }
}