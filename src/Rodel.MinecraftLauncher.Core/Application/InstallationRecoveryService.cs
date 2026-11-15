// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Launch;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed record InstanceInstallationRecovery(string InstanceId, string Name, string Version, string JobId, string Status);

/// <summary>Finds each instance's latest incomplete installation without replaying or canceling another owner's work.</summary>
public sealed class InstallationRecoveryService(LauncherClient client)
{
    /// <summary>Lists interrupted installations that have not since prepared the same profile for a real game process.</summary>
    public IReadOnlyList<InstanceInstallationRecovery> ReadPending()
    {
        var latest = client.Jobs.List()
            .Where(job => job.Request?.Parameters?["kind"]?.ToString() == "install-instance")
            .OrderByDescending(job => job.CreatedAt, StringComparer.Ordinal)
            .GroupBy(job => job.Request?.Parameters?["instanceId"]?.ToString())
            .Select(group => group.First())
            .ToArray();
        var pending = new List<InstanceInstallationRecovery>();
        if (!latest.Any(job => job.Status is "failed" or "canceled" or "interrupted"))
            return pending;
        IReadOnlyList<SessionStatus> sessions = [];
        try { sessions = client.Sessions.List(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            client.Activity.WriteAsync("installation.recovery-observation-failed", code: "SESSION_READ_FAILED").GetAwaiter().GetResult();
        }

        foreach (var job in latest)
        {
            if (job.Status is not ("failed" or "canceled" or "interrupted")) continue;
            try
            {
                var request = (InstallInstanceTask)client.Tasks.GetRequest(job.Id!);
                var instance = client.Instances.Get(request.InstanceId);
                var version = request.Options.VersionId ?? instance.VersionId ?? string.Empty;
                if (DateTimeOffset.TryParse(job.CreatedAt, out var created)
                    && sessions.Any(session => session.InstanceId == instance.Id && session.VersionId == version
                        && session.ProcessId is > 0 && DateTimeOffset.TryParse(session.ProcessStartUtc, out var started)
                        && started >= created))
                {
                    continue;
                }

                pending.Add(new(instance.Id, instance.Name, version, job.Id!, job.Status!));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
            {
                // An unrelated missing or unreadable instance cannot hide the remaining recoverable instances.
            }
        }

        return pending;
    }
}