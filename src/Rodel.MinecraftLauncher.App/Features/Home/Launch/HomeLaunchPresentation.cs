// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Imaging;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal static class HomeLaunchPresentation
{
    // Waiting animation only: the existing verified-window state ends it, never the elapsed time.

    internal static double? WindowWaitProgress(HomeLaunchState state) => state.WaitingForWindow && DateTimeOffset.TryParse(state.ProcessStartUtc, out var started) ? 95d * (1d - Math.Exp(-Math.Max(0d, (DateTimeOffset.UtcNow - started).TotalSeconds) / 2.5d)) : null;

    internal static string Title(HomeLaunchState state) => state.Status == "canceled"
        ? Resources.LaunchCanceled
        : state.Problem is { } problem && LaunchProblemText.HasFinding(problem) ? LaunchProblemText.Title(problem) : state.Status switch
        {
            "running" when state.WaitingForWindow => Resources.LaunchWaitingForWindow,
            "running" when state.ServerAddress is not null => Resources.ProblemStartedUnconfirmed,
            "running" => Resources.GameRunning,
            "queued" or "preparing" => Resources.LaunchPreparing,
            "failed" => Resources.LaunchFailed,
            "canceled" => Resources.LaunchCanceled,
            "exited" => Resources.GameExited,
            "interrupted" or "unobserved" => Resources.LaunchInterrupted,
            _ => Resources.LaunchGame,
        };

    internal static string Detail(HomeLaunchState state)
    {
        string WithLanguageWarning(string detail) => state.GameLanguageWarning is { } warning ? string.Format(Resources.LaunchLanguageSyncWarning, detail, warning) : detail;
        if (state.Status == "canceled")
            return Resources.LaunchCanceledDetail;
        if (state.Problem is { } problem && LaunchProblemText.HasFinding(problem))
            return LaunchProblemText.Detail(problem);
        if (state.WaitingForWindow)
        {
            return WithLanguageWarning(state.WindowObservationWarning is { } warning ? string.Format(Resources.LaunchWindowObservationError, warning) : Resources.LaunchWaitingForWindowDetail);
        }

        if (state.ServerAddress is not null && state.Status == "running")
            return WithLanguageWarning(Resources.ProblemUnconfirmedDetail);
        if (state.Error is { Length: > 0 } error)
            return string.Format(Resources.LaunchErrorDetail, error);
        if (state.Status == "running")
            return WithLanguageWarning(Resources.GameRunningDetail);
        if (state.ExitCode is { } code)
            return string.Format(Resources.GameExitCode, code);
        if (state.Status is "unobserved" or "interrupted")
            return Resources.LaunchInterruptedDetail;
        if (state.Status == "failed")
            return Resources.LaunchInterruptedDetail;
        if (state.TotalFiles is > 0 and var total && state.CompletedFiles is { } completed && state.Stage is not ("download" or "java-install" or "java-discovery"))
        {
            return string.Format(Resources.FeatureTaskFileProgress, completed, total);
        }

        if (state.Bytes is { } bytes && state.TotalBytes is > 0)
        {
            return string.Format(Resources.LaunchDownloadBytes, AppImageCache.FormatSize(bytes), AppImageCache.FormatSize(state.TotalBytes.Value));
        }

        return state.Stage switch
        {
            "download" => Resources.LaunchDownloading,
            "metadata" or "manifest" => Resources.LaunchReadingMetadata,
            "completed" => Resources.LaunchCheckingSession,
            _ => Resources.LaunchCheckingFiles,
        };
    }
}