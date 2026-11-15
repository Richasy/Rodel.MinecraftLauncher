// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Graphics;
using Sprout.Popups;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal sealed class LaunchErrorPresenter : IDisposable
{
    private readonly Window _window;
    private Flyout? _popup;
    private readonly Func<string, CancellationToken, Task<SessionDiagnosis>> _diagnose;
    private readonly Action<string, string> _findDependency;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _loading;

    internal LaunchErrorPresenter(Window window, Func<string, CancellationToken, Task<SessionDiagnosis>> diagnose, Action<string, string> findDependency)
    {
        _window = window;
        _diagnose = diagnose;
        _findDependency = findDependency;
        window.ActualThemeChanged += OnThemeChanged;
    }

    internal async Task ShowAsync(HomePageState state, Action<Core.Application.AI.AiRepairCreate>? repair = null)
    {
        if (_loading || _lifetime.IsCancellationRequested || ModalPopup.IsOpen(_window))
            return;
        if (!state.HasLaunchError || state.Selected is not { } selected)
            return;
        var launch = state.CurrentLaunch;
        var report = new StringBuilder(string.Format(
                Resources.LaunchErrorReport,
                selected.DisplayName,
                selected.Id,
                launch.JobId ?? Resources.ValueNotAvailable,
                launch.SessionId ?? Resources.ValueNotAvailable,
                state.LaunchTitle,
                state.LaunchDetail,
                state.ActionError));
        SessionDiagnosis? diagnosis = null;
        _loading = true;
        try
        {
            if (launch.SessionId is { } session)
                diagnosis = await _diagnose(session, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            report.AppendLine().AppendLine().AppendFormat(Resources.LaunchDiagnosisFailed, exception.Message);
        }
        finally
        {
            _loading = false;
        }

        if (_lifetime.IsCancellationRequested || ModalPopup.IsOpen(_window))
            return;
        var missing = (diagnosis?.DependencyEvidence ?? []).OfType<FabricDependencyEvidence>().Where(item => item.Kind is "missing-required-dependency" or "required-dependency").DistinctBy(item => (item.DependencyId, item.RequestedBy)).ToArray();
        foreach (var item in missing)
        {
            report.AppendLine().AppendLine().AppendFormat(Resources.LaunchMissingDependency, item.RequestedBy, item.DependencyId, item.Requirement);
        }

        foreach (var item in diagnosis?.Findings ?? [])
            report.AppendLine().AppendLine().AppendLine(item.Code).AppendLine(item.Evidence);
        var query = missing.FirstOrDefault()?.DependencyId;
        if (query is not null)
            report.AppendLine().AppendLine().AppendFormat(Resources.LaunchDependencySearchNotice, query);
        var size = _window.ClientSize;
        var layout = new LauncherLayout(size.Width, size.Height);
        _popup?.Hide();
        DetailReportPopup? content = null;
        var popup = new Flyout(close =>
            {
                content = new DetailReportPopup(
                    _window,
                    string.Format(Resources.LaunchErrorTitle, selected.DisplayName),
                    report.ToString(),
                    close,
                    layout.HeroWidth - 24f,
                    findDependencies: query is not null && launch.SessionId is { } repairSession ? () =>
                    {
                        if (content?.Exporting == true)
                            return;
                        close();
                        _window.RequestAnimationFrame(_ =>
                            {
                                if (!_lifetime.IsCancellationRequested)
                                    _findDependency(selected.Id, repairSession);
                            });
                    }
                : null,
                    repair: repair is not null && (launch.SessionId is not null || launch.JobId is not null) ? () =>
                    {
                        if (content?.Exporting == true)
                            return;
                        close();
                        _window.RequestAnimationFrame(_ => repair(new()
                        {
                            InstanceId = selected.Id,
                            SessionId = launch.SessionId,
                            JobId = launch.JobId
                        }));
                    }
                : null);
                if (diagnosis is not null)
                    content.SetReport(report.ToString(), JsonSerializer.Serialize(diagnosis, ApplicationJsonContext.Default.SessionDiagnosis));
                return content;
            })
        {
            Placement = PopupPlacement.Full,
            IsLightDismissEnabled = false,
            IsModal = true,
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup).Automation(new()
        {
            Name = Resources.FullLaunchError,
            AutomationId = "launch.error.popup",
            IsDialog = true
        });
        popup.Closing += e => e.Cancel = content?.Exporting == true;
        _popup = popup;
        ModalPopup.TryShow(_window, popup);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => _popup?.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup);

    public void Dispose()
    {
        _window.ActualThemeChanged -= OnThemeChanged;
        _lifetime.Cancel();
        _popup?.Hide();
        _lifetime.Dispose();
    }
}