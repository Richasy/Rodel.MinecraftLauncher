// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal sealed record HomePageState
{
    internal IReadOnlyList<InstanceSummary> Instances { get; init; } = [];
    internal IReadOnlyList<InstanceSummary> LocalInstances => [.. Instances.Where(instance => instance.ServerId is null)];
    internal bool InstancesLoaded { get; init; }
    internal long InstancesRevision { get; init; }
    internal string SelectedId { get; init; } = string.Empty;
    internal IReadOnlyList<Rodel.MinecraftLauncher.Core.Application.InstanceInstallationRecovery> InstallationRecoveries { get; init; } = [];
    internal bool CanResumeInstallation => !Pending && InstallationRecoveries.Any(item => item.InstanceId == SelectedId);

    internal bool InstallationIncomplete(string id) => InstallationRecoveries.Any(item => item.InstanceId == id);
    internal HomeInstanceDetails? Details { get; init; }
    internal string InstanceLoadError { get; init; } = string.Empty;
    internal string ConfigurationError { get; init; } = string.Empty;
    internal IReadOnlyDictionary<string, HomeLaunchState> LaunchStates { get; init; } = new Dictionary<string, HomeLaunchState>();
    internal IReadOnlyDictionary<string, string> ActionErrors { get; init; } = new Dictionary<string, string>();
    internal IReadOnlyDictionary<string, string> CancelRequests { get; init; } = new Dictionary<string, string>();
    internal IReadOnlyList<string> PendingInstances { get; init; } = [];
    internal string LaunchReadError { get; init; } = string.Empty;
    internal bool LaunchObserved { get; init; }
    internal InstanceSummary? Selected => Instances.FirstOrDefault(i => i.Id == SelectedId);
    internal bool CanConfigure => Selected is { Unreadable: false } && !Pending && !CurrentLaunch.Busy;
    internal string Heading => Selected?.DisplayName ?? Resources.HomeStartTitle;
    internal string Runtime => Details?.Runtime ?? Selected?.DisplayVersion ?? string.Empty;
    internal string Configuration => ConfigurationError.Length > 0 ? string.Format(Resources.ConfigurationReadError, ConfigurationError) : Details?.Configuration ?? (Selected is null
        ? string.Empty
        : Selected.Unreadable ? Resources.MetadataUnreadable : Resources.ReadingConfiguration);
    internal string EmptyText => !InstancesLoaded ? Resources.InstancesLoading : InstanceLoadError.Length > 0 ? string.Format(Resources.InstancesReadError, InstanceLoadError) : Resources.EmptyInstances;
    internal HomeLaunchState CurrentLaunch => LaunchStates.GetValueOrDefault(SelectedId) ?? new("idle");
    internal bool Pending => PendingInstances.Contains(SelectedId);
    internal bool HasOwnedWork => PendingInstances.Count > 0 || LaunchStates.Values.Any(s => s.Owned && s.Busy);
    internal bool CancelRequested => CurrentLaunch.JobId is { } job && CancelRequests.GetValueOrDefault(SelectedId) == job;
    internal bool CanLaunch => CanConfigure && (CanResumeInstallation || CurrentLaunch.TaskActive || Details is { CanLaunch: true }) && LaunchObserved && LaunchReadError.Length == 0 && !Pending && ((CurrentLaunch.Status != "running" && !CurrentLaunch.Busy) || (CurrentLaunch.TaskActive && CurrentLaunch.TaskOwned && !CancelRequested));
    internal string LaunchAction => Pending
        ? Resources.LaunchUpdating
        : CurrentLaunch.TaskActive && CancelRequested
        ? Resources.LaunchCanceling
        : CurrentLaunch.TaskActive
        ? Resources.LaunchPreparing
        : CurrentLaunch.WaitingForWindow
        ? Resources.LaunchWaitingForWindow
        : CurrentLaunch.Status == "running"
        ? Resources.GameRunning
        : CurrentLaunch.Busy
        ? Resources.LaunchPreparing
        : CanResumeInstallation ? Resources.CreateResumeInstallation : Resources.LaunchGame;
    internal string LaunchTitle => LaunchReadError.Length > 0 ? Resources.LaunchObservationFailed : HomeLaunchPresentation.Title(CurrentLaunch);
    internal string LaunchDetail => LaunchReadError.Length > 0
        ? string.Format(Resources.LaunchErrorDetail, LaunchReadError)
        : CurrentLaunch.TaskActive && CancelRequested ? Resources.LaunchCancelingDetail : HomeLaunchPresentation.Detail(CurrentLaunch);
    internal string ActionError => ActionErrors.GetValueOrDefault(SelectedId) ?? string.Empty;
    internal bool ShowLaunchStatus => HasLaunchError || (CurrentLaunch.Status != "idle" && CurrentLaunch is not { Status: "exited", ExitCode: 0 });
    internal bool HasLaunchError => (CurrentLaunch.Status != "canceled" && LaunchProblemText.HasFinding(CurrentLaunch.Problem)) || CurrentLaunch.Status is "failed" or "interrupted" or "unobserved" || LaunchReadError.Length > 0 || ActionError.Length > 0;
    internal bool HasCriticalLaunchError => CurrentLaunch.Status != "canceled" && (CurrentLaunch.Problem as LaunchDiagnosisProblem)?.Source != "candidate-crash-report" && (LaunchProblemText.IsCritical(CurrentLaunch.Problem) || CurrentLaunch.Status == "failed" || ActionError.Length > 0);
    internal double? FilePercent => CurrentLaunch is { TaskActive: true, TotalFiles: > 0, CompletedFiles: { } completed, Stage: not ("download" or "java-install" or "java-discovery") } ? Math.Clamp(completed * 100d / CurrentLaunch.TotalFiles.Value, 0d, 100d) : null;
    internal bool ShowLaunchProgress => CurrentLaunch.TaskActive || CurrentLaunch.Status == "running";
    internal double? LaunchPercent => CurrentLaunch is { Status: "running", GameWindowVisible: true }
        ? 100d
        : CurrentLaunch.WaitingForWindow ? HomeLaunchPresentation.WindowWaitProgress(CurrentLaunch) : FilePercent;
}