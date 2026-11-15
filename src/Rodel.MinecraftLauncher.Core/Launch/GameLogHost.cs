// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>Exposes only bounded in-memory console reads to other processes of the current user.</summary>
internal sealed class GameLogHost : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _listener;
    private readonly Task _cleanup;
    internal GameLogBuffer Stdout { get; } = new();
    internal GameLogBuffer Stderr { get; } = new();
    internal string PipeName { get; } = "rodel-game-log-" + Guid.NewGuid().ToString("N");
    internal string? Warning { get; private set; }

    private GameLogHost(LauncherPaths paths, TaskCompletionSource ready)
    {
        _listener = ListenAsync(ready);
        _cleanup = Task.Run(() => CleanupAsync(paths));
    }

    internal static async Task<GameLogHost> StartAsync(LauncherPaths paths)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new GameLogHost(paths, ready);
        await ready.Task.ConfigureAwait(false);
        return host;
    }

    private async Task ListenAsync(TaskCompletionSource ready)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                var connected = pipe.WaitForConnectionAsync(_stop.Token);
                ready.TrySetResult();
                await connected.ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    var frame = await FramedJsonPipe.ReadAsync(pipe, "GAME_LOG", deadline.Token).ConfigureAwait(false);
                    var request = frame.Deserialize(GameLogJsonContext.Default.LogReadRequest)
                        ?? throw new InvalidDataException("A console read request is required.");
                    LogReadResponse response;
                    try { response = new((request.StandardError ? Stderr : Stdout).Read(request.Offset, request.ByteBudget)); }
                    catch (ArgumentException error) { response = new(null, error.Message); }

                    await FramedJsonPipe.WriteAsync(pipe,
                        JsonSerializer.SerializeToNode(response, GameLogJsonContext.Default.LogReadResponse)!.AsObject(),
                        "GAME_LOG", deadline.Token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or OperationCanceledException or ArgumentException or JsonException
                    or Application.LauncherOperationException)
                {
                    Trace.TraceWarning("Game console request failed: {0}", error.Message);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Warning = "Live console reads are unavailable: " + error.Message;
            Trace.TraceWarning("{0}", Warning);
        }
        finally { ready.TrySetResult(); }
    }

    private async Task CleanupAsync(LauncherPaths paths)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        try
        {
            do { GameLogRetention.PruneIfDue(paths); }
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    internal static async Task<LogReadSnapshot> ReadAsync(string name, LogReadRequest request, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
        await FramedJsonPipe.WriteAsync(pipe,
            JsonSerializer.SerializeToNode(request, GameLogJsonContext.Default.LogReadRequest)!.AsObject(),
            "GAME_LOG", deadline.Token).ConfigureAwait(false);
        var frame = await FramedJsonPipe.ReadAsync(pipe, "GAME_LOG", deadline.Token).ConfigureAwait(false);
        var response = frame.Deserialize(GameLogJsonContext.Default.LogReadResponse)
            ?? throw new InvalidDataException("The console host returned no response.");
        if (response.Error is { } error) throw new ArgumentException(error, nameof(request));
        return response.Snapshot ?? throw new InvalidDataException("The console host returned no snapshot.");
    }

    /// <summary>Stops the reader endpoint and maintenance timer; the game pipes are owned by the session monitor.</summary>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(_listener, _cleanup).ConfigureAwait(false);
        _stop.Dispose();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LogReadRequest))]
[JsonSerializable(typeof(LogReadResponse))]
internal partial class GameLogJsonContext : JsonSerializerContext;