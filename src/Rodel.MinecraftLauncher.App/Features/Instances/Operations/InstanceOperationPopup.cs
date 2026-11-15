// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.WindowsAppSDK;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Instances;

internal sealed partial class InstanceOperationPopup : Control
{
    private readonly InstanceManagementController _controller;
    private readonly Window _window;
    private readonly Action _close;
    private readonly PopupPreparationClose _closing;
    private readonly DetailReportPopup _report;
    private bool _exporting;
    private bool _finished;
    private bool _flushingConfiguration;
    internal bool ConfigurationCloseReady { get; private set; }
    private readonly PopupFocusRecovery _focus;
    private InteractionNode? _detailsReturnFocus;

    internal InstanceOperationPopup(InstanceManagementController controller, Window window, Action close)
    {
        _controller = controller;
        _window = window;
        _close = close;
        _closing = new(close, () => State.Active, () => CanClose, () => !State.CancelRequested && State.Error.Length > 0);
        State = controller.State;
        _focus = new(window);
        _report = new(
            window,
            DetailsLabel,
            string.Empty,
            close,
            ContentWidth - 24f,
            "instances.report",
            accessibleName: DetailsLabel,
            exportStateChanged: value =>
            {
                _exporting = value;
                FinishIfSucceeded();
            },
            back: BackToOperation,
            canClose: () => CanClose,
            showCloseButton: false);
    }

    internal partial InstanceManagementState State { get; private set; }
    public partial bool DetailsOpen { get; private set; }
    public partial bool Picking { get; private set; }
    private float ContentWidth => Math.Min(480f, _window.ClientSize.Width - 48f);
    private float BodyMaxHeight => Math.Max(80f, _window.ClientSize.Height - 64f - (State.Action == InstanceAction.Rename ? 140f : 164f));
    internal bool CanClose => !State.Active && !Picking && !_exporting;
    private bool ReadOnly => State.Action == InstanceAction.Content;
    private string ConfirmLabel => State.Action == InstanceAction.Configure
        ? Resources.SaveLaunchSettings
        : State.Action == InstanceAction.Rename
        ? Resources.InstanceRename
        : State.Action == InstanceAction.Delete ? Resources.DialogConfirm : State.Title;
    private string DetailsLabel => State.Action == InstanceAction.Content ? Resources.InstanceErrorDetails : Resources.InstanceOperationDetails;
    private bool CanConfirm => !Picking && (State.Active ? State.Job is not null && !State.CancelRequested : State.Succeeded || (State.Action != InstanceAction.Content && (State.Action is not (InstanceAction.Rename or InstanceAction.Copy) || State.Name.Trim().Length > 0) && (State.Action != InstanceAction.Configure || State.Draft?.CanSave == true) && (State.Action != InstanceAction.Restore || (State.BackupIndex >= 0 && State.BackupIndex < State.Backups.Count))));

    private Ui Body => Overlay([
        _report.Visible(DetailsOpen),
        Grid(
            [GridLength.Star()],
            [GridLength.Auto, GridLength.Auto, GridLength.Auto],
            [
                Border(Grid(
                        [GridLength.Star(), GridLength.Auto],
                        [GridLength.Auto],
                        [
                            Column([
                                Text(State.Title)
                                    .FontSize(19f).FontWeight(600).Foreground(LauncherTheme.Ink)
                                    .Wrap(),
                                Text(State.Instance?.Name ?? string.Empty)
                                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                                    .Wrap()
                                    .Visible(State.Action != InstanceAction.Rename),
                            ])
                                .Spacing(5f)
                                .Cell(0, 0),
                            LauncherControls.OutlineCommand(DetailsLabel, () => ShowDetails())
                                .Style(LauncherButtons.DialogHeaderButton)
                                .VAlign(VerticalAlignment.Center)
                                .Enabled(!Picking)
                                .Visible(State.Action != InstanceAction.Rename && State.HasDetails)
                                .Automation(new()
                                {
                                    Name = DetailsLabel,
                                    AutomationId = "instances.operation.details"
                                })
                                .Cell(1, 0),
                        ])
                        .ColumnSpacing(16f))
                    .Padding(new Thickness(24f, 20f, 24f, 16f))
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(0f, 0f, 0f, 1f)
                    .Cell(0, 0),
                ScrollView(new InstanceOperationBody(
LauncherAppearance.Capture(),                        _window,
                        State,
                        State.Title,
                        State.Explanation,
                        State.BackupNames,
                        State.Status,
                        State.Progress,
                        State.Percent,
                        _controller))
                    .MaxHeight(BodyMaxHeight)
                    .Automation(new()
                    {
                        Name = State.Title,
                        AutomationId = "instances.operation.scroll"
                    })
                    .Cell(0, 1),
                Border(Grid(
                        [GridLength.Star(), GridLength.Star()],
                        [GridLength.Auto],
                        [
                            LauncherControls.Command(ConfirmLabel, () => _ = ConfirmAsync())
                                .Style(LauncherButtons.DialogPrimary)
                                .MinHeight(40f)
                                .Enabled(!State.Active && CanConfirm)
                                .Visible(!ReadOnly)
                                .Automation(new()
                                {
                                    Name = ConfirmLabel,
                                    AutomationId = "instances.operation.confirm"
                                })
                                .Cell(0, 0),
                            LauncherControls.OutlineCommand(ReadOnly ? Resources.CloseLaunchSettings : Resources.DialogCancel, () =>
                                {
                                    if (State.Active)
                                        _ = _controller.CancelAsync();
                                    else
                                        _close();
                                })
                                .Style(LauncherButtons.DialogSecondary)
                                .MinHeight(40f)
                                .Enabled(State.Active ? State.Job is not null && !State.CancelRequested : CanClose)
                                .Automation(new()
                                {
                                    Name = ReadOnly ? Resources.CloseLaunchSettings : Resources.DialogCancel,
                                    AutomationId = "instances.operation.dismiss"
                                })
                                .Cell(1, 0),
                        ])
                        .ColumnSpacing(12f))
                    .Padding(new Thickness(24f, 16f, 24f, 20f))
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(0f, 1f, 0f, 0f)
                    .Visible(State.Action != InstanceAction.Configure)
                    .Cell(0, 2),
            ])
            .Width(ContentWidth)
            .Visible(!DetailsOpen),
    ]);

    private async Task ConfirmAsync()
    {
        if (Picking || _exporting)
            return;
        if (State.Active)
        {
            await _controller.CancelAsync();
            return;
        }

        if (State.Action == InstanceAction.Export)
        {
            var returnFocus = _window.FocusedElement;
            Picking = true;
            try
            {
                var picker = _window.CreateFileSavePicker();
                picker.SuggestedFileName = "instance-export";
                // A concrete array keeps the WinRT collection projection available in NativeAOT.
                string[] extensions = [".mrpack"];
                picker.FileTypeChoices.Add(Resources.InstanceExport, extensions);
                var result = await picker.PickSaveFileAsync();
                if (result is not null)
                    await _controller.ExecuteAsync(result.Path);
            }
            catch (Exception exception)
            {
                _controller.ReportError(exception);
            }
            finally
            {
                Picking = false;
                if (!FinishIfSucceeded())
                    _focus.Request(this, returnFocus);
            }
        }
        else
        {
            await _controller.ExecuteAsync();
        }
    }

    private void ShowDetails()
    {
        _detailsReturnFocus = _window.FocusedElement;
        _report.ViewHeight = Render.Bounds.Height;
        _report.ViewWidth = Render.Bounds.Width;
        _report.SetReport(State.ReadableReport, State.RawReport);
        DetailsOpen = true;
        _focus.Request(_report);
    }

    private void BackToOperation()
    {
        DetailsOpen = false;
        _focus.Request(this, _detailsReturnFocus);
    }

    private void OnChanged(InstanceManagementState state)
    {
        if (_closing.IsClosing || _finished)
            return;
        var completed = State.Active && !state.Active;
        State = state;
        if (_closing.Observe())
            return;
        if (FinishIfSucceeded())
            return;
        if (completed)
        {
            _focus.RequestIfLost(this);
            if (state.Action == InstanceAction.Rename)
                Controls.LauncherToast.ShowError(_window, state.Error.Length > 0 ? state.Error : state.Status);
        }

        if (DetailsOpen)
            _report.SetReport(state.ReadableReport, state.RawReport);
    }

    internal void RequestClose()
    {
        if (State.Action == InstanceAction.Configure)
        {
            _ = CloseConfigurationAsync();
            return;
        }

        if (_closing.Request())
            _ = _controller.CancelAsync();
    }

    private async Task CloseConfigurationAsync()
    {
        if (_flushingConfiguration || ConfigurationCloseReady)
            return;
        _flushingConfiguration = true;
        try
        {
            if (await _controller.FlushConfigurationAsync())
            {
                ConfigurationCloseReady = true;
                _close();
            }
        }
        finally
        {
            _flushingConfiguration = false;
        }
    }

    private bool FinishIfSucceeded()
    {
        if (_closing.IsClosing)
            return true;
        if (_finished || !State.Succeeded || !CanClose)
            return _finished;
        _finished = true;
        _close();
        Controls.LauncherToast.Show(_window, Resources.InstanceOperationSucceeded);
        return true;
    }

    protected override IDisposable? OnMounted()
    {
        _controller.Changed += OnChanged;
        State = _controller.State;
        return PopupKeyboardClose.Bind(_window, RequestClose, () => !Picking && !_exporting);
    }

    protected override void OnDispose()
    {
        _closing.Dispose();
        _focus.Dispose();
        _controller.Changed -= OnChanged;
        base.OnDispose();
    }
}