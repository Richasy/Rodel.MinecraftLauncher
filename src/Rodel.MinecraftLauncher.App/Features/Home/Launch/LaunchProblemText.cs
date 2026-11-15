// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal static class LaunchProblemText
{
    internal static ReportedLaunchError? OriginalError(LaunchProblemReport problem) => (problem as LaunchDiagnosisProblem)?.ReportedError ?? (problem as LaunchDiagnosisProblem)?.SessionReportedError;

    internal static bool ProcessContext(LaunchProblemReport problem) => OriginalError(problem)?.AttributionScope is "game-process" or "unattributed-session-log";

    internal static bool HasFinding(LaunchProblemReport? problem) => problem is LaunchDiagnosisProblem { ReportedError: not null } || problem?.Code is "DIAGNOSIS_UNAVAILABLE" or "STATUS_QUERY_UNAVAILABLE";

    internal static bool IsCritical(LaunchProblemReport? problem) => problem is LaunchDiagnosisProblem { ReportedError: { } error } && (error.Level is "ERROR" or "FATAL" || error.FromFailedProcess);

    internal static string Title(LaunchProblemReport problem)
    {
        var error = OriginalError(problem);
        var original = error?.Message;
        if (string.IsNullOrWhiteSpace(original))
            original = error?.ExceptionText;
        if (original is { Length: > 0 })
            return original.Split('\n', 2)[0].TrimEnd('\r');
        return problem.Code == "DIAGNOSIS_UNAVAILABLE" ? Resources.ProblemReadFailed : Resources.ProblemOriginalUnavailableTitle;
    }

    internal static string Detail(LaunchProblemReport problem)
    {
        if (OriginalError(problem) is { } error)
        {
            var message = error.Message ?? string.Empty;
            var newline = message.IndexOf('\n');
            var remaining = newline >= 0 ? message[(newline + 1)..] : string.Empty;
            var exception = error.ExceptionText ?? string.Empty;
            return string.Join("\n", new[] { remaining, exception }.Where(text => text.Length > 0));
        }

        return problem.Code == "DIAGNOSIS_UNAVAILABLE" ? problem.Evidence ?? Resources.ProblemOriginalUnavailableDetail : Resources.ProblemOriginalUnavailableDetail;
    }
}