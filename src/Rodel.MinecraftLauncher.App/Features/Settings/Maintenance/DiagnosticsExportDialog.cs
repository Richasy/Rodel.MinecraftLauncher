// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Imaging;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.WindowsAppSDK;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed partial class DiagnosticsExportDialog : Control
{
    private readonly DiagnosticsExportController _controller;
    private readonly Window _window;
    private readonly Action _close;
    private readonly PopupPreparationClose _closing;
    private readonly PopupFocusRecovery _focus;
    private readonly DiagnosticsSessionList _sessions;
    private readonly DetailReportPopup _report;

    internal DiagnosticsExportDialog(
        LauncherClient client,
        HomeController home,
        Window window,
        Action close,
        bool launcher = true)
    {
        _controller = new(client, home, launcher);
        _window = window;
        _close = close;
        _closing = new(close, () => State.Exporting, () => CanClose, () => false);
        _focus = new(window);
        _sessions = new(_controller);
        State = _controller.State;
        _report = new(
            window,
            Resources.ContentOperationDetails,
            string.Empty,
            _close,
            Math.Min(580f, window.ClientSize.Width - 72f),
            "diagnostics.report",
            exportStateChanged: exporting =>
            {
                if (!exporting)
                    _closing.Observe();
            },
            back: () =>
            {
                DetailsOpen = false;
                _focus.Request(this);
            },
            canClose: () => CanClose,
            showCloseButton: false);
    }

    private partial DiagnosticsExportState State { get; set; }
    private partial bool Picking { get; set; }
    private partial bool DetailsOpen { get; set; }
    internal bool CanClose => !Picking && !State.Exporting && !_report.Exporting;
    private string Title => State.Launcher ? Resources.LauncherLogsTitle : Resources.GameLogsTitle;
    private bool ShowPrimary => !State.Completed && (State.Launcher || State.Sessions.Length > 0 || State.Error.Length > 0);
    private string Primary => !State.Launcher && State.Sessions.Length == 0 ? Resources.DiagnosticsRetry : Resources.DiagnosticsExport;
    private bool CanConfirm => !Picking && !State.Reading && !State.Exporting && (!State.Launcher && State.Sessions.Length == 0 ? State.Error.Length > 0 : State.CanExport);

    private Ui Body => Overlay([
        _report.Visible(DetailsOpen),
        Grid(
            [GridLength.Star()],
            [GridLength.Auto, GridLength.Auto, GridLength.Auto],
            [
                Grid(
                    [GridLength.Star(), GridLength.Auto],
                    [GridLength.Auto],
                    [
                        Text(Title)
                            .FontSize(20f).FontWeight(700).Foreground(LauncherTheme.Ink)
                            .VAlign(VerticalAlignment.Center)
                            .Cell(0, 0),
                        LauncherControls.OutlineCommand(Resources.ContentOperationDetails, () => ShowDetails())
                            .MinHeight(36f)
                            .Visible(State.Error.Length > 0)
                            .Enabled(!State.Exporting && !Picking)
                            .Automation(new()
                            {
                                Name = Resources.ContentOperationDetails,
                                AutomationId = "diagnostics.details"
                            })
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(12f)
                    .Padding(24f, 20f, 24f, 16f)
                    .Cell(0, 0),
                ScrollView(new DiagnosticsExportBody(
LauncherAppearance.Capture(),                        State,
                        Picking,
                        _sessions,
                        Math.Min(State.Sessions.Length * 64f, Math.Clamp(_window.ClientSize.Height * .25f, 120f, 200f)),
                        index => _controller.SetMode(index),
                        index => _controller.SelectInstance(index),
                        value => _controller.SetDestination(value),
                        () => _ = BrowseAsync()))
                    .MaxHeight(Math.Max(100f, _window.ClientSize.Height - 210f))
                    .Automation(new()
                    {
                        Name = Title,
                        AutomationId = "diagnostics.scroll"
                    })
                    .Cell(0, 1),
                Border(Column([
                        Column([
                            Text(Resources.DiagnosticsExporting)
                                .FontSize(12f).Foreground(LauncherTheme.Muted)
                                .Automation(new()
                                {
                                    Name = Resources.DiagnosticsExporting,
                                    AutomationId = "diagnostics.progress",
                                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                                }),
                            ProgressBar()
                                .Indeterminate()
                                .Thickness(3f),
                        ])
                            .Spacing(8f)
                            .Margin(new Thickness(0f, 0f, 0f, 12f))
                            .Visible(State.Exporting),
                        Grid(
                            [GridLength.Star(), GridLength.Star()],
                            [GridLength.Auto],
                            [
                                LauncherControls.Command(Primary, () => _ = ConfirmAsync())
                                    .Style(LauncherButtons.DialogPrimary)
                                    .MinHeight(40f)
                                    .Visible(ShowPrimary)
                                    .Enabled(CanConfirm)
                                    .Automation(new()
                                    {
                                        Name = Primary,
                                        AutomationId = "diagnostics.confirm"
                                    })
                                    .Cell(0, 0),
                                LauncherControls.OutlineCommand(ShowPrimary || State.Reading || State.Exporting ? Resources.DialogCancel : Resources.ContentUpdatesClose, () => Cancel())
                                    .Style(LauncherButtons.DialogSecondary)
                                    .MinHeight(40f)
                                    .Enabled(!Picking && !_report.Exporting)
                                    .Automation(new()
                                    {
                                        Name = ShowPrimary || State.Reading || State.Exporting ? Resources.DialogCancel : Resources.ContentUpdatesClose,
                                        AutomationId = "diagnostics.cancel"
                                    })
                                    .Cell(1, 0),
                            ])
                            .ColumnSpacing(12f),
                    ]))
                    .Padding(24f, 16f, 24f, 20f)
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(0f, 1f, 0f, 0f)
                    .Cell(0, 2),
            ])
            .Width(Math.Min(680f, _window.ClientSize.Width - 48f))
            .Visible(!DetailsOpen),
    ]);

    private void Cancel()
    {
        if (State.Exporting)
            _controller.Cancel();
        else if (!Picking)
            _close();
    }

    private Task ConfirmAsync() => !State.Launcher && State.Sessions.Length == 0 ? _controller.LoadAsync() : _controller.ExportAsync();

    private async Task BrowseAsync()
    {
        if (!State.Editable || Picking)
            return;
        var returnFocus = _window.FocusedElement;
        Picking = true;
        try
        {
            var picker = _window.CreateFileSavePicker();
            picker.SuggestedFileName = (State.Launcher ? "launcher-logs-" : State.Bundle ? "game-diagnostics-" : "game-logs-") + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string[] extensions = [".zip"];
            picker.FileTypeChoices.Add(Resources.DiagnosticsZipFiles, extensions);
            if (await picker.PickSaveFileAsync() is { } file)
                _controller.SetDestination(file.Path, replacementApproved: true);
        }
        catch (Exception exception)
        {
            _controller.ReportError(exception);
        }
        finally
        {
            Picking = false;
            _focus.Request(this, returnFocus);
        }
    }

    private void ShowDetails()
    {
        _report.ViewHeight = Render.Bounds.Height;
        _report.ViewWidth = Render.Bounds.Width;
        _report.SetReport(State.Error);
        DetailsOpen = true;
        _focus.Request(_report);
    }

    private void OnChanged(DiagnosticsExportState state)
    {
        if (_closing.IsClosing)
            return;
        var completed = !State.Completed && state.Completed;
        State = state;
        if (_closing.Observe(completed && state.SourceWarnings.Count == 0))
        {
            if (completed && state.SourceWarnings.Count == 0)
                Controls.LauncherToast.Show(_window, state.Outcome);
            return;
        }

        _focus.RequestIfLost(this);
    }

    internal void RequestClose()
    {
        if (_closing.Request())
            _controller.Cancel();
    }

    protected override IDisposable? OnMounted()
    {
        _controller.Changed += OnChanged;
        _ = _controller.LoadAsync();
        return PopupKeyboardClose.Bind(_window, RequestClose, () => !Picking && !_report.Exporting);
    }

    protected override void OnDispose()
    {
        _closing.Dispose();
        _controller.Changed -= OnChanged;
        _controller.Dispose();
        _focus.Dispose();
        base.OnDispose();
    }
}

[View]
internal readonly partial record struct DiagnosticsExportBody(
    LauncherAppearance Theme,
    DiagnosticsExportState State,
    bool Picking,
    DiagnosticsSessionList Sessions,
    float ListHeight,
    Action<int> ChooseMode,
    Action<int> ChooseInstance,
    Action<string> ChangeDestination,
    Action Browse)
{
    internal string ErrorText => State.ErrorCode == "EXPORT_DESTINATION_EXISTS" ? Resources.DiagnosticsDestinationExists : Resources.DiagnosticsFailure;
    internal string Selected => State.Session?.Label ?? string.Empty;
    internal string Completed => string.Format(Resources.DiagnosticsCompleted, State.Launcher ? Resources.LauncherLogsTitle : State.Bundle ? Resources.DiagnosticsBundle : Resources.DiagnosticsRawLogs, AppImageCache.FormatSize(State.Bytes));
    internal string SourceNotice => State.SourceWarnings.Count == 1 && State.SourceWarnings[0] == "LAUNCHER_LOGS_EMPTY"
        ? Resources.LauncherLogsEmptyHelp : Resources.LauncherLogsPartialHelp;

    /// <summary>Shows launcher export directly, or a separate instance and game-run selection flow.</summary>
    public Ui Body => Column([
        Text(State.Launcher ? Resources.LauncherLogsHelp : Resources.GameLogsHelp)
            .FontSize(12f).Foreground(Theme.Muted)
            .Wrap()
            .Margin(new Thickness(0f, 0f, 0f, 16f))
            .Visible(!State.Completed),
        Text(Resources.DiagnosticsLoading)
            .FontSize(12f).Foreground(Theme.Muted)
            .Wrap()
            .Visible(State.Reading),
        Text(Resources.DiagnosticsEmpty)
            .FontSize(14f).Foreground(Theme.Muted)
            .Wrap()
            .Visible(!State.Launcher && !State.Reading && State.Sessions.Length == 0 && State.Error.Length == 0),
        Column([
            Text(Resources.DiagnosticsSelectInstance)
                .FontSize(15f).Foreground(Theme.Ink),
            LauncherControls.Choice(Theme,[.. State.Instances.Select(instance => instance.Name)])
                .SelectedIndex(State.SelectedInstance)
                .OnSelectionChanged(ChooseInstance)
                .MinHeight(40f)
                .Enabled(State.Editable && !Picking)
                .Automation(new()
                {
                    Name = Resources.DiagnosticsSelectInstance,
                    AutomationId = "diagnostics.instance"
                }),
            Column([
                Text(Resources.DiagnosticsSelectSession)
                    .FontSize(15f).Foreground(Theme.Ink),
                Sessions.WithState(State)
                    .Height(ListHeight),
            ])
                .Spacing(8f),
            Text(Resources.DiagnosticsUnreadableHelp)
                .FontSize(12f).Foreground(Theme.Warning)
                .Wrap()
                .Margin(new Thickness(0f, 8f, 0f, 0f))
                .Visible(State.Session is { Readable: false }),
        ])
            .Spacing(8f)
            .Margin(new Thickness(0f, 0f, 0f, 16f))
            .Visible(!State.Launcher && State.Sessions.Length > 0 && !State.Completed),
        Column([
            Text(Resources.DiagnosticsExportType)
                .FontSize(15f).Foreground(Theme.Ink),
            LauncherControls.Choice(Theme,[Resources.DiagnosticsBundle, Resources.DiagnosticsRawLogs])
                .SelectedIndex(State.Bundle ? 0 : 1)
                .OnSelectionChanged(ChooseMode)
                .MinHeight(40f)
                .Enabled(State.Editable && !Picking)
                .Automation(new()
                {
                    Name = Resources.DiagnosticsExportType,
                    AutomationId = "diagnostics.type"
                }),
            Text(State.Bundle ? Resources.DiagnosticsBundleHelp : Resources.DiagnosticsRawHelp)
                .FontSize(12f).Foreground(Theme.Muted)
                .Wrap(),
        ])
            .Spacing(8f)
            .Margin(new Thickness(0f, 0f, 0f, 16f))
            .Visible(!State.Launcher && State.Sessions.Length > 0 && !State.Completed),
        Column([
            Text(Completed)
                .FontSize(15f).Foreground(Theme.Ink)
                .Wrap()
                .Automation(new()
                {
                    Name = Completed,
                    AutomationId = "diagnostics.completed",
                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                }),
            Text(Selected)
                .FontSize(12f).Foreground(Theme.Muted)
                .Wrap()
                .Visible(!State.Launcher),
            Text(SourceNotice)
                .FontSize(12f).Foreground(Theme.Muted)
                .Wrap()
                .Visible(State.SourceWarnings.Count > 0),
        ])
            .Spacing(8f)
            .Margin(new Thickness(0f, 0f, 0f, 16f))
            .Visible(State.Completed),
        Column([
            Text(Resources.DiagnosticsDestination)
                .FontSize(15f).Foreground(Theme.Ink),
            Grid(
                [GridLength.Star(), GridLength.Auto],
                [GridLength.Auto],
                [
                    TextBox()
                        .Text(State.Destination)
                        .Placeholder(Resources.PathSavePlaceholder)
                        .Style(LauncherStyles.Input)
                        .MinHeight(40f)
                        .ReadOnly()
                        .Enabled(!State.Reading && !State.Exporting && !Picking)
                        .Automation(new()
                        {
                            Name = Resources.DiagnosticsDestination,
                            AutomationId = "diagnostics.destination",
                            HelpText = State.Destination
                        })
                        .Cell(0, 0),
                    LauncherControls.OutlineCommand(
Theme,                        Row([
                            LauncherIcons.Line("folder", 18f)
                        ]),
                        Browse)
                        .Style(Theme.Buttons.BrowseButton)
                        .Height(40f).Width(40f)
                        .Visible(!State.Completed)
                        .Enabled(State.Editable && !Picking)
                        .ToolTip(Resources.DiagnosticsChooseDestination)
                        .Automation(new()
                        {
                            Name = Resources.DiagnosticsChooseDestination,
                            AutomationId = "diagnostics.browse"
                        })
                        .Cell(1, 0),
                ])
                .ColumnSpacing(8f),
        ])
            .Spacing(8f)
            .Visible(State.Launcher || State.Sessions.Length > 0),
        Text(ErrorText)
            .FontSize(12f).Foreground(Theme.Error)
            .Wrap()
            .Margin(new Thickness(0f, 10f, 0f, 0f))
            .Visible(State.Error.Length > 0)
            .Automation(new()
            {
                Name = ErrorText,
                AutomationId = "diagnostics.error"
            }),
    ])
        .Padding(24f, 0f, 24f, 20f);
}