// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Launch;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal sealed record HomeLaunchState(
    string Status,
    string? JobId = null,
    string? SessionId = null,
    string? Error = null,
    string? Stage = null,
    long? Bytes = null,
    long? TotalBytes = null,
    int? ProcessId = null,
    int? ExitCode = null,
    bool Owned = false,
    bool TaskActive = false,
    bool TaskOwned = false,
    bool SessionOwned = false,
    string? ServerAddress = null,
    LaunchProblemReport? Problem = null,
    int? CompletedFiles = null,
    int? TotalFiles = null,
    bool GameWindowVisible = false,
    string? WindowObservationWarning = null,
    string? GameLanguageWarning = null,
    string? ProcessStartUtc = null)
{
    internal bool WaitingForWindow => Status == "running" && !GameWindowVisible;
    internal bool Busy => TaskActive || Status is "queued" or "preparing" or "running" or "unobserved" or "unreadable";
}

internal static class HomeLaunchReader
{
    private static readonly DateTime OwnerStartedAt = ReadOwnerStartUtc();
    private static readonly string OwnerStartUtc = OwnerStartedAt.ToString("O");

    internal static IReadOnlyDictionary<string, HomeLaunchState> Read(LauncherClient client)
    {
        var jobs = client.Jobs.List().Where(j => j.Request?.Operation == "launcher-task" && j.Request?.Parameters?["kind"]?.ToString() == "launch").ToArray();
        var sessions = client.Sessions.List();
        var result = new Dictionary<string, HomeLaunchState>();
        foreach (var instance in client.Instances.List())
        {
            var attempts = jobs.Where(j => j.Request?.Parameters?["instanceId"]?.ToString() == instance.Id);
            var job = attempts.OrderByDescending(j => Active(j)).ThenByDescending(j => j.CreatedAt, StringComparer.Ordinal).FirstOrDefault();
            var related = sessions.Where(s => Related(s, instance.Id, instance.GameDirectory)).ToArray();
            var session = related.Where(Active).OrderByDescending(s => s.CreatedAt, StringComparer.Ordinal).FirstOrDefault() ?? related.Where(s => s.ActivityUnknown == true).FirstOrDefault() ?? related.Where(s => job is null || s.JobId == job.Id).OrderByDescending(s => s.CreatedAt, StringComparer.Ordinal).FirstOrDefault();
            if (session is not null && session.JobId != job?.Id)
                job = jobs.FirstOrDefault(item => item.Id == session.JobId);
            var jobActive = job is not null && Active(job);
            var state = session?.Status;
            var status = state ?? job?.Status ?? "idle";
            var completedWithoutSession = session is null && !jobActive && status == "succeeded";
            var unreadableWithKnownIdentity = state == "unreadable" && !jobActive && job?.ActivityUnknown != true && HasKnownActivityIdentity(session!) && related.Where(s => s.Status == "unreadable" || s.ActivityUnknown == true).All(HasKnownActivityIdentity);
            if (state == "exited" && session?.ExitCode is not 0)
                status = "failed";
            if (state is null && status == "succeeded")
                status = "unobserved";
            if (session?.ActivityUnknown == true || (session is null && job?.ActivityUnknown == true))
            {
                status = "unobserved";
            }

            var currentJobOwner = IsCurrentOwner(job?.ProcessId, job?.ProcessStartUtc);
            var sessionOwned = IsCurrentOwner(session?.MonitorId, session?.MonitorStartUtc);
            // Keep earlier terminal attempts in history so the live view cannot diagnose them as current errors.
            if (!jobActive && status is ("failed" or "interrupted" or "exited" or "canceled") && !sessionOwned && !currentJobOwner &&
                !OccurredDuringCurrentRun(session?.ExitedAt ?? job?.FinishedAt ?? session?.CreatedAt ?? job?.CreatedAt))
            {
                session = null;
                job = null;
                status = "idle";
            }

            var error = session?.Error ?? (status is "failed" or "interrupted" ? job?.Error : null);
            if (session is null && jobActive && status != "unobserved")
                status = "preparing";
            if ((completedWithoutSession && job?.ActivityUnknown != true) || unreadableWithKnownIdentity || status is not ("queued" or "preparing" or "running" or "unobserved" or "unreadable"))
            {
                try
                {
                    // Local history can be removed while a game registered by another host is still alive.
                    // Use Core's game-scoped process/registry check, not the launch job's terminal status.
                    if (SessionService.HasActiveGame(instance.GameDirectory, sessions))
                        status = "unobserved";
                    else if (completedWithoutSession)
                        status = "idle";
                    else if (unreadableWithKnownIdentity)
                        status = "interrupted";
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
                {
                    status = "unobserved";
                    error = exception.Message;
                }
            }

            var taskOwned = jobActive && currentJobOwner;
            result[instance.Id] = new(
                status,
                job?.Id,
                session?.Id,
                error,
                job?.Progress?.Stage,
                job?.Progress?.Bytes,
                job?.Progress?.TotalBytes,
                session?.ProcessId,
                session?.ExitCode,
                taskOwned || sessionOwned,
                jobActive,
                taskOwned,
                sessionOwned,
                session?.Server ?? job?.Request?.Parameters?["options"]?["serverAddress"]?.ToString() ?? job?.Request?.Parameters?["options"]?["server"]?.ToString(),
                CompletedFiles: job?.Progress?.CompletedFiles,
                TotalFiles: job?.Progress?.TotalFiles,
                GameWindowVisible: session?.Readiness == "window_visible",
                WindowObservationWarning: session?.WindowObservationWarning,
                GameLanguageWarning: session?.GameLanguageWarning,
                ProcessStartUtc: session?.ProcessStartUtc);
        }

        return result;
    }

    private static bool Related(SessionStatus session, string instanceId, string gameDirectory)
    {
        var activity = session.Activity;
        return session.InstanceId == instanceId || activity?["instanceId"]?.ToString() == instanceId || (session.InstanceId is null && string.Equals(activity?["gameDirectory"]?.ToString() ?? session.GameDirectory, gameDirectory, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Active(JobStatus value) => value.Status is "queued" or "preparing" or "running";

    private static bool Active(SessionStatus value) => value.Status is "queued" or "preparing" or "running";

    private static bool HasKnownActivityIdentity(SessionStatus session)
    {
        if (session.Activity is not JsonObject activity)
            return false;
        return activity["status"]?.ToString() switch
        {
            "running" => HasCompleteIdentity(activity, "process") && ((activity["monitorId"] is null && activity["monitorStartUtc"] is null) || HasCompleteIdentity(activity, "monitor")),
            "preparing" => HasCompleteIdentity(activity, "monitor") && ((activity["processId"] is null && activity["processStartUtc"] is null) || HasCompleteIdentity(activity, "process")),
            _ => false,
        };
    }

    private static bool HasCompleteIdentity(JsonObject activity, string prefix) => activity[prefix + "Id"] is JsonValue pid && pid.TryGetValue<int>(out var id) && id > 0 && activity[prefix + "StartUtc"] is JsonValue start && start.TryGetValue<string>(out var timestamp) && DateTime.TryParseExact(
        timestamp,
        "O",
        CultureInfo.InvariantCulture,
        DateTimeStyles.RoundtripKind,
        out _);

    private static bool IsCurrentOwner(int? processId, string? processStartUtc) => processId == Environment.ProcessId && processStartUtc == OwnerStartUtc;

    private static bool OccurredDuringCurrentRun(string? timestamp) => DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var occurredAt) && occurredAt >= OwnerStartedAt;

    private static DateTime ReadOwnerStartUtc()
    {
        using var process = Process.GetCurrentProcess();
        return process.StartTime.ToUniversalTime();
    }
}