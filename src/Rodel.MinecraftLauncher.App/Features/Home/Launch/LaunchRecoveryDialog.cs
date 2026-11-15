// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal sealed partial class LaunchRecoveryDialog : Control
{
    private readonly LauncherClient _client;
    private readonly HomeController _home;
    private readonly Window _window;
    private readonly string _instanceId;
    private readonly string? _server;
    private readonly Core.Servers.ServerStatus? _serverStatus;
    private readonly Action _close;
    private readonly DetailReportPopup _report;
    private readonly PopupFocusRecovery _focus;
    private readonly Action<Core.Application.AI.AiRepairCreate>? _repair;
    private readonly HomeLaunchState? _launch;
    private readonly string? _actionError;
    private Core.Application.AI.AiRepairCreate? _repairRequest;
    private readonly CancellationTokenSource _lifetime = new();
    private ILaunchDiagnosis? _diagnosis;
    private StatusQueryProblem? _queryProblem;
    private partial LaunchProblemReport? Problem { get; set; }
    private partial bool Loading { get; set; }
    private partial string ReadError { get; set; }
    private partial bool Details { get; set; }
    private string InstanceName => _home.State.Instances.FirstOrDefault(item => item.Id == _instanceId)?.DisplayName ?? _instanceId;
    private float ContentWidth => Math.Min(480f, _window.ClientSize.Width - 48f);
    private LaunchProblemReport CurrentProblem => Problem ?? new LaunchDiagnosisProblem("UNCLASSIFIED", null, "view-details")
    {
        Phase = "unconfirmed",
        Source = "none"
    };

    internal LaunchRecoveryDialog(
        LauncherClient client,
        HomeController home,
        Window window,
        string instanceId,
        string? server,
        Core.Servers.ServerStatus? serverStatus,
        Action close,
        Action<Core.Application.AI.AiRepairCreate>? repair = null,
        HomeLaunchState? launch = null,
        string? actionError = null)
    {
        _client = client;
        _home = home;
        _window = window;
        _instanceId = instanceId;
        _server = server;
        _close = close;
        _serverStatus = serverStatus;
        _repair = repair;
        _launch = launch;
        _actionError = actionError;
        _focus = new(window);
        ReadError = string.Empty;
        _report = new(
            window,
            Resources.ProblemOriginalHeading,
            string.Empty,
            close,
            ContentWidth,
            "recovery.report",
            back: () =>
            {
                Details = false;
                _focus.Request(this);
            },
            showCloseButton: false);
    }

    private Ui Body => Overlay([
        _report.Visible(Details),
        Grid(
            [GridLength.Star()],
            [GridLength.Auto, GridLength.Auto, GridLength.Auto],
            [
                Border(Grid(
                        [GridLength.Star(), GridLength.Auto],
                        [GridLength.Auto],
                        [
                            Text(Resources.ProblemOriginalHeading)
                                .FontSize(19f).FontWeight(600).Foreground(LauncherTheme.Ink)
                                .Cell(0, 0),
                            LauncherControls.OutlineCommand(Resources.ProblemFullLogs, () => ShowDetails())
                                .Style(LauncherButtons.DialogHeaderButton)
                                .Enabled(!Loading)
                                .Automation(new()
                                {
                                    Name = Resources.ProblemFullLogs,
                                    AutomationId = "recovery.details"
                                })
                                .Cell(1, 0),
                        ])
                        .ColumnSpacing(12f))
                    .Padding(24f, 20f)
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(0f, 0f, 0f, 1f)
                    .Cell(0, 0),
                ScrollView(new LaunchOriginalErrorBody(
LauncherAppearance.Capture(),                        InstanceName,
                        LaunchProblemText.ProcessContext(CurrentProblem) ? null : _server,
                        LaunchProblemText.ProcessContext(CurrentProblem),
                        _window.FlowDirection == FlowDirection.RightToLeft,
                        Loading,
                        LaunchProblemText.IsCritical(CurrentProblem),
                        LaunchProblemText.Title(CurrentProblem),
                        LaunchProblemText.Detail(CurrentProblem),
                        ReadError))
                    .MaxHeight(Math.Max(120f, _window.ClientSize.Height - 220f))
                    .Cell(0, 1),
                Border(Grid(
                        [GridLength.Star(), GridLength.Star()],
                        [GridLength.Auto],
                        [
                            LauncherControls.Command(Resources.AiRepairStart, () => StartRepair())
                                .Style(LauncherButtons.DialogPrimary)
                                .Visible(_repair is not null && _repairRequest is not null)
                                .Enabled(!Loading)
                                .Automation(new()
                                {
                                    Name = Resources.AiRepairStart,
                                    AutomationId = "recovery.ai.repair"
                                })
                                .Cell(0, 0),
                            LauncherControls.OutlineCommand(Resources.CloseLaunchSettings, _close)
                                .Style(LauncherButtons.DialogSecondary)
                                .Automation(new()
                                {
                                    Name = Resources.CloseLaunchSettings,
                                    AutomationId = "recovery.close"
                                })
                                .Cell(1, 0),
                        ])
                        .ColumnSpacing(12f))
                    .Padding(24f, 16f, 24f, 20f)
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(0f, 1f, 0f, 0f)
                    .Cell(0, 2),
            ])
            .Width(ContentWidth)
            .Visible(!Details)
    ]);

    private async Task ReadAsync()
    {
        Loading = true;
        ReadError = string.Empty;
        _queryProblem = null;
        Problem = _launch?.Problem;
        if (!LaunchProblemText.HasFinding(Problem) && (_launch?.Error ?? _actionError) is { Length: > 0 } original)
        {
            Problem = new LaunchDiagnosisProblem("LAUNCH_FAILED", original, "view-details")
            {
                Source = "launcher-error",
                Phase = _launch?.Stage ?? "preparation",
                ReportedError = new SourceLaunchError(original, "ERROR", "launcher-error")
            };
        }

        try
        {
            var launch = _launch;
            if (_server is not null && launch?.ServerAddress != _server)
                launch = null;
            var cancellation = _lifetime.Token;
            var diagnosis = await Task.Run(() => _client.Maintenance.DiagnoseLaunchAsync(_instanceId, launch?.SessionId, launch?.JobId, cancellation), cancellation);
            if (_lifetime.IsCancellationRequested)
                return;
            if (launch is null && _serverStatus is { Online: false })
            {
                _queryProblem = LaunchProblem.DescribeStatusQuery(_serverStatus);
            }

            _diagnosis = diagnosis;
            if (launch is not null && !diagnosis.GameActive && (launch.SessionId is not null || launch.JobId is not null))
            {
                _repairRequest = new()
                {
                    InstanceId = _instanceId,
                    SessionId = launch.SessionId,
                    JobId = launch.JobId
                };
            }

            if (_queryProblem is not null || LaunchProblemText.HasFinding(diagnosis.Problem) || !LaunchProblemText.HasFinding(Problem))
                Problem = (LaunchProblemReport?)_queryProblem ?? diagnosis.Problem;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            ReadError = string.Format(Resources.ProblemReadFailedDetail, exception.Message);
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
                Loading = false;
        }
    }

    private void ShowDetails()
    {
        _report.ViewHeight = Render.Bounds.Height;
        var document = _diagnosis is null ? null : JsonSerializer.SerializeToNode(_diagnosis, DiagnosticJsonContext.Default.ILaunchDiagnosis)!.AsObject();
        document ??= [];
        if (_queryProblem is not null)
        {
            document["serverStatus"] = JsonSerializer.SerializeToNode(_serverStatus, Core.Servers.ServerJsonContext.Default.ServerStatus);
        }

        document["problem"] = JsonSerializer.SerializeToNode(CurrentProblem, DiagnosticJsonContext.Default.LaunchProblemReport);
        _report.SetReport(string.Join(Environment.NewLine, LaunchProblemText.Title(CurrentProblem), LaunchProblemText.Detail(CurrentProblem)), document.ToJsonString());
        Details = true;
        _focus.Request(_report);
    }

    private void StartRepair()
    {
        if (_repairRequest is not { } request || _repair is null || Loading)
            return;
        var repair = _repair;
        _close();
        _window.RequestAnimationFrame(_ => repair(request));
    }

    protected override IDisposable? OnMounted()
    {
        _ = ReadAsync();
        return null;
    }

    protected override void OnDispose()
    {
        _focus.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnDispose();
    }

    internal bool CanClose => !_report.Exporting;
}

[View]
internal readonly partial record struct LaunchOriginalErrorBody(
    LauncherAppearance Theme,
    string InstanceName,
    string? Server,
    bool ProcessContext,
    bool RightToLeft,
    bool Loading,
    bool Critical,
    string Title,
    string Original,
    string ReadError)
{
    public Ui Body => Column([
        Column([
            Text(ProcessContext ? Resources.ProblemProcessLogHeading : Resources.ProblemTargetHeading)
                .FontSize(12f).Foreground(Theme.Muted)
                .Wrap(),
            Text(InstanceName)
                .FontSize(13f).FontWeight(600).Foreground(Theme.Ink)
                .Wrap()
                .Automation(new()
                {
                    Name = InstanceName,
                    AutomationId = "recovery.target"
                }),
            Flow(FlowDirection.LeftToRight, Text(Server ?? string.Empty)
                    .FontSize(12f).Foreground(Theme.Muted)
                    .Wrap()
                    .HAlign(HorizontalAlignment.Stretch)
                    .Align(RightToLeft ? TextAlignment.Trailing : TextAlignment.Leading))
                .Visible(Server is not null),
        ])
            .Spacing(6f),
        ProgressBar()
            .Indeterminate(true)
            .Thickness(3f)
            .Visible(Loading),
        Border(Grid(
                [GridLength.Pixels(24f), GridLength.Star()],
                [GridLength.Auto],
                [
                    LauncherIcons.Line(Critical ? "error" : "info", 24f, Critical ? Theme.Error : Theme.Muted)
                        .Cell(0, 0),
                    Column([
                        Flow(FlowDirection.LeftToRight, new SelectableText(Text(Title)
                                    .FontSize(14f).FontWeight(600).Foreground(Theme.Ink)
                                    .Wrap()
                                    .Align(RightToLeft ? TextAlignment.Trailing : TextAlignment.Leading)).HAlign(HorizontalAlignment.Stretch)
                                .Automation(new()
                                {
                                    Name = Title,
                                    AutomationId = "recovery.summary",
                                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                                })),
                        Flow(FlowDirection.LeftToRight, new SelectableText(Text(Original)
                                    .FontSize(13f).Foreground(Theme.Ink)
                                    .Wrap()
                                    .Align(RightToLeft ? TextAlignment.Trailing : TextAlignment.Leading)).HAlign(HorizontalAlignment.Stretch)
                                .Automation(new()
                                {
                                    Name = Original,
                                    AutomationId = "recovery.explanation"
                                }))
                            .Visible(Original.Length > 0),
                    ])
                        .Spacing(8f)
                        .Cell(1, 0),
                ])
                .ColumnSpacing(12f))
            .Padding(16f)
            .Background(Theme.HighContrast || !Critical ? Theme.ControlBackground : Theme.Error.MultiplyAlpha(.12f))
            .BorderBrush(Critical ? Theme.Error.MultiplyAlpha(.4f) : Theme.Edge)
            .BorderThickness(1f),
        Text(ReadError)
            .FontSize(13f).Foreground(Theme.Error)
            .Wrap()
            .Visible(ReadError.Length > 0),
    ])
        .Spacing(16f)
        .Padding(24f, 20f);
}