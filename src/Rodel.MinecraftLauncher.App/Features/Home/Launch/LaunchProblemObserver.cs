// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal sealed class LaunchProblemObserver(LauncherClient client)
{
    private readonly Dictionary<string, (string Session, string Status, DateTimeOffset Checked, LaunchProblemReport? Problem)> _cache = [with(StringComparer.Ordinal)];

    internal async Task<IReadOnlyDictionary<string, HomeLaunchState>> EnrichAsync(IReadOnlyDictionary<string, HomeLaunchState> states, IReadOnlyList<string> instanceIds, CancellationToken ct)
    {
        var result = new Dictionary<string, HomeLaunchState>(states);
        foreach (var instanceId in instanceIds)
        {
            if (!states.TryGetValue(instanceId, out var launch))
                continue;
            var session = launch.SessionId ?? (launch.JobId is { } job ? "job:" + job : null);
            if (session is null)
                continue;
            var now = DateTimeOffset.UtcNow;
            var (Session, Status, Checked, Problem) = _cache.GetValueOrDefault(instanceId);
            if (session != Session || launch.Status != Status || (launch.Busy && now - Checked > TimeSpan.FromSeconds(3)))
            {
                LaunchProblemReport? problem;
                try
                {
                    problem = (await Task.Run(() => client.Maintenance.DiagnoseLaunchAsync(instanceId, launch.SessionId, launch.JobId, ct), ct)).Problem;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
                {
                    problem = new UnavailableLaunchProblem(exception.Message);
                }

                _cache[instanceId] = (session, launch.Status, now, problem);
            }

            result[instanceId] = launch with
            {
                Problem = _cache[instanceId].Problem
            };
        }

        if (_cache.Count > 64)
        {
            foreach (var key in _cache.Keys.Where(key => !instanceIds.Contains(key)).ToArray())
                _cache.Remove(key);
        }

        return result;
    }
}