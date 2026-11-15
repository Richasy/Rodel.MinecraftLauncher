// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Assistant;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed class AiRuntime
{
    private static readonly ConcurrentDictionary<string, Lazy<AiRuntime>> Runtimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly AiHostProtocol _protocol;
    private readonly LauncherPaths _paths;
    private readonly AiProviderService _requests;
    private readonly AiLocalInferenceRuntime _localInference = new();
    private readonly SemaphoreSlim _generation = new(1, 1);
    private readonly AiChatCoordinator _chat;
    private readonly SemaphoreSlim _recovery = new(1, 1);
    private bool _recovered;
    private bool _serving;

    public static IReadOnlyList<string> ReadOnlyToolNames => AiChatRunner.ReadOnlyToolNames;

    private AiRuntime(LauncherPaths paths)
    {
        _protocol = new(paths);
        _paths = new(_protocol.CanonicalRoot, paths.CacheDirectory, paths.ApplicationDataDirectory);
        _requests = new(_paths, _localInference);
        _chat = new(_paths, _generation, _localInference);
    }

    public static AiRuntime For(LauncherPaths paths)
    {
        var root = ResourceLease.CanonicalPath(paths.Root);
        var scope = paths.ApplicationDataDirectory is { } directory ? ResourceLease.CanonicalPath(directory) : string.Empty;
        return Runtimes.GetOrAdd(root + "\0" + scope, _ => new Lazy<AiRuntime>(() => new(paths))).Value;
    }

    /// <summary>Runs a local operation without serializing its typed result through the RPC protocol.</summary>
    public async Task<object> InvokeAsync(AiHostOperation operation, CancellationToken ct)
    {
        if (!_chat.IsActive && operation is AiShowTurnOperation or AiReadEventsOperation
            && operation is AiChatOperation { ConversationId: { } conversation, TurnId: { } turnId })
        {
            var turn = new AiChatStore(_paths).GetTurnDocument(conversation, turnId);
            if (!AiChatStore.IsTerminal(turn["status"]?.ToString()) && !AiGenerationLease.OwnerAlive(turn)) _recovered = false;
        }

        await RecoverAsync(ct, operation is AiListConversationsOperation).ConfigureAwait(false);
        return await DispatchAsync(operation, ct).ConfigureAwait(false);
    }

    private async Task RecoverAsync(CancellationToken ct, bool cleanupGames = false)
    {
        if ((_recovered && !cleanupGames) || _chat.IsActive) return;
        await _recovery.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_recovered && !cleanupGames) return;
            FileStream lease;
            try { lease = AiGenerationLease.Acquire(_paths); }
            catch (LauncherOperationException error) when (error.Code == "AI_BUSY") { return; }

            using (lease)
            {
                if (!_recovered) await new AiChatStore(_paths).RecoverInterruptedAsync(ct).ConfigureAwait(false);
                await new AiGameHistory(_paths).RemoveClosedAsync(ct).ConfigureAwait(false);
            }

            _recovered = true;
        }
        finally { _recovery.Release(); }
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var directory = Path.Combine(_paths.Root, "ai");
        Directory.CreateDirectory(directory);
        using var ownership = new FileStream(Path.Combine(directory, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        await RecoverAsync(ct).ConfigureAwait(false);
        _serving = true;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var connections = new List<Task>();
        var idleSince = Environment.TickCount64;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                foreach (var finished in connections.Where(task => task.IsCompleted).ToArray())
                {
                    await finished.ConfigureAwait(false);
                    connections.Remove(finished);
                }

                if (connections.Count > 0 || _chat.IsActive || _chat.HasBackgroundWork)
                    idleSince = Environment.TickCount64;
                else if (Environment.TickCount64 - idleSince >= 5000)
                    break;
                if (connections.Count >= 8)
                {
                    await Task.WhenAny(connections).ConfigureAwait(false);
                    continue;
                }

                using var acceptTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                acceptTimeout.CancelAfter(TimeSpan.FromSeconds(1));
                var pipe = new NamedPipeServerStream(_protocol.PipeName, PipeDirection.InOut, 8,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try
                {
                    await pipe.WaitForConnectionAsync(acceptTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
                {
                    pipe.Dispose();
                    continue;
                }
                catch
                {
                    pipe.Dispose();
                    throw;
                }

                connections.Add(HandleAsync(pipe, lifetime.Token));
            }
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            await _chat.StopAsync().ConfigureAwait(false);
            await Task.WhenAll(connections).ConfigureAwait(false);
            await _localInference.UnloadAsync(CancellationToken.None).ConfigureAwait(false);
            _serving = false;
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken hostToken)
    {
        using (pipe)
        {
            var requestId = Guid.NewGuid().ToString("N");
            try
            {
                using var handshake = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
                handshake.CancelAfter(TimeSpan.FromSeconds(10));
                var hello = await AiHostProtocol.ReadAsync(pipe, handshake.Token).ConfigureAwait(false);
                _protocol.Validate(hello);
                requestId = hello.RequestId!;
                if (hello.Method != "hello")
                    throw new LauncherOperationException("AI_HOST_HANDSHAKE_REQUIRED", "Complete the current-user data-root handshake before making an AI request.");
                await WriteResultAsync(pipe, requestId, HostInfo(), null, handshake.Token).ConfigureAwait(false);
                var request = await AiHostProtocol.ReadAsync(pipe, handshake.Token).ConfigureAwait(false);
                _protocol.Validate(request);
                requestId = request.RequestId!;
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
                using var stopMonitor = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
                var monitor = WatchCancellationAsync(pipe, requestId, cancellation, stopMonitor.Token);
                object? result = null;
                Exception? failure = null;
                try
                {
                    result = await InvokeAsync(AiHostOperations.Read(request.Method!, request.Data), cancellation.Token).ConfigureAwait(false);
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                catch (Exception error)
                {
                    failure = error;
                }
                finally
                {
                    await stopMonitor.CancelAsync().ConfigureAwait(false);
                    await monitor.ConfigureAwait(false);
                }

                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    await new AiServiceStore(_paths).CleanupCredentialsAsync(cleanup.Token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException or LauncherOperationException)
                {
                    var payload = SerializeResult(result) ?? [];
                    payload["warnings"] = new JsonArray(new JsonObject { ["code"] = "AI_CREDENTIAL_CLEANUP_PENDING", ["message"] = "Retired credentials remain protected and will be retried by a later service operation." });
                    result = payload;
                }

                using var output = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
                output.CancelAfter(TimeSpan.FromSeconds(5));
                await WriteResultAsync(pipe, requestId, result, failure, output.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException)
            {
                // The connection's foreground request has already been canceled/joined before disposal.
            }
            catch (Exception error)
            {
                using var output = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await WriteResultAsync(pipe, requestId, null, error, output.Token).ConfigureAwait(false);
                }
                catch (Exception transport) when (transport is IOException or OperationCanceledException)
                {
                    // The peer is gone; there is no other client's output stream to write to.
                }
            }
        }
    }

    private async Task<object> DispatchAsync(AiHostOperation operation, CancellationToken ct)
    {
        if (operation is AiChatOperation chat)
            return await _chat.ExecuteAsync(chat, ct).ConfigureAwait(false);
        switch (operation)
        {
            case AiInspectMcpOperation inspect:
                var registrations = (await new AiExtensionStore(_paths).ReadDocumentAsync(ct).ConfigureAwait(false))["mcp"]!.AsArray();
                var requested = inspect.Id;
                if (requested is not null && !registrations.Any(item => item?["id"]?.ToString() == requested))
                    throw new LauncherOperationException("AI_EXTENSION_NOT_FOUND", "This MCP registration no longer exists.");
                var inspected = new List<AiMcpDescription>();
                foreach (var registration in registrations)
                {
                    if (requested is not null && registration!["id"]!.ToString() != requested) continue;
                    if (registration!["enabled"]?.GetValue<bool>() != true)
                    {
                        inspected.Add(new() { Id = registration["id"]!.GetValue<string>(), Status = "disabled" });
                        continue;
                    }

                    try
                    {
                        var session = await AiMcpSession.ConnectAsync(registration.Deserialize(AssistantJsonContext.Default.AiMcpRegistration)
                            ?? throw new JsonException("The MCP registration is empty."), ct).ConfigureAwait(false);
                        await using (session.ConfigureAwait(false))
                            inspected.Add(session.Describe());
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception error)
                    {
                        inspected.Add(new() { Id = registration["id"]!.GetValue<string>(), Status = "failed", Error = LauncherOperationException.ToResult(error)["error"]!.DeepClone() });
                    }
                }

                return new AiMcpCatalog { Items = [.. inspected] };
            case AiListSkillsOperation:
                return (await AiSkills.LoadAsync(_paths, ct).ConfigureAwait(false)).List();
            case AiReadSkillOperation skill:
                return (await AiSkills.LoadAsync(_paths, ct).ConfigureAwait(false)).Read(skill.Id);
            case AiHostStatusOperation:
                return HostInfo();
            case AiLocalStatusOperation:
                return await _localInference.StatusAsync(ct).ConfigureAwait(false);
            case AiLocalModelsOperation:
#if RODEL_LOCAL_INFERENCE
                var localLease = await _localInference.AcquireAsync(ct).ConfigureAwait(false);
                await using (localLease.ConfigureAwait(false))
                {
                    var localModels = await localLease.Engine.GetModelsAsync(ct).ConfigureAwait(false);
                    return new AiLocalModelCatalog
                    {
                        Models = [.. localModels.Select(model => new AiLocalModelReference { Id = model, DisplayName = model })]
                    };
                }
#else
                throw AiLocalInferenceRuntime.Unavailable();
#endif
            case AiLocalUnloadOperation:
                if (!await _generation.WaitAsync(0, ct).ConfigureAwait(false))
                    throw new LauncherOperationException("AI_BUSY", "Stop the active request before unloading the local model runtime.");
                try { await _localInference.UnloadAsync(ct).ConfigureAwait(false); }
                finally { _generation.Release(); }

                return await _localInference.StatusAsync(ct).ConfigureAwait(false);
            case AiListModelsOperation models:
                return await _requests.ListModelsAsync(models.ServiceId, ct).ConfigureAwait(false);
            case AiGenerateOperation generation:
                if (!await _generation.WaitAsync(0, ct).ConfigureAwait(false))
                    throw new LauncherOperationException("AI_BUSY", "This data root already has an active AI text request. No second request was queued.");
                try
                {
                    using var lease = AiGenerationLease.Acquire(_paths);
                    return await _requests.GenerateAsync(generation.Request, ct).ConfigureAwait(false);
                }
                finally
                {
                    _generation.Release();
                }
            default:
                throw new LauncherOperationException("AI_HOST_METHOD_UNKNOWN", "The requested AI host method is not implemented.");
        }
    }

    private async Task WatchCancellationAsync(Stream stream, string requestId, CancellationTokenSource request, CancellationToken stopToken)
    {
        try
        {
            var frame = await AiHostProtocol.ReadAsync(stream, stopToken).ConfigureAwait(false);
            _protocol.Validate(frame, requestId);
            if (frame.Method != "cancel")
                throw new LauncherOperationException("AI_HOST_METHOD_INVALID", "Only cancellation is allowed while a foreground request is active.");
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            return;
        }
        catch (IOException)
        {
            // EOF cancels only the request owned by this connection, not another client or Inference.
        }
        finally
        {
            if (!stopToken.IsCancellationRequested)
                await request.CancelAsync().ConfigureAwait(false);
        }
    }

    private AiHostStatus HostInfo() => new()
    {
        ProtocolVersion = AiHostProtocol.Version,
        ProcessId = Environment.ProcessId,
        RootIdentity = _protocol.RootIdentity,
        DynamicCodeSupported = System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported,
        ActiveTextRequest = _generation.CurrentCount == 0,
        Hosting = _serving ? "cli-background" : "in-process-core",
        ChatStorageFailure = _chat.StorageFailure
    };

    private static JsonObject? SerializeResult(object? data) => data is null ? null : data is JsonObject node ? node
        : JsonSerializer.SerializeToNode(data, AiJsonContext.Default.GetTypeInfo(data.GetType())
            ?? throw new InvalidOperationException("Missing AI result serialization metadata."))!.AsObject();

    private Task WriteResultAsync(Stream pipe, string requestId, object? data, Exception? error, CancellationToken ct)
    {
        var failure = error is null ? null : LauncherOperationException.CreateResult(error);
        var response = _protocol.Envelope(requestId, "result", failure?.Data?.DeepClone() ?? SerializeResult(data)) with
        {
            Status = error is null ? "succeeded" : error is OperationCanceledException ? "canceled" : "failed",
            Error = failure?.Error
        };

        return AiHostProtocol.WriteAsync(pipe, response, ct);
    }
}