// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Servers;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>Evidence and recovery routing shared by protocol and native consumers; never infers world entry.</summary>
public static class LaunchProblem
{
    /// <summary>Describes a failed status query without treating it as proof that connecting is impossible.</summary>
    public static StatusQueryProblem DescribeStatusQuery(ServerStatus status) => new(status.Error, status.ErrorCode);

    /// <summary>Routes original bounded launch and connection evidence to an advisory recovery action.</summary>
    public static LaunchDiagnosisProblem Describe(SessionDiagnosis diagnosis)
    {
        var connection = diagnosis.ServerConnection;
        var findings = diagnosis.Findings.Where(item => item.Code != "REGISTRY_MISMATCH").ToArray();
        var details = diagnosis.Details;
        static ReportedLaunchError? SelectError(IReadOnlyList<ReportedLaunchError> errors) => errors.LastOrDefault(error => error.Level == "FATAL")
            ?? errors.LastOrDefault(error => error.ExceptionText is { Length: > 0 }) ?? (errors.Count == 0 ? null : errors[^1]);
        // Background services can log ERROR during an otherwise successful game session.
        // Retain that evidence in diagnostics, but only promote process logs after an abnormal exit.
        var processFailed = diagnosis.Status == "exited" && diagnosis.ExitCode is { } exitCode && exitCode != 0;
        var closed = connection?.Observation is "connect_failed" or "connection_closed";
        var actionableErrors = diagnosis.CurrentReportedErrors
            .Where(error => processFailed || (closed && error.AttributionScope == "matching-connection-attempt")).ToArray();
        var reportedError = SelectError(actionableErrors);
        var sessionError = processFailed && reportedError is null ? SelectError(diagnosis.ReportedErrors) : null;
        var candidate = false;
        var finding = Select(findings);
        if (finding is null)
        {
            foreach (var report in diagnosis.CrashReports)
            {
                if (Select(report.Findings) is not { } match) continue;
                finding = match; details = report.Details; candidate = true; break;
            }
        }

        if (reportedError is null && closed && connection?.Evidence is { Length: > 0 } originalReason)
            reportedError = new SourceLaunchError(originalReason, "ERROR", "client-log");
        var code = finding?.Code ?? (closed ? ConnectionCode(connection!.IssueCode)
            : reportedError is not null ? "GAME_REPORTED_ERROR" : "UNCLASSIFIED");
        return new LaunchDiagnosisProblem(code, finding?.Evidence ?? (closed ? connection?.Evidence : null), RecoveryAction(code))
        {
            Phase = reportedError?.AttributionScope == "game-process" ? "game-log" : closed ? "connection" : reportedError is not null ? "game-log" : finding is not null ? "game-startup" : "unconfirmed",
            Source = reportedError?.AttributionScope == "game-process" ? "session-log" : closed ? "client-log" : reportedError is not null ? "session-log" : finding is not null ? candidate ? "candidate-crash-report" : "session-log" : "none",
            ClassificationSource = finding is null ? null : candidate ? "candidate-crash-report" : "session-log",
            ReportedError = reportedError,
            SessionReportedError = sessionError,
            Subject = details?.ResponsibleClass,
            RegistryEntries = closed ? connection?.RegistryEntries?.ToArray() : null,
            RequiredJavaMajor = details?.RequiredJavaMajor,
            JavaRequirementKind = details?.JavaRequirementKind,
            SelectedJavaMajor = diagnosis.SelectedJavaMajor,
            GameActive = diagnosis.GameActive
        };
    }

    internal static LaunchDiagnosisProblem DescribePreparation(bool gameActive, JobStatus? job, Launch.SessionStatus? session = null)
    {
        var error = job?.Failure?["error"] as System.Text.Json.Nodes.JsonObject;
        var evidence = session?.Error ?? error?["message"]?.ToString() ?? job?.Error;
        var code = session?.ErrorCode ?? error?["code"]?.ToString() ?? "UNCLASSIFIED";
        var source = session?.Error is not null ? "session-error" : "job-error";
        var action = code.StartsWith("AUTH_", StringComparison.Ordinal) || code == "ACCOUNT_NOT_FOUND" ? "manage-accounts"
            : code.StartsWith("JAVA_", StringComparison.Ordinal) ? "configure-instance" : "view-details";
        return new(code, evidence, action)
        {
            Phase = evidence is null ? "unconfirmed" : session?.ErrorDetails?["stage"]?.ToString() ?? job?.Progress?.Stage ?? "preparation",
            Source = evidence is null ? "none" : source,
            GameActive = gameActive,
            ReportedError = evidence is null ? null : new SourceLaunchError(evidence, "ERROR", source)
        };
    }

    private static DiagnosticFinding? Select(IReadOnlyList<DiagnosticFinding> findings) =>
        findings.FirstOrDefault(item => item.Code == "JAVA_VERSION_MISMATCH")
        ?? findings.FirstOrDefault(item => item.Code == "MOD_DEPENDENCY_CONFLICT")
        ?? findings.FirstOrDefault(item => item.Code != "REGISTRY_MISMATCH");

    private static string RecoveryAction(string code) => code switch
    {
        "JAVA_VERSION_MISMATCH" or "JAVA_OUT_OF_MEMORY" or "JAVA_MEMORY_RESERVATION" => "configure-instance",
        "MOD_DEPENDENCY_CONFLICT" or "CLASS_NOT_FOUND" or "BINARY_VERSION_MISMATCH" or "REGISTRY_MISMATCH" or "MIXIN_FAILURE" => "manage-content",
        "AUTHENTICATION_FAILED" => "manage-accounts",
        "DNS_FAILED" or "CONNECTION_REFUSED" or "SERVER_TIMEOUT" or "NETWORK_UNREACHABLE" or "CONNECTION_CLOSED" => "edit-connection",
        _ => "view-details"
    };

    private static string ConnectionCode(string? code) => code switch
    {
        "dns_failed" => "DNS_FAILED",
        "connection_refused" => "CONNECTION_REFUSED",
        "server_timeout" => "SERVER_TIMEOUT",
        "network_unreachable" => "NETWORK_UNREACHABLE",
        "authentication" => "AUTHENTICATION_FAILED",
        "version_mismatch" => "BINARY_VERSION_MISMATCH",
        "registry_mismatch" => "REGISTRY_MISMATCH",
        "whitelist" => "SERVER_WHITELIST",
        "banned" => "SERVER_BANNED",
        "server_full" => "SERVER_FULL",
        _ => "CONNECTION_CLOSED"
    };
}