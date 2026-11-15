// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Collections.Concurrent;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>Monitors game processes inside the calling host, without a CLI executable.</summary>
/// <remarks>The calling process must remain alive to capture output and persist session completion. Disposing a view does not stop games.</remarks>
public sealed class InProcessSessionHost : ISessionHost
{
    private readonly ConcurrentDictionary<string, Task> _monitors = new();
    public Task StartAsync(LauncherPaths paths, string sessionId, CancellationToken cancellationToken = default)
    {
        var key = Path.Combine(paths.SessionsDirectory, sessionId);
        // Reserve before scheduling; GetOrAdd factories may otherwise run more than once.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_monitors.TryAdd(key, completion.Task))
            return Task.CompletedTask;
        _ = RunAsync();
        return Task.CompletedTask;
        async Task RunAsync()
        {
            try
            {
                await Task.Run(() => new SessionService(paths).RunMonitorAsync(sessionId, cancellationToken), CancellationToken.None).ConfigureAwait(false);
                completion.SetResult();
            }
            catch (Exception exception)
            {
                // Observe monitor failures even when the caller no longer waits for the game.
                completion.SetResult();
                var file = Path.Combine(paths.SessionsDirectory, sessionId + ".json");
                try
                {
                    var state = JsonFiles.ReadObject(file);
                    state["status"] = "failed";
                    state["error"] = exception.Message;
                    await JsonFiles.WriteAsync(file, state, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                }
            }
            finally
            {
                _monitors.TryRemove(key, out _);
            }
        }
    }
}