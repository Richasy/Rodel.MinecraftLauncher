// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Packs;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.WindowsAppSDK;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Packs;

internal sealed partial class PackMaintenanceDialog : Control
{
    private readonly PackMaintenanceController _controller;
    private readonly Window _window;
    private readonly Action _close;
    private readonly Action? _connect;
    private bool _connectionRequested;
    private bool _automaticConnectionStopped;
    private readonly DetailReportPopup _report;
    private readonly PopupFocusRecovery _focus;
    private bool _closeWhenIdle;
    private bool _finished;
    private bool? _submittedMutation;
    private string? _rollbackId;
    private MenuFlyout? _menu;

    internal PackMaintenanceDialog(
        PackMaintenanceController controller,
        Window window,
        Action close,
        Action? connect = null)
    {
        _controller = controller;
        _window = window;
        _close = close;
        State = controller.State;
        _connect = connect;
        _focus = new(window);
        _report = new(
            window,
            Resources.FeatureTaskDetails,
            string.Empty,
            RequestClose,
            ContentWidth,
            "pack.update.report",
            back: () =>
            {
                Details = false;
                _focus.Request(this);
            },
            canClose: () => CanClose,
            showCloseButton: false);
        Confirmation = string.Empty;
        Viewport = window.ClientSize;
    }

    internal partial PackMaintenanceState State { get; private set; }
    private partial Size Viewport { get; set; }
    private partial bool Picking { get; set; }
    private partial bool Details { get; set; }
    private partial string Confirmation { get; set; }
    internal bool CanClose => !_controller.State.Active && !Picking && !_report.Exporting;
    internal const float MaximumWidth = 560f;
    private float ContentWidth => Math.Min(MaximumWidth, Math.Max(0f, Viewport.Width - 48f));
    private static GridLength[] Rows => [GridLength.Auto, GridLength.Auto, GridLength.Auto];
    private string Primary => Confirmation == "rollback"
        ? Resources.PackUpdateConfirmRollback
        : Confirmation == "detach"
        ? Resources.PackUpdateConfirmDetach
        : ContinueReady
        ? Resources.PackUpdateConnectCurrent
        : State.CanApply
        ? Resources.PackUpdateApply
        : State.UseSource ? Resources.PackUpdateCheck : Resources.PackUpdateReview;
    private bool CanSubmit => !State.Active && !Picking && (Confirmation.Length > 0 || ContinueReady || State.CanApply || State.CanReview);
    private bool ContinueReady => _connect is not null && !State.Active && (State.Error.Length > 0 || State.Review?.Phase == "applied" || State.Review is { Phase: "reviewed", HasChanges: false } || !State.HasSource);
    private string Secondary => State.Active
        ? Resources.FeatureTaskCancel
        : Confirmation.Length > 0 ? Resources.DialogCancel : Resources.CloseLaunchSettings;
    private PackChangeRow[] Changes
    {
        get
        {
            var rows = new List<PackChangeRow>();
            if (State.Review?.ChangeSummary is { } summary)
            {
                foreach (var mod in summary.Mods)
                {
                    rows.Add(new(
                            rows.Count,
                            mod.AfterPath ?? mod.BeforePath!,
                            mod.Action,
                            mod.Name,
                            mod.BeforeVersion ?? string.Empty,
                            mod.AfterVersion ?? string.Empty,
                            mod.Id));
                }

                foreach (var file in summary.Files)
                    rows.Add(new(rows.Count, file.Path, file.Action));
            }
            else
            {
                foreach (var action in (State.Review?.Actions ?? []).Where(action => action.Action != "unchanged"))
                    rows.Add(new(rows.Count, action.Path, action.Action));
            }

            return [.. rows];
        }
    }

    private string ChangeSummary => string.Format(
        Resources.PackUpdateChangeSummary,
        Changes.Count(row => row.IsMod && row.Action == "update"),
        Changes.Count(row => row.IsMod && row.Action == "add"),
        Changes.Count(row => row.IsMod && row.Action == "delete"),
        Changes.Count(row => !row.IsMod));

    private Ui Body => Overlay([
        _report.Visible(Details),
        Grid(
            [GridLength.Star()],
            Rows,
            [
                Border(Grid(
                        [GridLength.Star(), GridLength.Auto, GridLength.Auto],
                        [GridLength.Auto],
                        [
                            Column([
                                Text(Resources.PackUpdateHeading)
                                    .FontSize(20f).FontWeight(600).Foreground(LauncherTheme.Ink),
                                Text(State.Name)
                                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                                    .NoWrap()
                                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
                            ])
                                .Spacing(4f)
                                .Cell(0, 0),
                            LauncherControls.OutlineCommand(Resources.FeatureTaskDetails, () => ShowDetails())
                                .Style(LauncherButtons.DialogHeaderButton)
                                .Enabled(!Picking)
                                .Visible(State.Report.Length > 0 || State.Review is not null || State.ReadWarnings > 0)
                                .Automation(new()
                                {
                                    Name = Resources.FeatureTaskDetails,
                                    AutomationId = "pack.update.details"
                                })
                                .Cell(1, 0),
                            LauncherControls.OutlineCommand(
                                Row([
                                    LauncherIcons.Line("more", 18f)
                                ]),
                                () => ShowMenu())
                                .Style(LauncherButtons.FlatIconButton)
                                .Width(32f).Height(32f)
                                .Enabled(CanClose)
                                .ToolTip(Resources.PackUpdateMore)
                                .Automation(new()
                                {
                                    Name = Resources.PackUpdateMore,
                                    AutomationId = "pack.update.more"
                                })
                                .Cell(2, 0),
                        ])
                        .ColumnSpacing(10f))
                    .Padding(24f, 20f, 24f, 16f)
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(0f, 0f, 0f, 1f)
                    .Cell(0, 0),
                ScrollView(new PackMaintenanceBody(
LauncherAppearance.Capture(),                        State,
                        Confirmation,
                        Changes,
                        _controller,
                        () => _ = BrowseAsync(false),
                        () => _ = BrowseAsync(true),
                        CanClose,
                        Changes.Length > 0,
                        ChangeSummary))
                    .MaxHeight(Math.Max(80f, Viewport.Height - 220f))
                    .Automation(new()
                    {
                        Name = Resources.PackUpdateHeading,
                        AutomationId = "pack.update.body"
                    })
                    .Cell(0, 1),
                Border(Grid(
                        [GridLength.Star(), GridLength.Star()],
                        [GridLength.Auto],
                        [
                            LauncherControls.Command(Primary, () => _ = SubmitAsync())
                                .Style(LauncherButtons.DialogPrimary)
                                .Enabled(CanSubmit)
                                .Visible(Confirmation.Length > 0 || ContinueReady || State.CanReview || State.CanApply)
                                .Automation(new()
                                {
                                    Name = Primary,
                                    AutomationId = "pack.update.confirm"
                                })
                                .Cell(0, 0),
                            LauncherControls.OutlineCommand(Secondary, () => Dismiss())
                                .Style(LauncherButtons.DialogSecondary)
                                .Enabled(!Picking && !_report.Exporting && !State.CancelRequested)
                                .Automation(new()
                                {
                                    Name = Secondary,
                                    AutomationId = "pack.update.dismiss"
                                })
                                .Cell(1, 0),
                        ])
                        .ColumnSpacing(12f))
                    .Padding(24f, 16f, 24f, 20f)
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(0f, 1f, 0f, 0f)
                    .Cell(0, 2),
            ])
            .Visible(!Details),
    ])
        .Width(ContentWidth);

    internal void RequestClose()
    {
        if (Picking || _report.Exporting || _closeWhenIdle)
            return;
        if (State.Active)
        {
            _closeWhenIdle = true;
            _automaticConnectionStopped = true;
            _ = _controller.CancelAsync();
        }
        else
        {
            _close();
        }
    }

    private void Dismiss()
    {
        if (State.Active)
        {
            _automaticConnectionStopped = true;
            _ = _controller.CancelAsync();
        }
        else if (Confirmation.Length > 0)
        {
            Confirmation = string.Empty;
            _focus.Request(this);
        }
        else
        {
            RequestClose();
        }
    }

    private async Task SubmitAsync()
    {
        if (_finished || !CanSubmit)
            return;
        _submittedMutation = Confirmation is "rollback" or "detach" || State.CanApply;
        try
        {
            if (Confirmation == "rollback")
            {
                var id = _rollbackId;
                Confirmation = string.Empty;
                if (id is not null)
                    await _controller.RollbackAsync(id);
            }
            else if (Confirmation == "detach")
            {
                Confirmation = string.Empty;
                await _controller.DetachAsync();
            }
            else if (ContinueReady)
            {
                Connect();
            }
            else if (State.CanApply)
            {
                await _controller.ApplyAsync();
            }
            else
            {
                await ReviewAsync();
            }

            FinishIfSucceeded(_controller.State);
        }
        finally
        {
            _submittedMutation = null;
        }
    }

    private bool FinishIfSucceeded(PackMaintenanceState state)
    {
        if (_finished)
            return true;
        var nothingToApply = state.Review is { Phase: "reviewed", HasChanges: false } && state.MissingCount == 0 && state.ReadWarnings == 0;
        if (_submittedMutation is not { } mutation || _connect is not null || !_controller.Succeeded || !CanClose || (!mutation && !nothingToApply))
            return false;
        _finished = true;
        _close();
        Controls.LauncherToast.Show(_window, Resources.FeatureTaskSucceeded);
        return true;
    }

    private void ShowMenu()
    {
        var entries = new List<MenuFlyoutItemBase>
        {
            new MenuFlyoutItem(Resources.PackUpdateRollback, () => Confirm("rollback"))
            {
                IsEnabled = State.RollbackId is not null,
                Automation = new()
                {
                    AutomationId = "pack.update.menu.rollback"
                }
            },
            new MenuFlyoutItem(Resources.PackUpdateDetach, () => Confirm("detach"))
            {
                IsEnabled = State.HasSource,
                Automation = new()
                {
                    AutomationId = "pack.update.menu.detach"
                }
            },
        };
        if (State.OnlineSource && State.HasSource)
        {
            entries.Insert(0, new MenuFlyoutItem(Resources.RuntimeLoadVersions, _controller.ReloadVersions) { Automation = new() { AutomationId = "pack.update.menu.versions" } });
        }

        if (_connect is not null)
        {
            entries.Insert(0, new MenuFlyoutItem(Resources.PackUpdateConnectCurrent, Connect) { Automation = new() { AutomationId = "pack.update.menu.connect-current" } });
            entries.Insert(1, new MenuFlyoutItem(Resources.PackUpdateCheck, () => _ = ReviewAsync())
            {
                IsEnabled = State.CanReview,
                Automation = new() { AutomationId = "pack.update.menu.check" }
            });
        }

        _menu = new MenuFlyout([.. entries]).Style(LauncherMenus.CommandMenu);
        _menu.ShowAt(_window);
    }

    private void Connect()
    {
        if (_connectionRequested || !CanClose || _connect is null)
            return;
        _connectionRequested = true;
        _menu?.Hide();
        _connect();
    }

    private Task ReviewAsync()
    {
        _automaticConnectionStopped = false;
        return _controller.ReviewAsync();
    }

    private void Confirm(string action)
    {
        _menu?.Hide();
        _rollbackId = State.RollbackId;
        Confirmation = action;
    }

    private void ShowDetails()
    {
        _report.ViewHeight = Render.Bounds.Height;
        _report.ViewWidth = Render.Bounds.Width;
        var actions = string.Join(Environment.NewLine, (State.Review?.Actions ?? []).Where(item => item.Action != "unchanged").Select(item => string.Format(Resources.PackUpdateReportFile, item.Path, PackChangeText.Label(item.Action))));
        var missing = string.Join(Environment.NewLine, (State.Review?.Incoming.Missing ?? []).Select(item => string.Format(Resources.PackUpdateReportFile, item.Path ?? item.ManualKey, item.Reason)));
        var warnings = string.Join(Environment.NewLine, (State.History?.Warnings ?? []).Select(item => string.Format(Resources.PackUpdateReportFile, item.OperationId, item.Error)).Concat((State.Review?.ChangeSummary.Warnings ?? []).Select(item => string.Format(Resources.PackUpdateReportFile, item.Path, item.Message))));
        var readable = string.Format(
            Resources.PackUpdateReport,
            State.Name,
            State.CurrentVersion,
            State.TargetVersion,
            actions,
            missing,
            warnings,
            State.Error);
        _report.SetReport(readable, new JsonObject
        {
            ["pack"] = JsonSerializer.SerializeToNode(State.Pack, PackJsonContext.Default.PackContentState),
            ["review"] = JsonSerializer.SerializeToNode(State.Review, PackJsonContext.Default.PackReview),
            ["history"] = JsonSerializer.SerializeToNode(State.History, PackJsonContext.Default.PackOperationList),
            ["task"] = State.Report
        }.ToJsonString(new() { WriteIndented = true }));
        Details = true;
        _focus.Request(_report);
    }

    private async Task BrowseAsync(bool manual)
    {
        if (!CanClose)
            return;
        Picking = true;
        var target = _window.FocusedElement;
        try
        {
            if (manual)
            {
                if (await _window.CreateFolderPicker().PickSingleFolderAsync() is { } folder)
                    _controller.SetManual(folder.Path);
            }
            else
            {
                var picker = _window.CreateFileOpenPicker();
                picker.FileTypeFilter.Add(".zip");
                picker.FileTypeFilter.Add(".mrpack");
                if (await picker.PickSingleFileAsync() is { } file)
                    _controller.SetArchive(file.Path);
            }
        }
        catch (Exception exception)
        {
            _controller.ReportError(exception);
        }
        finally
        {
            Picking = false;
            _focus.Request(this, target);
        }
    }

    private void OnChanged(PackMaintenanceState state)
    {
        if (FinishIfSucceeded(state))
            return;
        State = state;
        if (_closeWhenIdle && state.Active && !state.CancelRequested && state.Error.Length > 0)
            _closeWhenIdle = false;
        if (_closeWhenIdle && CanClose)
        {
            _close();
        }
        else if (_connect is not null && !_automaticConnectionStopped && !Details && Confirmation.Length == 0 && CanClose && State.Error.Length == 0 && State.Review is { Phase: "reviewed", HasChanges: false } && State.MissingCount == 0)
        {
            Connect();
        }
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs args) => Viewport = args.Size;

    protected override IDisposable? OnMounted()
    {
        _controller.Changed += OnChanged;
        State = _controller.State;
        _ = OpenAsync();
        _window.SizeChanged += OnSizeChanged;
        return PopupKeyboardClose.Bind(_window, RequestClose, () => !Picking && !_report.Exporting);
    }

    private async Task OpenAsync()
    {
        await _controller.OpenAsync();
        if (_connect is not null && !_closeWhenIdle && State.Error.Length == 0 && State.HasSource)
            await ReviewAsync();
    }

    protected override void OnDispose()
    {
        _controller.Changed -= OnChanged;
        _controller.Dispose();
        _focus.Dispose();
        _menu?.Hide();
        _window.SizeChanged -= OnSizeChanged;
        base.OnDispose();
    }
}

[View]
internal readonly partial record struct PackMaintenanceBody(
    LauncherAppearance Theme,
    PackMaintenanceState State,
    string Confirmation,
    PackChangeRow[] Changes,
    PackMaintenanceController Controller,
    Action BrowseArchive,
    Action BrowseManual,
    bool Editable,
    bool HasChanges,
    string ChangeSummary)
{
    internal static Func<PackChangeRow, PackChangeView> ChangeTemplate(LauncherAppearance theme) => row => new PackChangeView(theme, row);

    internal string Source => State.Pack?.Manifest["fileApi"]?.ToString() ?? State.Pack?.OnlineSource?.Name?.ToString() ?? State.Pack?.OnlineSource?.ProjectId ?? string.Empty;
    internal string VersionSummary => State.Review?.Phase == "reviewed" ? string.Format(Resources.PackUpdateVersions, State.CurrentVersion, State.TargetVersion) : string.Format(Resources.PackUpdateCurrentVersion, State.CurrentVersion);
    internal string[] VersionNames => [.. State.Versions.Select(version => version.Name)];
    internal bool ReviewingChanges => HasChanges && State.Review is { Phase: "reviewed", Incoming.Complete: true };
    internal int VersionIndex
    {
        get
        {
            var id = State.VersionId;
            return State.Versions.ToList().FindIndex(version => version.Id == id);
        }
    }

    internal Action ToggleDownload
    {
        get
        {
            var controller = Controller;
            var value = !State.Download;
            return () => controller.SetDownload(value);
        }
    }

    internal Action ToggleRuntime
    {
        get
        {
            var controller = Controller;
            var value = !State.PrepareRuntime;
            return () => controller.SetRuntime(value);
        }
    }

    /// <summary>Presents the actual staged file plan and editable acquisition choices, leaving mutation to typed Core tasks.</summary>
    public Ui Body => Column([
        Column([
            LauncherControls.Choice(Theme,[Resources.PackUpdateArchiveMode, Resources.PackUpdateSourceMode]).MinHeight(40f)
                .SelectedIndex(State.UseSource ? 1 : 0)
                .OnSelectionChanged(Controller.SetMode)
                .Enabled(Editable)
                .Visible(State.HasSource && !ReviewingChanges)
                .Automation(new()
                {
                    Name = Resources.PackUpdateMode,
                    AutomationId = "pack.update.mode"
                }),
            Flow(FlowDirection.LeftToRight, Text(Source)
                    .FontSize(12f).Foreground(Theme.Muted)
                    .NoWrap()
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed())
                .Visible(State.UseSource && !ReviewingChanges),
            Column([
                Text(Resources.ContentUpdatesChannel)
                    .FontSize(13f).Foreground(Theme.Ink),
                LauncherControls.Choice(Theme,[Resources.ContentUpdatesRelease, Resources.ContentUpdatesBeta, Resources.ContentUpdatesAlpha]).MinHeight(40f)
                    .SelectedIndex(State.Channel == "beta"
                        ? 1
                        : State.Channel == "alpha" ? 2 : 0)
                    .OnSelectionChanged(Controller.SetChannel)
                    .Enabled(Editable)
                    .Automation(new()
                    {
                        Name = Resources.ContentUpdatesChannel,
                        AutomationId = "pack.update.channel"
                    }),
                Text(Resources.ContentVersion)
                    .FontSize(13f).Foreground(Theme.Ink),
                Flow(FlowDirection.LeftToRight, LauncherControls.Choice(Theme,VersionNames).MinHeight(40f)
                        .SelectedIndex(VersionIndex)
                        .OnSelectionChanged(Controller.SetVersion)
                        .Enabled(Editable && !State.ReadingVersions)
                        .Automation(new()
                        {
                            Name = Resources.ContentVersion,
                            AutomationId = "pack.update.version"
                        })),
            ])
                .Spacing(8f)
                .Visible(State.UseSource && State.OnlineSource && !ReviewingChanges),
            Column([
                new Features.Import.ImportPathInput(
Theme,                    Resources.PackUpdateArchive,
                    State.Archive,
                    Editable,
                    Controller.SetArchive,
                    BrowseArchive,
                    "pack.update.archive",
                    "pack.update.browse",
                    Resources.PackBrowse,
                    LauncherStyles.Input,
                    Theme.Buttons.BrowseButton,
                    IsFile: true),
                new Features.Import.ImportPathInput(
Theme,                    Resources.PackManualFolder,
                    State.ManualDirectory,
                    Editable,
                    Controller.SetManual,
                    BrowseManual,
                    "pack.update.manual",
                    "pack.update.manual-browse",
                    Resources.PackManualFolder,
                    LauncherStyles.Input,
                    Theme.Buttons.BrowseButton),
                new LauncherCheckBox(
Theme,                    Resources.PackDownload,
                    State.Download,
                    ToggleDownload,
                    Editable,
                    "pack.update.download"),
            ])
                .Spacing(10f)
                .Visible(!State.UseSource && !ReviewingChanges),
            new LauncherCheckBox(
Theme,                Resources.PackPrepareRuntime,
                State.PrepareRuntime,
                ToggleRuntime,
                Editable && (State.UseSource || State.Download),
                "pack.update.runtime").Visible(!ReviewingChanges),
            Grid(
                [GridLength.Star(), GridLength.Auto],
                [GridLength.Auto],
                [
                    Flow(FlowDirection.LeftToRight, Text(VersionSummary)
                            .FontSize(14f).Foreground(Theme.Ink)
                            .Wrap())
                        .VAlign(VerticalAlignment.Center)
                        .Cell(0, 0),
                    LauncherControls.OutlineCommand(Theme,Resources.PackUpdateReselect, Controller.Reselect)
                        .Style(Theme.Buttons.DialogHeaderButton)
                        .Enabled(Editable)
                        .Visible(ReviewingChanges)
                        .Automation(new()
                        {
                            Name = Resources.PackUpdateReselect,
                            AutomationId = "pack.update.reselect"
                        })
                        .Cell(1, 0),
                ])
                .ColumnSpacing(12f)
                .Visible(State.Pack is not null),
            Text(ChangeSummary)
                .FontSize(12f).Foreground(Theme.Muted)
                .Wrap()
                .Visible(HasChanges && State.Review?.Phase == "reviewed"),
            ItemsRepeater(Changes, static row => row.Index, ChangeTemplate(Theme), new Sprout.Widgets.StackLayout { EstimatedItemExtent = 42f })
                .Visible(Changes.Length > 0 && State.Review?.Phase == "reviewed"),
            Text(Resources.PackUpdateNoChanges)
                .FontSize(13f).Foreground(Theme.Muted)
                .Wrap()
                .Visible(State.Review is { Phase: "reviewed", HasChanges: false } && State.MissingCount == 0),
            new LauncherNotice(Theme,Resources.PackUpdateMissing, string.Format(Resources.PackUpdateMissingDetail, State.MissingCount), LauncherNoticeKind.Information, "pack.update.missing").Visible(State.MissingCount > 0),
        ])
            .Spacing(14f)
            .Visible(Confirmation.Length == 0),
        new LauncherNotice(Theme,Resources.PackUpdateConfirmRollback, Resources.PackUpdateRollbackDetail, LauncherNoticeKind.Information, "pack.update.rollback-confirm").Visible(Confirmation == "rollback"),
        new LauncherNotice(Theme,Resources.PackUpdateConfirmDetach, Resources.PackUpdateDetachDetail, LauncherNoticeKind.Information, "pack.update.detach-confirm").Visible(Confirmation == "detach"),
        new LauncherNotice(Theme,Resources.PackUpdateReadWarning, string.Format(Resources.PackUpdateReadWarningDetail, State.ReadWarnings), LauncherNoticeKind.Information, "pack.update.history-warning").Visible(State.ReadWarnings > 0),
        ProgressBar()
            .Indeterminate(true)
            .Thickness(3f)
            .Visible(State.Active || State.ReadingVersions),
        Text(State.Progress)
            .FontSize(12f).Foreground(Theme.Muted)
            .Wrap()
            .Visible(State.Active && State.Progress.Length > 0),
        new LauncherNotice(Theme,Resources.FeatureTaskFailed, State.Error, LauncherNoticeKind.Error, "pack.update.error").Visible(State.Error.Length > 0),
    ])
        .Spacing(14f)
        .Padding(24f, 20f);
}