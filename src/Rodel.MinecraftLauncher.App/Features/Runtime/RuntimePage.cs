// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Runtime;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Runtime;

internal sealed partial class RuntimePage : Control
{
    private readonly RuntimeController _controller;
    private readonly Window _window;
    private readonly Action _back;
    private Flyout? _report;
    internal readonly Func<RuntimeOperationSummary, RuntimeHistoryRow> HistoryFactory;

    internal RuntimePage(
        LauncherClient client,
        HomeController home,
        Window window,
        Action back)
    {
        _controller = new(client, home);
        _window = window;
        _back = back;
        State = _controller.State;
        _controller.Changed += OnChanged;
        HistoryFactory = operation => new(LauncherAppearance.Capture(), operation, this);
        ViewportWidth = window.ClientSize.Width;
    }

    internal partial RuntimeState State { get; private set; }
    private partial float ViewportWidth { get; set; }
    private bool Compact => ViewportWidth < 1100f;

    private void OnChanged(RuntimeState state) => State = state;

    internal Task OpenAsync(string id) => _controller.OpenAsync(id);
    internal bool Editable => !State.Active && !State.Loading;

    internal void Rollback(string id) => _ = _controller.RollbackAsync(id);

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Star(), GridLength.Auto],
        [
            Row([
                LauncherControls.OutlineCommand(Resources.BackToOperation, _back)
                    .MinHeight(40f)
                    .Enabled(!State.Active)
                    .Automation(new()
                    {
                        Name = Resources.BackToOperation,
                        AutomationId = "runtime.back"
                    }),
                Text(Resources.RuntimeHeading)
                    .FontSize(28f).FontWeight(700).Foreground(LauncherTheme.Ink)
                    .Weight(1f),
                Text(State.Name)
                    .FontSize(13f).Foreground(LauncherTheme.Muted)
                    .Wrap(),
            ])
                .Spacing(14f)
                .Cell(0, 0),
            Overlay([
                new Controls.LauncherMaterialSurface(LauncherMaterials.Acrylic).Margin(1f),
                Border(ScrollView(new RuntimeFields(LauncherAppearance.Capture(),State, _controller, this, Compact)))
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(1f),
            ]).Cell(0, 1),
            Column([
                Overlay([
                    new Controls.LauncherMaterialSurface(LauncherMaterials.Acrylic).Margin(1f),
                    Border(Text(string.Format(Resources.RuntimePlanSummary, State.Plan?.PreviousVersion, State.Plan?.TargetVersion ?? State.Plan?.MinecraftVersion))
                        .FontSize(15f).Foreground(LauncherTheme.Ink)
                        .Wrap()
                        .Automation(new()
                        {
                            Name = Resources.RuntimeReview,
                            AutomationId = "runtime.plan"
                        }))
                    .Padding(14f)
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(1f),
                ]).Visible(State.Plan is not null),
                ProgressBar()
                    .Indeterminate(State.Percent is null)
                    .Value(State.Percent ?? 0d)
                    .Thickness(3f)
                    .Visible(State.Loading || State.Active),
                Text(State.TaskStatus)
                    .FontSize(13f).Foreground(LauncherTheme.Ink)
                    .Wrap()
                    .Visible(State.TaskStatus.Length > 0)
                    .Automation(new()
                    {
                        Name = State.TaskStatus,
                        AutomationId = "runtime.status",
                        LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                    }),
                Text(State.TaskProgress)
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Wrap()
                    .Visible(State.Active && State.TaskProgress.Length > 0),
                Text(State.Error)
                    .FontSize(12f).Foreground(LauncherTheme.Error)
                    .Wrap()
                    .Visible(State.Error.Length > 0)
                    .Automation(new()
                    {
                        Name = State.Error,
                        AutomationId = "runtime.error",
                        LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                    }),
                Row([
                    LauncherControls.Command(State.Plan is null ? Resources.RuntimeReview : Resources.RuntimeApply, () =>
                        {
                            if (State.Plan is null)
                                _ = _controller.ReviewAsync();
                            else
                                _ = _controller.ApplyAsync();
                        })
                        .Style(LauncherButtons.DialogPrimary)
                        .MinHeight(40f)
                        .Enabled(State.CanReview)
                        .Automation(new()
                        {
                            Name = State.Plan is null ? Resources.RuntimeReview : Resources.RuntimeApply,
                            AutomationId = "runtime.apply"
                        }),
                    LauncherControls.OutlineCommand(Resources.FeatureTaskCancel, () => _ = _controller.CancelAsync())
                        .MinHeight(40f)
                        .Visible(State.Active)
                        .Automation(new()
                        {
                            Name = Resources.FeatureTaskCancel,
                            AutomationId = "runtime.cancel"
                        }),
                    LauncherControls.OutlineCommand(Resources.FeatureTaskDetails, () => ShowReport())
                        .MinHeight(40f)
                        .Visible(State.TaskReport.Length > 0)
                        .Automation(new()
                        {
                            Name = Resources.FeatureTaskDetails,
                            AutomationId = "runtime.details"
                        }),
                ])
                    .Spacing(12f),
            ])
                .Spacing(10f)
                .Cell(0, 2),
        ])
        .RowSpacing(14f);

    private void ShowReport()
    {
        if (Controls.ModalPopup.IsOpen(_window))
            return;
        _report = new Flyout(close =>
            {
                var report = new DetailReportPopup(
                    _window,
                    Resources.FeatureTaskDetails,
                    State.TaskStatus + "\n" + State.Error,
                    close,
                    Math.Min(680f, _window.ClientSize.Width - 96f),
                    "runtime.report");
                report.SetReport(State.TaskStatus + "\n" + State.Error, State.TaskReport);
                return report;
            })
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup);
        Controls.ModalPopup.TryShow(_window, _report);
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs args) => ViewportWidth = args.Size.Width;

    protected override IDisposable? OnMounted()
    {
        _window.SizeChanged += OnSizeChanged;
        return null;
    }

    protected override void OnDispose()
    {
        _window.SizeChanged -= OnSizeChanged;
        _controller.Changed -= OnChanged;
        _controller.Dispose();
        _report?.Hide();
        base.OnDispose();
    }
}