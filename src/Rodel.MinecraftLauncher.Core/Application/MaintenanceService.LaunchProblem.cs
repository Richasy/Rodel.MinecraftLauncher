// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Launch;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class MaintenanceService
{
    /// <summary>Combines instance-owned session or preparation-task evidence into an advisory recovery report.</summary>
    public async Task<ILaunchDiagnosis> DiagnoseLaunchAsync(string instanceId, string? sessionId = null, string? jobId = null, CancellationToken ct = default)
    {
        var instance = new Instances.InstanceStore(paths).Get(instanceId);
        if (sessionId is not null)
        {
            var session = new SessionService(paths).Get(sessionId);
            if (session.InstanceId != instanceId
                && !string.Equals(session.GameDirectory, instance.GameDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The session does not belong to the requested instance.", nameof(sessionId));
            }

            if (jobId is not null && session.JobId != jobId)
                throw new ArgumentException("The session does not belong to the requested launch job.", nameof(sessionId));

            return (await DiagnoseSessionAsync(sessionId, ct).ConfigureAwait(false)) with { InstanceId = instanceId };
        }

        JobStatus? job = null;
        if (jobId is not null)
        {
            job = new JobService(paths).Get(jobId);
            if (job.Request?.Parameters?["instanceId"]?.ToString() != instanceId)
                throw new ArgumentException("The job does not belong to the requested instance.", nameof(jobId));
        }

        var active = new SessionService(paths).HasActiveGame(instance.GameDirectory);
        return new PreparationDiagnosis(instanceId, job, active, [], LaunchProblem.DescribePreparation(active, job));
    }

    private static List<DiagnosticLogEvent> SnapshotLogEvents(LogEventRead logRead)
    {
        var result = new List<DiagnosticLogEvent>();
        foreach (var stream in new[] { "stdoutLog", "stderrLog" })
        {
            foreach (var entry in logRead.Events.Where(entry => entry.Stream == stream))
                result.Add(new(entry));
            if (logRead.ParserState.TryGetValue(stream, out var state) && state.TextEvent is { } pending)
            {
                result.Add(new(pending with { Stream = stream }, true));
            }
        }

        return result;
    }

    private static List<ReportedLaunchError> ReadReportedErrors(IReadOnlyList<DiagnosticLogEvent> events, LogEventRead logRead, Servers.ServerConnectionReport? connection)
    {
        var start = connection?.AttemptEventIndex;
        var differentTarget = connection?.Observation == "different_target";
        var errors = events.Select((entry, index) => (entry, index))
            .Where(item => item.entry.Event.Level.ToUpperInvariant() is "ERROR" or "FATAL"
                && (!string.IsNullOrWhiteSpace(item.entry.Event.Message) || !string.IsNullOrWhiteSpace(DiagnosticLogFields.Exception(item.entry.Event))));
        return [.. errors.TakeLast(16).Select(item =>
        {
            var entry = item.entry.Event;
            var stream = entry.Stream ?? "stdoutLog";
            var scope = stream == "stderrLog" ? "game-process"
                : !differentTarget && start is { } boundary && item.index > boundary ? "matching-connection-attempt"
                : connection is null || (start is null && (!logRead.Streams.TryGetValue(stream, out var summary) || summary.Truncated != true))
                    ? "game-process" : "unattributed-session-log";
            return (ReportedLaunchError)new ParsedLaunchError(entry.Message, entry.Level.ToUpperInvariant())
            {
                Exception = DiagnosticLogFields.Exception(entry),
                Logger = DiagnosticLogFields.Logger(entry),
                Timestamp = DiagnosticLogFields.Timestamp(entry),
                Stream = entry.Stream,
                Format = entry.Format,
                Pending = item.entry.Pending,
                Warning = entry.Warning,
                Scope = scope
            };
        })];
    }
}