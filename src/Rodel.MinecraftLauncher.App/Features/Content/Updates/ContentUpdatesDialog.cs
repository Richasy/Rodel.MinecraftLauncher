// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
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
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed partial class ContentUpdatesDialog : Control
{
    private readonly ContentController _operations;
    private readonly Window _window;
    private readonly string _instanceId;
    private readonly ContentKind _kind;
    private readonly Action _close;
    private readonly PopupPreparationClose _closing;
    private readonly DetailReportPopup _report;
    private readonly PopupFocusRecovery _focus;
    private readonly ContentUpdateSourceImages _sourceImages;
    private ImageLoadServices? _imageServices;
    private readonly Func<ContentUpdateCandidateRow, ContentUpdateCandidateView> _candidateFactory;
    private readonly Func<ContentPlanRow, ContentUpdatePlanView> _planFactory;
    private readonly Func<ContentFinding, ContentFindingView> _findingFactory = row => new(LauncherAppearance.Capture(), row, "content.updates.finding.");
    private bool _exporting;
    private bool _applying;

    internal ContentUpdatesDialog(
        LauncherClient client,
        ContentController operations,
        Window window,
        string instanceId,
        string instanceName,
        ContentKind kind,
        IReadOnlyList<ContentItem> items,
        Action close)
    {
        UpdateController = new(client, instanceId, kind, items);
        _operations = operations;
        _window = window;
        Viewport = window.ClientSize;
        _instanceId = instanceId;
        Context = instanceName;
        _kind = kind;
        _close = close;
        _closing = new(close, () => operations.State.Active || _applying, () => CanClose, () => !operations.State.CancelRequested && operations.State.Failure is not null);
        _focus = new(window);
        _sourceImages = new(OnSourceImagesUpdated);
        SourceIcons = _sourceImages.Snapshot;
        Updates = UpdateController.State;
        Operation = operations.State;
        _candidateFactory = row => new(LauncherAppearance.Capture(), row, file => UpdateController.Toggle(file),
            (file, index) => _ = UpdateController.SelectSourceAsync(file, index));
        _planFactory = row => new(LauncherAppearance.Capture(), row, _window.FlowDirection == FlowDirection.RightToLeft);
        _report = new(
            window,
            Resources.ContentOperationDetails,
            string.Empty,
            _close,
            Math.Min(580f, window.ClientSize.Width - 72f),
            "content.updates.report",
            exportStateChanged: value =>
            {
                _exporting = value;
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

    private partial ContentUpdatesState Updates { get; set; }
    private partial ContentState Operation { get; set; }
    private partial bool DetailsOpen { get; set; }
    private partial Size Viewport { get; set; }
    private partial IReadOnlyDictionary<string, ImageSource> SourceIcons { get; set; }
    internal bool CanClose => !_applying && !_operations.State.Active && !_exporting;
    private bool Available => Updates.Items.Any(item => item.Status == "available");
    private bool HasFailures => Updates.Items.Any(item => item.Status == "failed");
    private string Primary => Updates.BaselineChanged ? Resources.ContentUpdatesCheckAgain : string.Format(Updates.Reviewed ? Resources.ContentUpdatesConfirm : Resources.ContentUpdatesReview, Updates.SelectedCount);
    private ContentUpdateCandidateRow[] Candidates => [..Updates.Items.Select((item, index) => new ContentUpdateCandidateRow(
                item,
                !Updates.Reading,
                index == Updates.Items.Count - 1,
                _kind.Directory(),
                _window.FlowDirection == FlowDirection.RightToLeft,
                SourceIcons))];
    private string Error => Updates.Error.Length > 0
        ? Updates.Error
        : Operation.Outcome is "failed" or "interrupted" ? Operation.Status : string.Empty;
    private bool HasDetails => Updates.Error.Length > 0 || HasFailures || Operation.HasActionDetails || (Updates.Reviewed && Updates.Evidence?.Plan?.Warnings.Count > 0);
    private bool HasBody => Error.Length > 0
        || (Updates.Reviewed ? Updates.Plan.Count > 0 || Updates.Findings.Count > 0 : Updates.Items.Count > 0)
        || (!Updates.Reading && !Operation.Active);
    private string Context => string.Format(Resources.ContentUpdatesContext, field,
        Updates.Context.Length > 0 ? string.Format(Resources.ContentUpdatesContext, _kind.Title(), Updates.Context) : _kind.Title());
    private bool HasFooterActions => Updates.Reviewed || Available || Updates.BaselineChanged;
    private bool HasFooter => HasFooterActions || Updates.Reading || Operation.Active;

    private Ui Body => Overlay([
        _report.Visible(DetailsOpen),
        Grid(
            [GridLength.Star()],
            [GridLength.Auto, GridLength.Auto, GridLength.Auto],
            [
                Border(Grid(
                        [GridLength.Star(), GridLength.Auto, GridLength.Auto],
                        [GridLength.Auto, GridLength.Auto],
                        [
                            Text(Resources.ContentUpdatesTitle)
                                .FontSize(20f).FontWeight(700).Foreground(LauncherTheme.Ink)
                                .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                                .VAlign(VerticalAlignment.Center)
                                .Automation(new() { Name = Resources.ContentUpdatesTitle, AutomationId = "content.updates.title" })
                                .Cell(0, 0),
                            Text(Context)
                                .FontSize(12f).Foreground(LauncherTheme.Muted)
                                .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                                .Automation(new() { Name = Context, AutomationId = "content.updates.context" })
                                .Cell(0, 1),
                            LauncherControls.OutlineCommand(Resources.ContentOperationDetails, () => ShowDetails())
                                .Style(LauncherButtons.DialogHeaderButton)
                                .Visible(HasDetails)
                                .Enabled(!Operation.Active && !_applying)
                                .Cell(1, 0)
                                .Automation(new()
                                {
                                    Name = Resources.ContentOperationDetails,
                                    AutomationId = "content.updates.details"
                                }),
                            LauncherControls.OutlineCommand(
                                    Row([LauncherIcons.Line("close", 18f)]),
                                    RequestClose)
                                .Style(LauncherButtons.PopupCloseButton)
                                .Width(32f).Height(32f)
                                .Enabled(!Operation.CancelRequested && !_exporting)
                                .ToolTip(Resources.ContentUpdatesClose)
                                .Automation(new()
                                {
                                    Name = Resources.ContentUpdatesClose,
                                    AutomationId = "content.updates.close"
                                })
                                .Cell(2, 0),
                        ])
                        .ColumnSpacing(12f).RowSpacing(6f))
                    .Padding(24f, 20f)
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(0f, 0f, 0f, 1f)
                    .Cell(0, 0),
                ScrollView(new ContentUpdatesBody(
                        LauncherAppearance.Capture(),
                        this,
                        Updates,
                        Operation,
                        _applying,
                        Available,
                        HasFailures,
                        Error,
                        Candidates,
                        _candidateFactory,
                        _planFactory,
                        _findingFactory))
                    .MaxHeight(Math.Max(80f, Viewport.Height - (Updates.Reading || Operation.Active ? 280f : 220f)))
                    .Visible(HasBody)
                    .Automation(new()
                    {
                        Name = Resources.ContentUpdatesTitle,
                        AutomationId = "content.updates.scroll"
                    })
                    .Cell(0, 1),
                Border(Column([
                        Column([
                            Text(Operation.Active ? Operation.Status : Resources.ContentUpdatesChecking)
                                .FontSize(12f).Foreground(LauncherTheme.Muted)
                                .Automation(new()
                                {
                                    Name = Operation.Active ? Operation.Status : Resources.ContentUpdatesChecking,
                                    AutomationId = "content.updates.status",
                                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                                }),
                            Text(Operation.Progress)
                                .FontSize(11f).Foreground(LauncherTheme.Muted)
                                .Visible(Operation.Active && Operation.Progress.Length > 0),
                            ProgressBar()
                                .Indeterminate(!Operation.Active || Operation.Percent is null)
                                .Value(Operation.Percent ?? 0)
                                .Thickness(3f),
                        ])
                            .Spacing(6f)
                            .Visible(Updates.Reading || Operation.Active),
                        Grid(
                            [GridLength.Star(), GridLength.Auto],
                            [GridLength.Auto],
                            [
                                LauncherControls.OutlineCommand(Resources.ContentUpdatesBack, () => UpdateController.Back())
                                    .Style(LauncherButtons.DialogSecondary)
                                    .MinHeight(40f).HAlign(_window.FlowDirection == FlowDirection.RightToLeft ? HorizontalAlignment.Right : HorizontalAlignment.Left)
                                    .Visible(Updates.Reviewed)
                                    .Enabled(!Updates.Reading && !Operation.Active && !_applying)
                                    .Automation(new()
                                    {
                                        Name = Resources.ContentUpdatesBack,
                                        AutomationId = "content.updates.back"
                                    })
                                    .Cell(0, 0),
                                LauncherControls.Command(Primary, () => _ = ProceedAsync())
                                        .Style(LauncherButtons.DialogPrimary)
                                        .MinContentWidth(120f).MinHeight(40f)
                                        .Visible(Available || Updates.BaselineChanged)
                                        .Enabled(!Updates.Reading && !Operation.Active && !_applying && (Updates.BaselineChanged || (Updates.SelectedCount > 0 && (!Updates.Reviewed || Updates.Ready))))
                                        .Automation(new()
                                        {
                                            Name = Primary,
                                            AutomationId = "content.updates.confirm"
                                        })
                                        .Cell(1, 0),
                            ])
                            .ColumnSpacing(12f)
                            .Visible(HasFooterActions),
                    ])
                        .Spacing(12f))
                    .Padding(24f, 16f)
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(0f, HasBody ? 1f : 0f, 0f, 0f)
                    .Visible(HasFooter)
                    .Cell(0, 2),
            ])
            .Width(Math.Min(640f, Viewport.Width - 48f))
            .Visible(!DetailsOpen),
    ]);
    internal ContentUpdatesController UpdateController { get; }

    internal Task CheckUpdatesAsync(bool failuresOnly = false)
    {
        _operations.ClearOutcome();
        return UpdateController.CheckAsync(failuresOnly);
    }

    private async Task ProceedAsync()
    {
        if (Updates.Reading || Operation.Active || _applying)
            return;
        if (Updates.BaselineChanged)
        {
            await CheckUpdatesAsync();
            return;
        }

        if (!Updates.Reviewed)
        {
            _operations.ClearOutcome();
            await UpdateController.ReviewAsync();
            return;
        }

        if (UpdateController.ReviewId is not { } reviewId)
            return;
        _applying = true;
        try
        {
            await _operations.ApplyReviewAsync(_instanceId, _kind, reviewId);
            if (_closing.IsClosing)
                return;
            if (_operations.State.Outcome != "succeeded")
            {
                UpdateController.Invalidate();
            }
        }
        finally
        {
            _applying = false;
            if (!_closing.IsClosing && !_closing.Observe())
            {
                Updates = UpdateController.State with
                {
                };
            }
        }
    }

    private void ShowDetails()
    {
        _report.ViewHeight = Render.Bounds.Height;
        _report.ViewWidth = Render.Bounds.Width;
        var raw = Operation.HasActionDetails ? Operation.RawReport : Updates.RawReport;
        var summaries = new[]
        {
            Error,
            Operation.HasActionDetails ? Operation.ReadableReport : string.Empty,
            string.Format(Resources.ContentUpdatesContext, _kind.Title(), Updates.Context)
        }.Where(text => text.Length > 0).Concat(Updates.Findings.Select(finding => finding.Summary)).Concat(Updates.Items.Where(item => item.Status == "failed").Select(item => item.Name + Environment.NewLine + Resources.ContentUpdatesCheckFailed + Environment.NewLine + item.Reason));
        _report.SetReport(string.Join(Environment.NewLine + Environment.NewLine, summaries), raw);
        DetailsOpen = true;
        _focus.Request(_report);
    }

    private void OnUpdated(ContentUpdatesState state)
    {
        Updates = state;
        _sourceImages.Request(state.Items.SelectMany(item => item.SourceCandidates).Select(source => source.IconUrl), _imageServices);
        _focus.RequestIfLost(this);
    }

    private void OnSourceImagesUpdated() => SourceIcons = _sourceImages.Snapshot;

    private void OnOperation(ContentState state)
    {
        if (_closing.IsClosing)
            return;
        var succeeded = _applying && !state.Active && state.Outcome == "succeeded";
        if (succeeded)
            _applying = false;
        if (_closing.Observe(succeeded))
            return;
        Operation = state;
    }

    internal void RequestClose()
    {
        if (_closing.Request())
            _ = _operations.CancelAsync();
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs e) => Viewport = e.Size;

    protected override IDisposable? OnMounted()
    {
        _imageServices = Sprout.Theming.Environment.Resolve(ImageLoadServices.Key);
        UpdateController.Changed += OnUpdated;
        _operations.Changed += OnOperation;
        _window.SizeChanged += OnSizeChanged;
        _ = UpdateController.CheckAsync();
        return PopupKeyboardClose.Bind(_window, RequestClose, () => !_exporting);
    }

    protected override void OnDispose()
    {
        _sourceImages.Dispose();
        _closing.Dispose();
        UpdateController.Changed -= OnUpdated;
        _operations.Changed -= OnOperation;
        _window.SizeChanged -= OnSizeChanged;
        UpdateController.Dispose();
        _focus.Dispose();
        base.OnDispose();
    }
}

[View]
internal readonly partial record struct ContentUpdatesBody(
    LauncherAppearance Theme,
    ContentUpdatesDialog Dialog,
    ContentUpdatesState Updates,
    ContentState Operation,
    bool Applying,
    bool Available,
    bool HasFailures,
    string Error,
    ContentUpdateCandidateRow[] Candidates,
    Func<ContentUpdateCandidateRow, ContentUpdateCandidateView> CandidateFactory,
    Func<ContentPlanRow, ContentUpdatePlanView> PlanFactory,
    Func<ContentFinding, ContentFindingView> FindingFactory)
{
    internal Action ToggleAll
    {
        get
        {
            var controller = Dialog.UpdateController;
            return controller.ToggleAll;
        }
    }

    internal Action RetryFailed
    {
        get
        {
            var dialog = Dialog;
            return () => _ = dialog.CheckUpdatesAsync(failuresOnly: true);
        }
    }

    internal Action CheckAgain
    {
        get
        {
            var dialog = Dialog;
            return () => _ = dialog.CheckUpdatesAsync();
        }
    }

    /// <summary>Arranges the current selection, frozen review and actionable failures within one scrolling content region.</summary>
    public Ui Body => Column([
        Border(new LauncherCheckBox(
                Theme,
                string.Format(Resources.ContentUpdatesSelectAll, Updates.SelectedCount),
                Available && Updates.Items.Where(item => item.Status == "available").All(item => item.Selected),
                ToggleAll,
                !Updates.Reading,
                "content.updates.all",
                FontSize: 12f))
            .Padding(0f, 4f)
            .Visible(Available && !Updates.Reviewed),
        Text(Resources.ContentUpdatesEmpty)
            .FontSize(14f).Foreground(Theme.Ink)
            .Wrap()
            .Visible(!Updates.Reading && !Available && Updates.Error.Length == 0 && !HasFailures
                && !Updates.Items.Any(item => item.SourceCandidates.Count > 0))
            .Automation(new()
            {
                Name = Resources.ContentUpdatesEmpty,
                AutomationId = "content.updates.empty"
            }),
        ItemsRepeater(Candidates, static row => row.Item.FileName, CandidateFactory, new StackLayout { EstimatedItemExtent = 78f })
            .KeyboardNavigation(singleTabStop: false)
            .Visible(!Updates.Reviewed),
        ItemsRepeater(Updates.Plan, static row => row.Key, PlanFactory, new StackLayout { EstimatedItemExtent = 78f })
            .KeyboardNavigation(singleTabStop: false)
            .Visible(Updates.Reviewed),
        ItemsRepeater(Updates.Findings, static finding => finding.Key, FindingFactory, new StackLayout { EstimatedItemExtent = 56f })
            .Visible(Updates.Reviewed),
        Text(Error)
            .FontSize(12f).Foreground(Theme.Error)
            .Wrap()
            .Visible(Error.Length > 0)
            .Automation(new()
            {
                Name = Error,
                AutomationId = "content.updates.error",
                LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
            }),
        LauncherControls.OutlineCommand(Theme, Resources.ContentUpdatesRetryFailed, RetryFailed)
            .HAlign(HorizontalAlignment.Left)
            .Visible(HasFailures && !Updates.Reviewed)
            .Enabled(!Updates.Reading)
            .Automation(new()
            {
                Name = Resources.ContentUpdatesRetryFailed,
                AutomationId = "content.updates.retry-failed"
            }),
        LauncherControls.OutlineCommand(Theme, Resources.ContentUpdatesCheckAgain, CheckAgain)
            .HAlign(HorizontalAlignment.Left)
            .Visible(Updates.Error.Length > 0 && !Updates.Reviewed && !HasFailures && !Updates.BaselineChanged)
            .Enabled(!Updates.Reading && !Operation.Active && !Applying)
            .Automation(new()
            {
                Name = Resources.ContentUpdatesCheckAgain,
                AutomationId = "content.updates.recheck"
            }),
    ])
        .Spacing(8f)
        .Padding(24f, 10f);
}