// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.WindowsAppSDK;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Import;

internal sealed partial class ImportPopup : Control
{
    private readonly ImportController _controller;
    private readonly Window _window;
    private readonly Action _close;
    private readonly PopupPreparationClose _closing;
    private readonly ImportFields _fields;
    private readonly DetailReportPopup _report;
    private bool _disposed;
    private bool _finished;
    private long _pickerRevision;
    private readonly PopupFocusRecovery _focus;
    private InteractionNode? _detailsReturnFocus;

    internal ImportPopup(ImportController controller, Window window, Action close)
    {
        _controller = controller;
        _window = window;
        _focus = new(window);
        _close = close;
        _closing = new(close, () => State.Active, () => CanClose, () => !State.CancelRequested && State.Error.Length > 0);
        State = controller.State;
        Viewport = window.ClientSize;
        _fields = new(controller, kind => _ = PickAsync(kind));
        _report = new(
            window,
            Resources.ImportDetails,
            string.Empty,
            _close,
            ContentWidth,
            "import.details",
            Resources.ImportDetails,
            exporting =>
            {
                CanClose = exporting;
                FinishIfSucceeded();
            },
            back: BackToForm,
            canClose: () => CanClose,
            showCloseButton: false);
    }

    internal partial ImportState State { get; private set; }
    public partial Size Viewport { get; private set; }
    public partial bool DetailsOpen { get; private set; }
    public partial bool Picking { get; private set; }
    private bool _exporting;
    internal bool CanClose { get => !State.Active && !_exporting && !Picking; private set => _exporting = value; }
    private float ContentWidth => Math.Min(520f, Viewport.Width - 80f);
    private bool Editable => !State.Active && !Picking && State.RegisteredId is null;

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
                            Text(Resources.ImportInstance)
                                .FontSize(19f).FontWeight(600).Foreground(LauncherTheme.Ink)
                                .Wrap()
                                .VAlign(VerticalAlignment.Center)
                                .Cell(0, 0),
                            LauncherControls.OutlineCommand(Resources.ImportDetails, () => ShowDetails())
                                .Style(LauncherButtons.DialogHeaderButton)
                                .VAlign(VerticalAlignment.Center)
                                .Enabled(!Picking)
                                .Visible(State.HasDetails)
                                .Automation(new()
                                {
                                    Name = Resources.ImportDetails,
                                    AutomationId = "import.details"
                                })
                                .Cell(1, 0),
                        ])
                        .ColumnSpacing(16f))
                    .Padding(new Thickness(24f, 20f, 24f, 16f))
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(0f, 0f, 0f, 1f)
                    .Cell(0, 0),
                ScrollView(new ImportDialogBody(LauncherAppearance.Capture(),_fields, State, State.Status, State.ErrorSummary))
                    .MaxHeight(Math.Max(100f, Viewport.Height - 200f))
                    .Automation(new()
                    {
                        Name = Resources.ImportInstance,
                        AutomationId = "import.form.scroll"
                    })
                    .Cell(0, 1),
                Border(Grid(
                        [GridLength.Star(), GridLength.Star()],
                        [GridLength.Auto],
                        [
                            LauncherControls.Command(Resources.ImportConfirm, () => RunPrimaryAction())
                                .Style(LauncherButtons.DialogPrimary)
                                .MinHeight(40f)
                                .Enabled(State.CanConfirm && !Picking)
                                .Visible(State.RegisteredId is null)
                                .Automation(new()
                                {
                                    Name = Resources.ImportConfirm,
                                    AutomationId = "import.confirm"
                                })
                                .Cell(0, 0),
                            LauncherControls.OutlineCommand(State.RegisteredId is not null
                                    ? Resources.CloseLaunchSettings
                                    : State.Active ? Resources.ImportCancel : Resources.DialogCancel, () => Dismiss())
                                .Style(LauncherButtons.DialogSecondary)
                                .MinHeight(40f)
                                .Enabled(State.Active ? !State.CancelRequested : CanClose)
                                .Automation(new()
                                {
                                    Name = State.RegisteredId is not null
                                        ? Resources.CloseLaunchSettings
                                        : State.Active ? Resources.ImportCancel : Resources.DialogCancel,
                                    AutomationId = State.Active ? "import.cancel" : "import.close"
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
            .Visible(!DetailsOpen),
    ]);

    private void Dismiss()
    {
        if (State.Active)
            _ = _controller.CancelAsync();
        else if (CanClose)
            _close();
    }

    private void RunPrimaryAction() => _ = _controller.ConfirmAsync();

    private void ShowDetails()
    {
        if (!State.HasDetails || Picking)
            return;
        _detailsReturnFocus = _window.FocusedElement;
        _report.ViewHeight = Render.Bounds.Height;
        _report.ViewWidth = Render.Bounds.Width;
        _report.SetReport(State.Report);
        DetailsOpen = true;
        _focus.Request(_report);
    }

    private void BackToForm()
    {
        DetailsOpen = false;
        _focus.Request(this, _detailsReturnFocus);
    }

    private async Task PickAsync(ImportPathKind kind)
    {
        if (!Editable)
            return;
        var returnFocus = _window.FocusedElement;
        var revision = ++_pickerRevision;
        Picking = true;
        _fields.InputBlocked = true;
        try
        {
            string? path;
            if (kind == ImportPathKind.LauncherConfig)
            {
                var picker = _window.CreateFileOpenPicker();
                picker.FileTypeFilter.Add(".json");
                picker.FileTypeFilter.Add(".ini");
                path = (await picker.PickSingleFileAsync())?.Path;
            }
            else
            {
                path = (await _window.CreateFolderPicker().PickSingleFolderAsync())?.Path;
            }

            if (_disposed || revision != _pickerRevision || path is null)
                return;
            switch (kind)
            {
                case ImportPathKind.Source:
                    _controller.ChangeDirectory(path);
                    break;
                case ImportPathKind.Shared:
                    _controller.ChangeSharedDirectory(path);
                    break;
                case ImportPathKind.Game:
                    _controller.ChangeGameDirectory(path);
                    break;
                case ImportPathKind.LauncherConfig:
                    _controller.ChangeLauncherConfig(path);
                    break;
            }
        }
        catch (Exception exception)
        {
            if (!_disposed)
                _controller.ReportError(exception.Message);
        }
        finally
        {
            if (!_disposed)
            {
                Picking = false;
                _fields.InputBlocked = false;
                _focus.Request(this, returnFocus);
            }
        }
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
        return PopupKeyboardClose.Bind(_window, RequestClose, () => !Picking && !_exporting);
    }

    private void OnStateChanged(ImportState state)
    {
        if (_closing.IsClosing || _finished)
            return;
        if (State.Active && !state.Active)
            _focus.RequestIfLost(this);
        State = state;
        if (_closing.Observe())
            return;
        if (FinishIfSucceeded())
            return;
        if (DetailsOpen)
            _report.SetReport(state.Report);
    }

    private bool FinishIfSucceeded()
    {
        if (_closing.IsClosing)
            return true;
        if (_finished || State.Job?.Status != "succeeded" || !CanClose)
            return _finished;
        _finished = true;
        _close();
        Controls.LauncherToast.Show(_window, State.Status);
        return true;
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs e) => Viewport = e.Size;

    protected override void OnDispose()
    {
        _closing.Dispose();
        _disposed = true;
        _focus.Dispose();
        _pickerRevision++;
        _controller.StateChanged -= OnStateChanged;
        _window.SizeChanged -= OnSizeChanged;
        base.OnDispose();
    }
}