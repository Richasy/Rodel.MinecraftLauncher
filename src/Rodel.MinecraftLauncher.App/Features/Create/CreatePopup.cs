// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Popups;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Create;

internal sealed partial class CreatePopup : Control
{
    private readonly CreateController _controller;
    private readonly Window _window;
    private readonly Action _close;
    private readonly PopupPreparationClose _closing;
    private readonly Action<string> _launch;
    private readonly DetailReportPopup _report;
    private readonly CreateFields _fields;
    private bool _exporting;
    private bool _disposed;
    private bool _finished;
    private readonly PopupFocusRecovery _focus;
    private InteractionNode? _detailsReturnFocus;

    internal CreatePopup(
        CreateController controller,
        Window window,
        Action close,
        Action<string> launch)
    {
        _controller = controller;
        _window = window;
        _focus = new(window);
        _close = close;
        _closing = new(close, () => State.Active, () => CanClose, () => !State.CancelRequested && State.Error.Length > 0);
        _launch = launch;
        State = controller.State;
        _fields = new(controller, window);
        Viewport = window.ClientSize;
        _report = new(
            window,
            Resources.CreateDetails,
            string.Empty,
            _close,
            ContentWidth,
            "create.report",
            Resources.CreateDetails,
            value =>
            {
                _exporting = value;
                FinishIfSucceeded();
            },
            back: BackToForm,
            canClose: () => CanClose,
            showCloseButton: false);
    }

    internal partial CreateState State { get; private set; }
    public partial Size Viewport { get; private set; }
    public partial bool DetailsOpen { get; private set; }
    internal bool CanClose => !State.Active && !_exporting;
    private float ContentWidth => Math.Min(520f, Viewport.Width - 80f);
    private string Primary => State.Succeeded
        ? _controller.ServerId is null ? Resources.CreateLaunchGame : Resources.ServerConnect
        : State.InstanceId is not null ? Resources.CreateRetry : Resources.CreateConfirm;
    private string Secondary => State.Active
        ? Resources.CreateCancel
        : State.InstanceId is not null ? Resources.CloseLaunchSettings : Resources.DialogCancel;

    private Ui Body => Overlay([
        _report.Visible(DetailsOpen),
        Grid(
            [GridLength.Star()],
            [GridLength.Auto, GridLength.Star(), GridLength.Auto],
            [
                Border(Grid(
                        [GridLength.Star(), GridLength.Auto],
                        [GridLength.Auto],
                        [
                            Text(_controller.ServerId is null ? Resources.CreateInstance : Resources.ServerCreateClient)
                                .FontSize(19f).FontWeight(600).Foreground(LauncherTheme.Ink)
                                .Wrap()
                                .VAlign(VerticalAlignment.Center)
                                .Cell(0, 0),
                            LauncherControls.OutlineCommand(Resources.CreateDetails, () => ShowDetails())
                                .Style(LauncherButtons.DialogHeaderButton)
                                .VAlign(VerticalAlignment.Center)
                                .Visible(State.HasDetails)
                                .Enabled(!_exporting)
                                .Automation(new()
                                {
                                    Name = Resources.CreateDetails,
                                    AutomationId = "create.details"
                                })
                                .Cell(1, 0),
                        ])
                        .ColumnSpacing(16f))
                    .Padding(new Thickness(24f, 20f, 24f, 16f))
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(0f, 0f, 0f, 1f)
                    .Cell(0, 0),
                ScrollView(new CreateDialogBody(
LauncherAppearance.Capture(),                        _fields,
                        State,
                        State.Workflow,
                        State.Status,
                        State.FileProgress,
                        State.CurrentFileProgress,
                        State.Warning))
                    .Automation(new()
                    {
                        Name = Resources.CreateInstance,
                        AutomationId = "create.form.scroll"
                    })
                    .Cell(0, 1),
                Border(Grid(
                        [GridLength.Star(), GridLength.Star()],
                        [GridLength.Auto],
                        [
                            LauncherControls.Command(Primary, () => RunPrimary())
                                .Style(LauncherButtons.DialogPrimary)
                                .MinHeight(40f)
                                .Enabled(!State.Active && (State.Succeeded || State.CanConfirm))
                                .Automation(new()
                                {
                                    Name = Primary,
                                    AutomationId = "create.confirm"
                                })
                                .Cell(0, 0),
                            LauncherControls.OutlineCommand(Secondary, () => Dismiss())
                                .Style(LauncherButtons.DialogSecondary)
                                .MinHeight(40f)
                                .Enabled(State.Active ? !State.CancelRequested : CanClose)
                                .Automation(new()
                                {
                                    Name = Secondary,
                                    AutomationId = State.Active ? "create.cancel" : "create.close"
                                })
                                .Cell(1, 0),
                        ])
                        .ColumnSpacing(12f))
                    .Padding(new Thickness(24f, 16f, 24f, 20f))
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(0f, 1f, 0f, 0f)
                    .Cell(0, 2),
            ])
            .Width(ContentWidth + 24f)
            .MaxHeight(Math.Max(0f, Viewport.Height - 48f))
            .Visible(!DetailsOpen),
    ]);

    private void Dismiss()
    {
        if (State.Active)
            _ = _controller.CancelAsync();
        else if (CanClose)
            _close();
    }

    private void RunPrimary()
    {
        if (State.Succeeded && State.InstanceId is { } id)
        {
            _close();
            _launch(id);
        }
        else if (State.Active)
        {
            _ = _controller.CancelAsync();
        }
        else
        {
            _ = _controller.ConfirmAsync();
        }
    }

    private async void ShowDetails()
    {
        if (!State.HasDetails)
            return;
        _detailsReturnFocus = _window.FocusedElement;
        _report.ViewHeight = Render.Bounds.Height;
        _report.ViewWidth = Render.Bounds.Width;
        _report.SetReport(State.ReadableReport, State.Report);
        DetailsOpen = true;
        _focus.Request(_report);
        await _controller.LoadActivityAsync();
        if (!_disposed)
            _report.SetReport(_controller.State.ReadableReport, _controller.State.Report);
    }

    internal void RequestClose()
    {
        if (_closing.Request())
            _ = _controller.CancelAsync();
    }

    protected override IDisposable? OnMounted()
    {
        _controller.StateChanged += OnStateChanged;
        _window.SizeChanged += OnSizeChanged;
        State = _controller.State;
        return PopupKeyboardClose.Bind(_window, RequestClose, () => !_exporting);
    }

    private void OnStateChanged(CreateState state)
    {
        if (_closing.IsClosing || _finished)
            return;
        if (State.Active && !state.Active)
            _focus.RequestIfLost(this);
        State = state;
        if (_closing.Observe())
            return;
        _fields.Update(state);
        if (FinishIfSucceeded())
            return;
        if (DetailsOpen)
            _report.SetReport(state.ReadableReport, state.Report);
    }

    private bool FinishIfSucceeded()
    {
        if (_closing.IsClosing)
            return true;
        if (_finished || !State.Succeeded || !CanClose)
            return _finished;
        _finished = true;
        _close();
        Controls.LauncherToast.Show(_window, State.Warning.Length > 0 ? State.Warning : State.Status);
        return true;
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs e) => Viewport = e.Size;

    private void BackToForm()
    {
        DetailsOpen = false;
        _focus.Request(this, _detailsReturnFocus);
    }

    protected override void OnDispose()
    {
        _closing.Dispose();
        _disposed = true;
        _focus.Dispose();
        _controller.StateChanged -= OnStateChanged;
        _window.SizeChanged -= OnSizeChanged;
        base.OnDispose();
    }
}