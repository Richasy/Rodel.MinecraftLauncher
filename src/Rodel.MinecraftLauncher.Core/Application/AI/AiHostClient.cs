// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed class AiHostClient(LauncherPaths paths, AiHostOptions? options = null)
{
    /// <summary>Sends one closed Core operation through the explicitly selected background host.</summary>
    public async Task<JsonObject> InvokeAsync(AiHostOperation operation, CancellationToken ct = default)
    {
        var method = operation.Method;
        var protocol = new AiHostProtocol(paths);
        using var pipe = await ConnectAsync(protocol, ct).ConfigureAwait(false);
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(ct);
        handshake.CancelAfter(TimeSpan.FromSeconds(10));
        var helloId = Guid.NewGuid().ToString("N");
        try
        {
            await AiHostProtocol.WriteAsync(pipe, protocol.Envelope(helloId, "hello"), handshake.Token).ConfigureAwait(false);
            var hello = await AiHostProtocol.ReadAsync(pipe, handshake.Token).ConfigureAwait(false);
            protocol.Validate(hello, helloId);
            EnsureSuccess(hello);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LauncherOperationException("AI_HOST_HANDSHAKE_TIMEOUT", "The AI host did not complete its identity handshake in time.");
        }

        var id = Guid.NewGuid().ToString("N");
        using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var seconds = operation is AiGenerateOperation generation ? generation.Request.TimeoutSeconds : 30;
        if (seconds is < 1 or > 3600)
            throw new ArgumentException("The model request timeout must be between 1 and 3600 seconds.");
        responseTimeout.CancelAfter(TimeSpan.FromSeconds(method == "status" ? 10 : seconds + 15));
        try
        {
            await AiHostProtocol.WriteAsync(pipe, protocol.Envelope(id, method, AiHostOperations.Write(operation)), responseTimeout.Token).ConfigureAwait(false);
            var response = await AiHostProtocol.ReadAsync(pipe, responseTimeout.Token).ConfigureAwait(false);
            protocol.Validate(response, id);
            return EnsureSuccess(response);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Every S1 connection owns one foreground request. Disconnect is also a cancellation signal.
            using var cancelTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try
            {
                await AiHostProtocol.WriteAsync(pipe, protocol.Envelope(id, "cancel"), cancelTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException)
            {
                pipe.Dispose();
            }

            throw;
        }
        catch (OperationCanceledException) when (responseTimeout.IsCancellationRequested)
        {
            throw new LauncherOperationException("AI_HOST_RESPONSE_TIMEOUT", "The AI host did not respond within the operation deadline. This connection was closed and the request was not replayed.");
        }
        catch (IOException)
        {
            throw new LauncherOperationException("AI_HOST_DISCONNECTED", "The AI host disconnected before returning a result. The model request has not been replayed.");
        }
    }

    private async Task<NamedPipeClientStream> ConnectAsync(AiHostProtocol protocol, CancellationToken ct)
    {
        var connected = await TryConnectAsync(protocol.PipeName, 150, ct).ConfigureAwait(false);
        if (connected is not null)
            return connected;
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(ct);
        startup.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var lease = await ResourceLease.AcquireAsync(Path.Combine(protocol.CanonicalRoot, "ai", "host-start"), startup.Token).ConfigureAwait(false);
            connected = await TryConnectAsync(protocol.PipeName, 150, startup.Token).ConfigureAwait(false);
            if (connected is not null)
                return connected;
            var executable = Path.GetFullPath(options?.ExecutablePath ?? Path.Combine(AppContext.BaseDirectory, "rodel-mc.exe"));
            if (!File.Exists(executable))
                throw new LauncherOperationException("AI_HOST_NOT_INSTALLED", "The selected launcher CLI is unavailable for background Core AI execution. In-process Core AI and offline configuration do not require a separate assistant executable.");
            var start = new ProcessStartInfo(executable)
            {
                // Match the existing session-monitor launch: a persistent host must not inherit
                // the submitting CLI's redirected stdout/stderr and hold its caller's EOF open.
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            };
            foreach (var prefix in options?.PrefixArguments ?? [])
                start.ArgumentList.Add(prefix);
            start.ArgumentList.Add("ai");
            start.ArgumentList.Add("serve");
            start.ArgumentList.Add("--data-dir");
            start.ArgumentList.Add(protocol.CanonicalRoot);
            using var process = Process.Start(start) ?? throw new LauncherOperationException("AI_HOST_START_FAILED", "The AI host process could not be started.");
            while (true)
            {
                connected = await TryConnectAsync(protocol.PipeName, 200, startup.Token).ConfigureAwait(false);
                if (connected is not null)
                    return connected;
                if (process.HasExited)
                    throw new LauncherOperationException("AI_HOST_START_FAILED", "The AI host exited before accepting a connection.", new JsonObject { ["exitCode"] = process.ExitCode });
                await Task.Delay(50, startup.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LauncherOperationException("AI_HOST_START_TIMEOUT", "The AI host did not become available within 20 seconds.");
        }
    }

    private static async Task<NamedPipeClientStream?> TryConnectAsync(string name, int milliseconds, CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(milliseconds, ct).ConfigureAwait(false);
            return pipe;
        }
        catch (TimeoutException)
        {
            pipe.Dispose();
            return null;
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    private static JsonObject EnsureSuccess(AiHostFrame response)
    {
        if (response.Method != "result")
            throw new LauncherOperationException("AI_HOST_FRAME_INVALID", "The AI host returned an unexpected message type.");
        if (response.Status == "succeeded")
            return (response.Data as JsonObject)?.DeepClone().AsObject() ?? throw new LauncherOperationException("AI_HOST_FRAME_INVALID", "The AI host response has no result object.");
        if (response.Status == "canceled")
            throw new OperationCanceledException("The AI request was canceled by its owner.");
        throw new LauncherOperationException(response.Error?.Code ?? "AI_HOST_FAILED",
            response.Error?.Message ?? "The AI host reported a failure.", response.Data?.DeepClone());
    }
}