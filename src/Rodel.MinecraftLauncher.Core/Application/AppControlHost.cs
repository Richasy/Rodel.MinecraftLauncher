// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>A current-user App-owned endpoint. The App supplies actual foreground operations, not a second business implementation.</summary>
public sealed class AppControlHost : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly AppControlRegistration _registration;
    private readonly string _file;
    private readonly Task _run;
    private int _stopping;
    private int _withdrawn;

    private AppControlHost(AppControlRegistration registration, string directory,
        Func<AppControlRequest, CancellationToken, Task<JsonObject>> handler,
        Func<AppControlRequest, Task>? afterReply, TaskCompletionSource ready)
    {
        _registration = registration;
        _file = Path.Combine(directory, registration.Id + ".json");
        _run = RunAsync(handler, afterReply, ready);
    }

    /// <summary>Registers this exact process and starts bounded current-user pipe handling before returning.</summary>
    public static Task<AppControlHost> StartAsync(LauncherPaths paths,
        Func<AppControlRequest, CancellationToken, Task<JsonObject>> handler,
        Func<AppControlRequest, Task>? afterReply = null, CancellationToken ct = default)
        => StartAsync(paths, handler, afterReply, AppControlService.RegistryDirectory(paths), ct);

    internal static async Task<AppControlHost> StartAsync(LauncherPaths paths,
        Func<AppControlRequest, CancellationToken, Task<JsonObject>> handler,
        Func<AppControlRequest, Task>? afterReply, string directory, CancellationToken ct)
    {
        using var process = Process.GetCurrentProcess();
        var id = Guid.NewGuid().ToString("N");
        var registration = new AppControlRegistration(id, process.Id, process.StartTime.ToUniversalTime().Ticks,
            ResourceLease.CanonicalPath(paths.Root), AppControlProcessIdentity.PackageFor(process), "rodel-app-" + id,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new AppControlHost(registration, directory, handler, afterReply, ready);
        try { await ready.Task.WaitAsync(ct).ConfigureAwait(false); return host; }
        catch { await host.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private async Task RunAsync(Func<AppControlRequest, CancellationToken, Task<JsonObject>> handler,
        Func<AppControlRequest, Task>? afterReply, TaskCompletionSource ready)
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                using var pipe = new NamedPipeServerStream(_registration.PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                var connected = pipe.WaitForConnectionAsync(_lifetime.Token);
                if (!ready.Task.IsCompleted)
                {
                    await JsonFiles.WriteAsync(_file, JsonSerializer.SerializeToNode(_registration, AppControlWireJsonContext.Default.AppControlRegistration)!, _lifetime.Token).ConfigureAwait(false);
                    ready.SetResult();
                }

                await connected.ConfigureAwait(false);
                await HandleAsync(pipe, handler, afterReply).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { Trace.TraceError("App control listener failed: {0}", error); ready.TrySetException(error); throw; }
        finally { WithdrawRegistration(); }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe,
        Func<AppControlRequest, CancellationToken, Task<JsonObject>> handler, Func<AppControlRequest, Task>? afterReply)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        AppControlRequest? request = null;
        string? requestId = null;
        AppControlResponseFrame result;
        try
        {
            var document = await FramedJsonPipe.ReadAsync(pipe, "APP_CONTROL", deadline.Token).ConfigureAwait(false);
            var frame = document.Deserialize(AppControlWireJsonContext.Default.AppControlRequestFrame)
                ?? throw new InvalidDataException("An App request frame is required.");
            requestId = frame.RequestId;
            if (frame.Version != 1 || frame.HostId != _registration.Id
                || frame.Nonce != _registration.Nonce || !Guid.TryParseExact(requestId, "N", out _))
            {
                throw new LauncherOperationException("APP_CONTROL_IDENTITY_MISMATCH", "The selected App identity or request does not match.");
            }

            request = frame.Request ?? throw new ArgumentException("An App operation is required.");
            using var monitorStop = new CancellationTokenSource();
            var disconnected = WatchDisconnectAsync(pipe, deadline, monitorStop.Token);
            JsonObject data;
            try { data = await handler(request, deadline.Token).ConfigureAwait(false); }
            finally
            {
                await monitorStop.CancelAsync().ConfigureAwait(false);
                await disconnected.ConfigureAwait(false);
            }

            result = new(requestId, _registration.Id, "succeeded", data);
        }

        catch (Exception error) when (error is not OutOfMemoryException)
        {
            var failure = LauncherOperationException.CreateResult(error);
            result = new(requestId, _registration.Id, failure.Status == LauncherResultStatus.Canceled ? "canceled" : "failed",
                failure.Data, failure.SchemaVersion, failure.Error);
        }

        try
        {
            await FramedJsonPipe.WriteAsync(pipe,
                JsonSerializer.SerializeToNode(result, AppControlWireJsonContext.Default.AppControlResponseFrame)!.AsObject(),
                "APP_CONTROL", deadline.Token).ConfigureAwait(false);
            if (result.Status == "succeeded" && request is not null && afterReply is not null)
                await afterReply(request).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
            Trace.TraceWarning("App control response was not delivered: {0}", error.Message);
        }
    }

    private static async Task WatchDisconnectAsync(Stream pipe, CancellationTokenSource operation, CancellationToken stop)
    {
        try
        {
            var next = new byte[1];
            _ = await pipe.ReadAsync(next, stop).ConfigureAwait(false);
            await operation.CancelAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (IOException) { await operation.CancelAsync().ConfigureAwait(false); }
    }

    private void WithdrawRegistration()
    {
        if (Interlocked.CompareExchange(ref _withdrawn, 1, 0) != 0) return;
        if (!File.Exists(_file)) { Volatile.Write(ref _withdrawn, 0); return; }

        try { File.Delete(_file); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Volatile.Write(ref _withdrawn, 0);
            Trace.TraceWarning("App discovery cleanup is pending: {0}", error.Message);
        }
    }

    /// <summary>Withdraws this owned endpoint before a UI message loop exits; never waits for that same UI thread.</summary>
    public void RequestStop()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        _lifetime.Cancel();
        WithdrawRegistration();
    }

    /// <summary>Stops this endpoint, awaits owned work and removes only its own discovery record.</summary>
    public async ValueTask DisposeAsync()
    {
        RequestStop();
        try { await _run.ConfigureAwait(false); }
        finally { _lifetime.Dispose(); }
    }
}