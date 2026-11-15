// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Packs;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using Sprout.WindowsAppSDK;
using System.Text.Json;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Packs;

internal sealed partial class PackImportDialog : Control
{
    internal const float MaximumWidth = 1120f;
    private const float ContentInset = 24f;
    private const float ColumnGap = 40f;
    private readonly PackImportController _controller;
    private readonly Window _window;
    private readonly Action _close;
    private readonly Action<string> _launch;
    private readonly PopupFocusRecovery _focus;
    private readonly DetailReportPopup _report;
    private readonly Func<PackOptionalEntry, PackOptionalRowView> _factory;
    private float _reportViewHeight;
    private bool _finished;
    private FileDropHandler Drop => field ??= new(AcceptDrop, ReceiveDrop, value => Dropping = value);
    private partial bool Dropping { get; set; }

    internal PackImportDialog(
        PackImportController controller,
        Window window,
        Action close,
        Action<string> launch)
    {
        _controller = controller;
        _window = window;
        _close = close;
        _launch = launch;
        _focus = new(window);
        State = controller.State;
        Viewport = window.ClientSize;
        _factory = data => new(data.Path, controller);
        _report = new(
            window,
            Resources.PackPreparation,
            string.Empty,
            RequestClose,
            560f,
            "pack.import.report",
            Resources.PackPreparation,
            value =>
            {
                Exporting = value;
                FinishIfSucceeded();
            },
            back: () =>
            {
                Details = false;
                _focus.Request(this);
            },
            canClose: () => CanClose,
            showCloseButton: false);
    }

    private partial PackImportState State { get; set; }
    private partial Size Viewport { get; set; }
    private partial bool Picking { get; set; }
    private partial bool Exporting { get; set; }
    private partial bool Details { get; set; }
    private partial bool ClosingAfterCancel { get; set; }
    internal bool Succeeded => State.RetainedInstanceId.Length > 0;
    internal bool CanClose => !State.Active && !Picking && !Exporting;
    private string Title => State.HasNotices
        ? Resources.PackWorkflowNeedsReview
        : State.Preparation?.Status == "content-only"
        ? Resources.PackWorkflowContentOnly
        : State.Completed
        ? Resources.PackWorkflowDone
        : State.Existing || State.RetainedInstanceId.Length > 0 ? Resources.PackPreparation : Resources.PackImport;
    private bool ReadyToLaunch => State.Preparation?.Status is "completed" or "completed-with-notices";
    private string Primary => ReadyToLaunch
        ? State.Result?.Instance?.ServerId is null ? Resources.LaunchGame : Resources.ServerConnect
        : State.Inspection is null && !State.Existing
        ? Resources.PackInspect
        : State.HasWorkflow ? Resources.PackResumePreparation : Resources.PackStartPreparation;
    private float BodyMaximumHeight => Math.Max(100f, Viewport.Height - 208f);

    private void Confirm()
    {
        if (ReadyToLaunch)
            LaunchPrepared();
        else if (State.Inspection is null && State.RetainedInstanceId.Length == 0)
            _ = _controller.InspectAsync();
        else
            _ = _controller.ImportAsync();
    }

    private void Dismiss()
    {
        if (State.Active)
            _ = _controller.CancelAsync();
        else if (CanClose)
            _close();
    }

    private void LaunchPrepared()
    {
        if (!CanClose || State.Preparation?.Status is not ("completed" or "completed-with-notices") || State.RetainedInstanceId.Length == 0)
            return;
        var instanceId = State.RetainedInstanceId;
        _close();
        _launch(instanceId);
    }

    internal void RequestClose()
    {
        if (Picking || Exporting || ClosingAfterCancel)
            return;
        if (State.Active)
        {
            ClosingAfterCancel = true;
            _ = _controller.CancelAsync();
        }
        else
        {
            _close();
        }
    }

    private Ui Body => Overlay([
        _report.Visible(Details),
        Grid(
            [GridLength.Star()],
            [GridLength.Auto, GridLength.Auto, GridLength.Auto],
            [
                Border(Grid(
                        [GridLength.Star(), GridLength.Auto, GridLength.Auto],
                        [GridLength.Auto],
                        [
                            Text(Title)
                                .FontSize(20f).FontWeight(600).Foreground(LauncherTheme.Ink)
                                .VAlign(VerticalAlignment.Center)
                                .Cell(0, 0),
                            LauncherControls.OutlineCommand(
                                Row([
                                    LauncherIcons.Line("folder", 18f)
                                ]),
                                () => _ = BrowseAsync())
                                .Style(LauncherButtons.BrowseButton)
                                .Width(32f).Height(32f)
                                .Visible(State.RetainedInstanceId.Length > 0 && State.HasMissing)
                                .Enabled(CanClose)
                                .ToolTip(Resources.PackChangeArchive)
                                .Automation(new()
                                {
                                    Name = Resources.PackChangeArchive,
                                    HelpText = State.Source,
                                    AutomationId = "pack.import.change-source"
                                })
                                .Cell(1, 0),
                            LauncherControls.OutlineCommand(Resources.CreateDetails, () => ShowDetails())
                                .Style(LauncherButtons.DialogHeaderButton)
                                .VAlign(VerticalAlignment.Center)
                                .Visible(State.Report.Length > 0)
                                .Enabled(!State.Active && !Exporting)
                                .Automation(new()
                                {
                                    Name = Resources.CreateDetails,
                                    AutomationId = "pack.import.details"
                                })
                                .Cell(2, 0),
                        ])
                        .ColumnSpacing(12f))
                    .Padding(24f, 20f, 24f, 16f)
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(0f, 0f, 0f, 1f)
                    .Cell(0, 0),
                Grid(
                    [GridLength.Star(), GridLength.Star()],
                    [GridLength.Auto],
                    [
                        ScrollView(new PackPreparationTimeline(LauncherAppearance.Capture(),State))
                            .MaxHeight(BodyMaximumHeight)
                            .VAlign(VerticalAlignment.Top)
                            .Automation(new()
                            {
                                Name = Resources.PackPreparation,
                                AutomationId = "pack.workflow.scroll"
                            })
                            .Cell(0, 0),
                        DropTarget(Column([ScrollView(new PackImportBody(
LauncherAppearance.Capture(),                                State,
                                _controller,
                                _factory,
                                () => _ = BrowseAsync(),
                                () => _ = BrowseManualAsync(),
                                () => ShowDetails(),
                                CanClose,
                                _window.FlowDirection == FlowDirection.RightToLeft))
                            .MaxHeight(BodyMaximumHeight)
                            .VAlign(VerticalAlignment.Top)
                            .Automation(new()
                            {
                                Name = Resources.PackImport,
                                AutomationId = "pack.import.scroll"
                            }), new FileDropHint(LauncherAppearance.Capture(),Dropping)]).Spacing(8f), Drop)
                            .Automation(new() { Name = Resources.PackArchive, AutomationId = "pack.import.drop" })
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(ColumnGap)
                    .Padding(ContentInset, 0f)
                    .Cell(0, 1),
                Border(Grid(
                        [GridLength.Star(), GridLength.Star()],
                        [GridLength.Auto],
                        [
                            LauncherControls.Command(Primary, () => Confirm())
                                .Style(LauncherButtons.DialogPrimary)
                                .MinHeight(40f)
                                .Visible(State.CanContinue || ReadyToLaunch)
                                .Enabled(CanClose && (State.HasPack || (!State.Existing && !string.IsNullOrWhiteSpace(State.Source))))
                                .Automation(new()
                                {
                                    Name = Primary,
                                    AutomationId = ReadyToLaunch ? "pack.import.launch" : "pack.import.confirm"
                                })
                                .Cell(0, 0),
                            LauncherControls.OutlineCommand(State.Active ? Resources.FeatureTaskCancel : Resources.DialogCancel, () => Dismiss())
                                .Style(LauncherButtons.DialogSecondary)
                                .MinHeight(40f)
                                .Enabled(!Picking && !State.CancelRequested && !Exporting)
                                .Automation(new()
                                {
                                    Name = State.Active ? Resources.FeatureTaskCancel : Resources.DialogCancel,
                                    AutomationId = "pack.import.close"
                                })
                                .Cell(1, 0),
                        ])
                        .ColumnSpacing(ColumnGap))
                    .Padding(ContentInset, 16f, ContentInset, 20f)
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(0f, 1f, 0f, 0f)
                    .Cell(0, 2),
            ])
            .Width(Math.Min(MaximumWidth, Viewport.Width - 48f)).MaxHeight(Math.Max(100f, Viewport.Height - 48f))
            .Visible(!Details),
    ]);

    private async Task BrowseAsync()
    {
        if (!CanClose)
            return;
        var returnFocus = _window.FocusedElement;
        Picking = true;
        try
        {
            var picker = _window.CreateFileOpenPicker();
            picker.FileTypeFilter.Add(".mrpack");
            picker.FileTypeFilter.Add(".zip");
            if (await picker.PickSingleFileAsync() is { } file)
            {
                await SelectArchiveAsync(file.Path);
            }
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

    private async Task BrowseManualAsync()
    {
        if (!CanClose)
            return;
        var returnFocus = _window.FocusedElement;
        Picking = true;
        try
        {
            if (await _window.CreateFolderPicker().PickSingleFolderAsync() is { } folder)
                _controller.SetManualDirectory(folder.Path);
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

    private bool AcceptDrop(IReadOnlyList<string> paths) => CanClose && !Details && PackDropSource.Accepts(paths);

    private void ReceiveDrop(IReadOnlyList<string> paths) => _ = SelectArchiveAsync(paths[0]);

    private async Task SelectArchiveAsync(string path)
    {
        try
        {
            _controller.SetSource(path);
            if (State.RetainedInstanceId.Length == 0)
                await _controller.InspectAsync();
        }
        catch (Exception error)
        {
            _controller.ReportError(error);
        }
    }

    private void ShowDetails()
    {
        _reportViewHeight = Render.Bounds.Height;
        _report.ViewWidth = Render.Bounds.Width;
        _report.ViewHeight = _reportViewHeight;
        _report.SetReport(string.Join(Environment.NewLine, new[] { State.Status, State.Error, State.Source, PackImportText.Missing(State.Result), PackImportText.Acquisition(State.Result), Resources.PackResultExplanation }), State.Report);
        Details = true;
        _focus.Request(_report);
    }

    private void OnChanged(PackImportState state)
    {
        State = state;
        if (FinishIfSucceeded())
            return;
        if (ClosingAfterCancel && CanClose)
        {
            ClosingAfterCancel = false;
            _close();
            return;
        }

        if (ClosingAfterCancel && state.Active && state.Outcome.Length == 0 && !state.CancelRequested && state.Error.Length > 0)
            ClosingAfterCancel = false;
        _focus.RequestIfLost(this);
    }

    private bool FinishIfSucceeded()
    {
        var contentOnly = State.Preparation?.Status == "content-only" && State.Result?.Complete == true && !State.HasMissing;
        var completed = State.Preparation?.Status == "completed" || contentOnly;
        if (_finished || State.Outcome != "succeeded" || !completed || State.Error.Length > 0 || !CanClose)
            return _finished;
        _finished = true;
        _close();
        LauncherToast.Show(_window, State.Preparation?.Status == "content-only" ? Resources.PackWorkflowContentOnly : Resources.PackWorkflowDone);
        return true;
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs e)
    {
        Viewport = e.Size;
        if (Details)
        {
            _report.ViewWidth = Math.Min(MaximumWidth, e.Size.Width - 48f);
            _report.ViewHeight = Math.Min(_reportViewHeight, Math.Max(100f, e.Size.Height - 48f));
        }
    }

    protected override IDisposable? OnMounted()
    {
        _controller.Changed += OnChanged;
        _window.SizeChanged += OnSizeChanged;
        State = _controller.State;
        Viewport = _window.ClientSize;
        return PopupKeyboardClose.Bind(_window, RequestClose, () => !Picking && !Exporting && !ClosingAfterCancel);
    }

    protected override void OnDispose()
    {
        _controller.Changed -= OnChanged;
        _window.SizeChanged -= OnSizeChanged;
        _controller.Dispose();
        _focus.Dispose();
        base.OnDispose();
    }
}

[View]
internal readonly partial record struct PackImportBody(
    LauncherAppearance Theme,
    PackImportState State,
    PackImportController Controller,
    Func<PackOptionalEntry, PackOptionalRowView> Factory,
    Action Browse,
    Action BrowseManual,
    Action ShowDetails,
    bool CanBrowse,
    bool RightToLeft)
{
    internal static Func<PackMissingEntry, PackMissingRowView> MissingTemplate(LauncherAppearance theme) => row => new PackMissingRowView(theme, row);

    internal bool Retained => State.RetainedInstanceId.Length > 0;
    internal string Summary => State.Inspection is { } info
        ? string.Format(Resources.PackVersionSummary, info.VersionId?.ToString() ?? string.Empty, info.MinecraftVersion, info.Format)
        : State.Result?.Pack is { } pack ? string.Format(Resources.PackVersionSummary, pack.VersionId?.ToString() ?? string.Empty, pack.MinecraftVersion, pack.Format) : string.Empty;
    internal Action ToggleDownload
    {
        get
        {
            var controller = Controller;
            var enabled = !State.Download;
            return () => controller.SetDownload(enabled);
        }
    }

    internal Action ToggleRuntime
    {
        get
        {
            var controller = Controller;
            var enabled = !State.PrepareRuntime;
            return () => controller.SetPrepareRuntime(enabled);
        }
    }

    internal bool Editable => !State.Active && !State.HasWorkflow;
    internal int MissingCount => State.MissingFiles.Count;
    internal PackMissingEntry[] MissingPreview
    {
        get
        {
            var rightToLeft = RightToLeft;
            return [.. State.MissingFiles.Take(4).Select((item, index) => PackMissingEntry.From(item, index, rightToLeft))];
        }
    }

    internal string MissingHeading => string.Format(State.HasMissing ? Resources.PackMissingHeading : Resources.PackFilesProcessedList, MissingCount);
    internal int WarningCount => (State.Result?.Runtime?.Warnings.Count ?? 0) + (State.Result?.JavaWarning is null ? 0 : 1) + (State.Result?.Java?.CompatibilityNote is null ? 0 : 1);

    /// <summary>Keeps preparation inputs and file evidence in a stable side panel beside the complete step timeline.</summary>
    public Ui Body => Border(Column([
            Column([
                Text(State.Name)
                    .FontSize(18f).FontWeight(600).Foreground(Theme.Ink)
                    .Wrap()
                    .Visible(State.Existing),
                Flow(FlowDirection.LeftToRight, Text(Summary)
                        .FontSize(12f).Foreground(Theme.Muted)
                        .Wrap()
                        .Automation(new()
                        {
                            Name = Summary,
                            AutomationId = "pack.import.summary"
                        })),
            ])
                .Spacing(4f)
                .Visible(State.HasPack),
            Column([
                Text(Resources.PackArchive)
                    .FontSize(14f).Foreground(Theme.Ink),
                Grid(
                    [GridLength.Star(), GridLength.Auto],
                    [GridLength.Auto],
                    [
                        Flow(FlowDirection.LeftToRight, TextBox()
                                .Text(State.Source)
                                .ReadOnly()
                                .Placeholder(Resources.PathFilePlaceholder)
                                .Style(LauncherStyles.Input)
                                .MinHeight(40f)
                                .Enabled(Editable)
                                .Automation(new()
                                {
                                    Name = Resources.PackArchive,
                                    AutomationId = "pack.import.source"
                                }))
                            .Cell(0, 0),
                        LauncherControls.OutlineCommand(
Theme,                            Row([
                                LauncherIcons.Line("folder", 18f)
                            ]),
                            Browse)
                            .Style(Theme.Buttons.BrowseButton)
                            .Width(40f).Height(40f)
                            .Enabled(Editable && CanBrowse)
                            .ToolTip(Resources.PackBrowse)
                            .Automation(new()
                            {
                                Name = Resources.PackBrowse,
                                AutomationId = "pack.import.browse"
                            })
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(8f),
            ])
                .Spacing(6f),
            Text(Resources.PackChooseArchive)
                .FontSize(12f).Foreground(Theme.Muted)
                .Wrap()
                .Visible(State.Inspection is null && !Retained),
            Column([
                Text(Resources.PackInstanceName)
                    .FontSize(14f).Foreground(Theme.Ink),
                TextBox(State.Name, Controller.SetName)
                    .Style(LauncherStyles.Input)
                    .MinHeight(40f)
                    .Enabled(Editable)
                    .Automation(new()
                    {
                        Name = Resources.PackInstanceName,
                        AutomationId = "pack.import.name"
                    }),
                Text(Resources.PackDestinationShort)
                    .FontSize(12f).Foreground(Theme.Muted)
                    .ToolTip(string.Format(Resources.PackDestination, Controller.Destination)),
            ])
                .Spacing(6f)
                .Visible(State.Inspection is not null && !State.Existing),
            Column([
                Text(MissingHeading)
                    .FontSize(15f).FontWeight(600).Foreground(Theme.Ink)
                    .Wrap()
                    .Automation(new()
                    {
                        Name = MissingHeading,
                        AutomationId = "pack.import.missing-heading",
                        LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                    }),
                ItemsRepeater(MissingPreview, static entry => entry.Index, MissingTemplate(Theme), new StackLayout { EstimatedItemExtent = 56f })
                    .Visible(MissingCount > 0),
                LauncherControls.OutlineCommand(Theme,string.Format(Resources.PackMoreMissing, Math.Max(0, MissingCount - 4)), ShowDetails)
                    .Style(Theme.Buttons.DialogHeaderButton)
                    .Visible(MissingCount > 4)
                    .Automation(new()
                    {
                        Name = string.Format(Resources.PackMoreMissing, Math.Max(0, MissingCount - 4)),
                        AutomationId = "pack.import.more-missing"
                    }),
            ])
                .Spacing(12f)
                .Visible(MissingCount > 0),
            Column([
                Text(Resources.PackOptionalFiles)
                    .FontSize(15f).FontWeight(600).Foreground(Theme.Ink),
                ItemsRepeater(State.OptionalFiles, static data => data.Index, Factory, new StackLayout { EstimatedItemExtent = 36f }).KeyboardNavigation(),
            ])
                .Spacing(6f)
                .Visible(!State.Existing && State.OptionalFiles.Length > 0),
            Column([
                new LauncherCheckBox(
Theme,                    Resources.PackDownload,
                    State.Download,
                    ToggleDownload,
                    !State.Active && !State.Completed,
                    "pack.import.download",
                    State.Download ? Resources.PackNetworkModeOnline : Resources.PackNetworkModeLocal),
                new LauncherCheckBox(
Theme,                    Resources.PackPrepareRuntime,
                    State.Download && State.PrepareRuntime,
                    ToggleRuntime,
                    !State.Active && !State.Completed && State.Download,
                    "pack.import.runtime"),
                Column([
                    Text(Resources.PackManualFolder)
                        .FontSize(14f).Foreground(Theme.Ink)
                        .ToolTip(Resources.PackManualHelp),
                    Grid(
                        [GridLength.Star(), GridLength.Auto],
                        [GridLength.Auto],
                        [
                            Flow(FlowDirection.LeftToRight, TextBox()
                                    .Text(State.ManualDirectory)
                                    .ReadOnly()
                                    .Placeholder(Resources.PathFolderPlaceholder)
                                    .Style(LauncherStyles.Input)
                                    .MinHeight(40f)
                                    .Enabled(!State.Active && !State.Completed)
                                    .Automation(new()
                                    {
                                        Name = Resources.PackManualFolder,
                                        HelpText = Resources.PackManualHelp,
                                        AutomationId = "pack.import.manual"
                                    }))
                                .Cell(0, 0),
                            LauncherControls.OutlineCommand(
Theme,                                Row([
                                    LauncherIcons.Line("folder", 18f)
                                ]),
                                BrowseManual)
                                .Style(Theme.Buttons.BrowseButton)
                                .Width(40f).Height(40f)
                                .Enabled(!State.Active && !State.Completed && CanBrowse)
                                .ToolTip(Resources.PackManualFolder)
                                .Automation(new()
                                {
                                    Name = Resources.PackManualFolder,
                                    AutomationId = "pack.import.manual-browse"
                                })
                                .Cell(1, 0),
                        ])
                        .ColumnSpacing(8f),
                ])
                    .Spacing(6f),
            ])
                .Spacing(12f)
                .Visible(State.HasPack),
            new LauncherNotice(Theme,Resources.PackWorkflowContentOnly, Resources.PackWorkflowDoneDetail, LauncherNoticeKind.Information, "pack.import.result").Visible(State.Preparation?.Status == "content-only"),
            new LauncherNotice(Theme,Resources.PackWorkflowNeedsReview, Resources.PackWorkflowNoticesDetail, LauncherNoticeKind.Information, "pack.import.notices").Visible(State.HasNotices),
            LauncherControls.OutlineCommand(Theme,string.Format(Resources.PackWorkflowWarnings, WarningCount), ShowDetails)
                .Style(Theme.Buttons.DialogHeaderButton)
                .Visible(WarningCount > 0)
                .Enabled(!State.Active)
                .Automation(new()
                {
                    Name = string.Format(Resources.PackWorkflowWarnings, WarningCount),
                    AutomationId = "pack.workflow.warnings"
                }),
            new LauncherNotice(Theme,State.Active ? Resources.PackProgressReadFailed : Resources.FeatureTaskFailed, State.Error, LauncherNoticeKind.Error, "pack.import.error").Visible(State.Error.Length > 0 && (State.Preparation is null || State.Active)),
        ])
            .Spacing(20f))
        .Padding(0f, 24f);
}