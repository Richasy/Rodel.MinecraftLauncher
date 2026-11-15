// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Imaging;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using System.Text.Json;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed partial class CacheCleanupDialog : Control
{
    private readonly CacheCleanupController _controller;
    private readonly Window _window;
    private readonly Action _close;
    private readonly PopupPreparationClose _closing;
    private readonly PopupFocusRecovery _focus;
    private readonly Func<CacheCleanupEntry, CacheCleanupEntryView> _factory;
    private readonly DetailReportPopup _report;
    private bool _finished;

    internal CacheCleanupDialog(
        LauncherClient client,
        HomeController home,
        Window window,
        Action close)
    {
        _controller = new(client, home);
        _window = window;
        _close = close;
        _closing = new(close, () => State.Busy, () => CanClose, () => false);
        _focus = new(window);
        State = _controller.State;
        _factory = item => new(LauncherAppearance.Capture(), item, window.FlowDirection == FlowDirection.RightToLeft);
        _report = new(
            window,
            Resources.ContentOperationDetails,
            string.Empty,
            _close,
            Math.Min(580f, window.ClientSize.Width - 72f),
            "settings.cleanup.report",
            exportStateChanged: exporting =>
            {
                if (!exporting)
                    FinishIfSucceeded();
            },
            back: () =>
            {
                DetailsOpen = false;
                _focus.Request(this);
            },
            canClose: () => CanClose,
            showCloseButton: false);
    }

    private partial CacheCleanupState State { get; set; }
    private partial bool DetailsOpen { get; set; }
    internal bool CanClose => !State.Busy && !_report.Exporting;
    private bool HasDetails => State.Error.Length > 0 || State.Plan?.Warnings.Count > 0;
    private bool ShowPrimary => !State.Completed && (State.Plan is null || State.CandidateCount > 0);
    private string Primary => State.Plan is null ? Resources.CacheCleanupAnalyze : Resources.CacheCleanupApply;

    private Ui Body => Overlay([
        _report.Visible(DetailsOpen),
        Grid(
            [GridLength.Star()],
            [GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto],
            [
                Grid(
                    [GridLength.Star(), GridLength.Auto],
                    [GridLength.Auto],
                    [
                        Text(Resources.CacheCleanupTitle)
                            .FontSize(20f).FontWeight(700).Foreground(LauncherTheme.Ink)
                            .VAlign(VerticalAlignment.Center)
                            .Cell(0, 0),
                        LauncherControls.OutlineCommand(Resources.ContentOperationDetails, () => ShowDetails())
                            .MinHeight(36f)
                            .Visible(HasDetails)
                            .Enabled(!State.Busy)
                            .Automation(new()
                            {
                                Name = Resources.ContentOperationDetails,
                                AutomationId = "settings.cleanup.details"
                            })
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(12f)
                    .Margin(new Thickness(24f, 20f, 24f, 16f))
                    .Cell(0, 0),
                Grid(
                    [GridLength.Star(), GridLength.Pixels(140f)],
                    [GridLength.Auto],
                    [
                        Column([
                            Text(Resources.CacheCleanupAge)
                                .FontSize(15f).Foreground(LauncherTheme.Ink)
                                .Wrap(),
                            Text(Resources.CacheCleanupAgeHelp)
                                .FontSize(12f).Foreground(LauncherTheme.Muted)
                                .Wrap(),
                        ])
                            .Spacing(6f)
                            .VAlign(VerticalAlignment.Center)
                            .Cell(0, 0),
                        new LauncherNumberBox().Value(State.Days)
                            .Minimum(0d).Maximum(int.MaxValue)
                            .ValidationMode(NumberBoxValidationMode.Disabled)
                            .AcceptsOnlyNumericInput()
                            .Style(LauncherStyles.NumberInput)
                            .SpinButtonPlacement(NumberBoxSpinButtonPlacementMode.Inline)
                            .SmallChange(1d).LargeChange(10d)
                            .OnValueChanged(value => _controller.SetDays(value.NewValue))
                            .Enabled(!State.Busy && !State.Completed)
                            .VAlign(VerticalAlignment.Center)
                            .Automation(new()
                            {
                                Name = Resources.CacheCleanupAge,
                                AutomationId = "settings.cleanup.days"
                            })
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(16f)
                    .Padding(24f, 0f, 24f, 12f)
                    .Cell(0, 1),
                ScrollView(new CacheCleanupBody(LauncherAppearance.Capture(),State, _controller.Entries(State), _factory))
                    .MaxHeight(Math.Max(90f, _window.ClientSize.Height - 300f))
                    .Automation(new()
                    {
                        Name = Resources.CacheCleanupTitle,
                        AutomationId = "settings.cleanup.scroll"
                    })
                    .Cell(0, 2),
                Border(Column([
                        Column([
                            Text(State.Executing ? Resources.CacheCleanupApplying : Resources.CacheCleanupAnalyzing)
                                .FontSize(12f).Foreground(LauncherTheme.Muted)
                                .Wrap()
                                .Automation(new()
                                {
                                    Name = State.Executing ? Resources.CacheCleanupApplying : Resources.CacheCleanupAnalyzing,
                                    AutomationId = "settings.cleanup.progress",
                                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                                }),
                            ProgressBar()
                                .Indeterminate()
                                .Thickness(3f),
                        ])
                            .Spacing(8f)
                            .Margin(new Thickness(0f, 0f, 0f, 12f))
                            .Visible(State.Busy),
                        Grid(
                            [GridLength.Star(), GridLength.Star()],
                            [GridLength.Auto],
                            [
                                LauncherControls.Command(Primary, () => _ = _controller.RunAsync(State.Plan is not null))
                                    .Style(LauncherButtons.DialogPrimary)
                                    .MinHeight(40f)
                                    .Enabled(!State.Busy && State.ValidAge)
                                    .Visible(ShowPrimary)
                                    .Automation(new()
                                    {
                                        Name = Primary,
                                        AutomationId = "settings.cleanup.confirm"
                                    })
                                    .Cell(0, 0),
                                LauncherControls.OutlineCommand(State.Busy || ShowPrimary ? Resources.DialogCancel : Resources.ContentUpdatesClose, () => Cancel())
                                    .Style(LauncherButtons.DialogSecondary)
                                    .MinHeight(40f)
                                    .Automation(new()
                                    {
                                        Name = State.Busy || ShowPrimary ? Resources.DialogCancel : Resources.ContentUpdatesClose,
                                        AutomationId = "settings.cleanup.cancel"
                                    })
                                    .Cell(1, 0),
                            ])
                            .ColumnSpacing(12f),
                    ]))
                    .Padding(24f, 16f, 24f, 20f)
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(0f, 1f, 0f, 0f)
                    .Cell(0, 3),
            ])
            .Width(Math.Min(680f, _window.ClientSize.Width - 48f))
            .Visible(!DetailsOpen),
    ]);

    private void ShowDetails()
    {
        _report.ViewHeight = Render.Bounds.Height;
        _report.ViewWidth = Render.Bounds.Width;
        var summary = State.Error.Length > 0
            ? State.Error
            : State.Completed ? State.Outcome : string.Format(Resources.CacheCleanupEstimate, State.CandidateCount, AppImageCache.FormatSize(State.CandidateBytes));
        var entries = _controller.Entries(State).Select(entry => entry.Name + Environment.NewLine + entry.Reason);
        _report.SetReport(string.Join(Environment.NewLine + Environment.NewLine, new[] { summary }.Concat(entries)), State.Plan is null ? State.Error : JsonSerializer.SerializeToNode(State.Plan, ApplicationJsonContext.Default.CachePruneResult)!.ToJsonString(new() { WriteIndented = true }));
        DetailsOpen = true;
        _focus.Request(_report);
    }

    private void Cancel()
    {
        if (State.Busy)
            _controller.Cancel();
        else
            _close();
    }

    private void OnChanged(CacheCleanupState state)
    {
        if (_closing.IsClosing || _finished)
            return;
        State = state;
        if (_closing.Observe())
            return;
        if (FinishIfSucceeded())
            return;
        _focus.RequestIfLost(this);
    }

    internal void RequestClose()
    {
        if (_closing.Request())
            _controller.Cancel();
    }

    private bool FinishIfSucceeded()
    {
        if (_closing.IsClosing)
            return true;
        if (_finished || !State.Completed || !CanClose || State.Error.Length > 0 || State.Plan is not { CacheBusy: false, Warnings.Count: 0 })
            return _finished;
        _finished = true;
        _close();
        Controls.LauncherToast.Show(_window, State.Outcome);
        return true;
    }

    protected override IDisposable? OnMounted()
    {
        _controller.Changed += OnChanged;
        _ = _controller.RunAsync(false);
        return PopupKeyboardClose.Bind(_window, RequestClose, () => !_report.Exporting);
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
internal readonly partial record struct CacheCleanupBody(LauncherAppearance Theme, CacheCleanupState State, CacheCleanupEntry[] Entries, Func<CacheCleanupEntry, CacheCleanupEntryView> Factory)
{
    internal string Summary => string.Format(Resources.CacheCleanupEstimate, State.CandidateCount, AppImageCache.FormatSize(State.CandidateBytes));

    public Ui Body => Column([
        Text(Resources.CacheCleanupExplanation)
            .FontSize(12f).Foreground(Theme.Muted)
            .Wrap()
            .Margin(new Thickness(0f, 0f, 0f, 10f)),
        Text(Summary)
            .FontSize(15f).Foreground(Theme.Ink)
            .Wrap()
            .Margin(new Thickness(0f, 0f, 0f, 10f))
            .Visible(State.Plan is not null && !State.Completed)
            .Automation(new()
            {
                Name = Summary,
                AutomationId = "settings.cleanup.estimate"
            }),
        Text(Resources.CacheCleanupEmpty)
            .FontSize(14f).Foreground(Theme.Muted)
            .Wrap()
            .Margin(new Thickness(0f, 0f, 0f, 10f))
            .Visible(State.Plan is not null && State.CandidateCount == 0 && !State.Completed),
        Text(State.Outcome)
            .FontSize(14f).Foreground(Theme.Ink)
            .Wrap()
            .Margin(new Thickness(0f, 0f, 0f, 10f))
            .Visible(State.Outcome.Length > 0)
            .Automation(new()
            {
                Name = State.Outcome,
                AutomationId = "settings.cleanup.outcome",
                LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
            }),
        Text(Resources.CacheCleanupFailure)
            .FontSize(12f).Foreground(Theme.Error)
            .Wrap()
            .Margin(new Thickness(0f, 0f, 0f, 10f))
            .Visible(State.Error.Length > 0)
            .Automation(new()
            {
                Name = Resources.CacheCleanupFailure,
                AutomationId = "settings.cleanup.error"
            }),
        Text(Resources.CacheCleanupInvalidAge)
            .FontSize(12f).Foreground(Theme.Error)
            .Wrap()
            .Margin(new Thickness(0f, 0f, 0f, 10f))
            .Visible(!State.ValidAge),
        ItemsRepeater(Entries, static entry => entry.Key, Factory, new StackLayout { EstimatedItemExtent = 66f }).KeyboardNavigation(singleTabStop: false),
    ])
        .Padding(24f, 0f, 24f, 16f);
}