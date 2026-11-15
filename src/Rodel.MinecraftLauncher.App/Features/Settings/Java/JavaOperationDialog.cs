// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Runtime;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.WindowsAppSDK;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal enum JavaOperation
{
    AddLocal,
    Download,
    Remove
}

internal sealed partial class JavaOperationDialog : Control
{
    private readonly JavaManagerController _controller;
    private readonly Window _window;
    private readonly JavaOperation _operation;
    private readonly JavaEntry? _target;
    private readonly Action _close;
    private readonly PopupPreparationClose _closing;
    private readonly PopupFocusRecovery _focus;
    private readonly JavaCatalogList _catalog;

    internal JavaOperationDialog(
        JavaManagerController controller,
        Window window,
        JavaOperation operation,
        JavaEntry? target,
        Action close)
    {
        _controller = controller;
        _window = window;
        _operation = operation;
        _target = target;
        _close = close;
        _closing = new(close, () => controller.State.Active, () => CanClose, () => !controller.State.CancelRequested && controller.State.Error.Length > 0);
        _focus = new(window);
        _catalog = new(controller);
        State = controller.State;
        BrowseError = string.Empty;
    }

    private partial JavaManagerState State { get; set; }
    private partial bool Picking { get; set; }
    private partial string BrowseError { get; set; }
    internal bool CanClose => !_controller.State.Active && !Picking;
    private string Title => _operation == JavaOperation.Download
        ? Resources.JavaDownload
        : _operation == JavaOperation.Remove ? Resources.JavaRemove : Resources.JavaAddLocal;
    private string ConfirmLabel => _operation == JavaOperation.Download
        ? Resources.JavaInstall
        : _operation == JavaOperation.Remove ? Resources.JavaConfirmRemove : Resources.JavaAddLocal;
    private bool CanConfirm => State.Editable && CanClose && (_operation == JavaOperation.Remove
        ? _target?.Id is not null
        : _operation == JavaOperation.Download ? State.Component >= 0 && State.Component < State.Components.Length : !string.IsNullOrWhiteSpace(State.Source));

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Auto, GridLength.Auto],
        [
            Border(Text(Title)
                    .FontSize(20f).FontWeight(600).Foreground(LauncherTheme.Ink))
                .Padding(24f, 20f, 24f, 16f)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 0f, 0f, 1f)
                .Cell(0, 0),
            ScrollView(new JavaOperationBody(
LauncherAppearance.Capture(),                    State,
                    BrowseError,
                    _operation,
                    _target,
                    _controller.SetSource,
                    () => _ = BrowseAsync(),
                    () => _ = _controller.LoadCatalogAsync(),
                    _catalog,
                    Math.Clamp(_window.ClientSize.Height - 380f, 120f, 240f)))
                .MaxHeight(Math.Max(100f, _window.ClientSize.Height - 260f))
                .Automation(new()
                {
                    Name = Title,
                    AutomationId = "java.operation.scroll"
                })
                .Cell(0, 1),
            Border(Grid(
                    [GridLength.Star(), GridLength.Star()],
                    [GridLength.Auto],
                    [
                        LauncherControls.Command(ConfirmLabel, () => _ = ConfirmAsync())
                            .Style(_operation == JavaOperation.Remove ? LauncherButtons.DialogSecondary : LauncherButtons.DialogPrimary)
                            .MinHeight(40f)
                            .Enabled(CanConfirm)
                            .Automation(new()
                            {
                                Name = ConfirmLabel,
                                AutomationId = "java.operation.confirm"
                            })
                            .Cell(0, 0),
                        LauncherControls.OutlineCommand(State.Active ? Resources.FeatureTaskCancel : Resources.DialogCancel, () =>
                            {
                                if (State.Active)
                                    _ = _controller.CancelAsync();
                                else
                                    _close();
                            })
                            .MinHeight(40f)
                            .Enabled(!Picking && !State.CancelRequested)
                            .Automation(new()
                            {
                                Name = State.Active ? Resources.FeatureTaskCancel : Resources.DialogCancel,
                                AutomationId = "java.operation.cancel"
                            })
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(12f))
                .Padding(24f, 16f, 24f, 20f)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 1f, 0f, 0f)
                .Cell(0, 2),
        ])
        .Width(Math.Min(560f, _window.ClientSize.Width - 48f));

    private async Task BrowseAsync()
    {
        if (!CanClose || !State.Editable)
            return;
        var returnFocus = _window.FocusedElement;
        Picking = true;
        BrowseError = string.Empty;
        try
        {
            var picker = _window.CreateFileOpenPicker();
            foreach (var extension in new[]
            {
                ".exe",
                ".zip",
                ".gz",
                ".tgz"
            }
            )
            {
                picker.FileTypeFilter.Add(extension);
            }

            if (await picker.PickSingleFileAsync() is { } file)
                _controller.SetSource(file.Path);
        }
        catch (Exception exception)
        {
            BrowseError = exception.Message;
        }
        finally
        {
            Picking = false;
            _focus.Request(this, returnFocus);
        }
    }

    private async Task ConfirmAsync()
    {
        if (_closing.IsClosing || !CanConfirm)
            return;
        if (_operation == JavaOperation.Remove)
            _closing.Request();
        if (_operation == JavaOperation.AddLocal)
            await _controller.ImportAsync();
        else if (_operation == JavaOperation.Download)
            await _controller.InstallAsync();
        else
            await _controller.RemoveAsync(_target!.Id!);
        if (_operation == JavaOperation.Remove && _controller.Outcome == "succeeded")
            Controls.LauncherToast.Show(_window, Resources.FeatureTaskSucceeded);
    }

    private void OnChanged(JavaManagerState state)
    {
        if (_closing.IsClosing)
            return;
        var wasActive = State.Active;
        if (_closing.Observe(wasActive && !state.Active && _controller.Outcome == "succeeded"))
        {
            if (_controller.Outcome == "succeeded")
                Controls.LauncherToast.Show(_window, Resources.FeatureTaskSucceeded);
            return;
        }

        State = state;
        if (wasActive && !state.Active)
            _focus.RequestIfLost(this);
    }

    internal void RequestClose()
    {
        if (_closing.Request())
            _ = _controller.CancelAsync();
    }

    protected override IDisposable? OnMounted()
    {
        _controller.Changed += OnChanged;
        State = _controller.State;
        return PopupKeyboardClose.Bind(_window, RequestClose, () => !Picking);
    }

    protected override void OnDispose()
    {
        _closing.Dispose();
        _controller.Changed -= OnChanged;
        _focus.Dispose();
        base.OnDispose();
    }
}

[View]
internal readonly partial record struct JavaOperationBody(
    LauncherAppearance Theme,
    JavaManagerState State,
    string BrowseError,
    JavaOperation Operation,
    JavaEntry? Target,
    Action<string> SetSource,
    Action Browse,
    Action Retry,
    JavaCatalogList Catalog,
    float CatalogHeight)
{
    internal string Status => State.Loading
        ? Operation == JavaOperation.Download ? Resources.JavaLoadingCatalog : Resources.JavaScanning
        : State.Status;

    public Ui Body => Border(Column([
            Column([
                Text(Resources.JavaLocalExplanation)
                    .FontSize(13f).Foreground(Theme.Muted)
                    .Wrap(),
                Text(Resources.JavaSource)
                    .FontSize(14f).Foreground(Theme.Ink),
                Grid(
                    [GridLength.Star(), GridLength.Auto],
                    [GridLength.Auto],
                    [
                        TextBox()
                            .Text(State.Source)
                            .ReadOnly()
                            .Placeholder(Resources.PathFilePlaceholder)
                            .Style(LauncherStyles.Input)
                            .MinHeight(40f)
                            .Enabled(State.Editable)
                            .Automation(new()
                            {
                                Name = Resources.JavaSource,
                                AutomationId = "java.source"
                            })
                            .Cell(0, 0),
                        LauncherControls.OutlineCommand(
Theme,                            Row([
                                LauncherIcons.Line("folder", 18f)
                            ]),
                            Browse)
                            .Style(Theme.Buttons.BrowseButton)
                            .Width(40f).Height(40f)
                            .Enabled(State.Editable)
                            .ToolTip(Resources.JavaBrowse)
                            .Automation(new()
                            {
                                Name = Resources.JavaBrowse,
                                AutomationId = "java.browse"
                            })
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(8f),
            ])
                .Spacing(10f)
                .Visible(Operation == JavaOperation.AddLocal),
            Column([
                Text(Resources.JavaDownloadExplanation)
                    .FontSize(13f).Foreground(Theme.Muted)
                    .Wrap(),
                Text(Resources.JavaSelectVersion)
                    .FontSize(14f).Foreground(Theme.Ink)
                    .Visible(State.Components.Length > 0),
                Catalog.WithState(State)
                    .Height(CatalogHeight)
                    .Visible(State.Components.Length > 0),
                LauncherControls.OutlineCommand(Theme,Resources.JavaRetryCatalog, Retry)
                    .MinHeight(36f)
                    .HAlign(HorizontalAlignment.Left)
                    .Visible(!State.Loading && !State.Active && State.Components.Length == 0)
                    .Automation(new()
                    {
                        Name = Resources.JavaRetryCatalog,
                        AutomationId = "java.catalog.retry"
                    }),
            ])
                .Spacing(10f)
                .Visible(Operation == JavaOperation.Download),
            Column([
                Text(Target is null ? string.Empty : "Java " + Target.Version)
                    .FontSize(15f).FontWeight(600).Foreground(Theme.Ink)
                    .Automation(new()
                    {
                        Name = Target is null ? string.Empty : string.Format(Resources.JavaEntryTitle, Target.Version, Target.Path),
                        AutomationId = "java.operation.target",
                        HelpText = Target?.Managed == true ? Resources.JavaRemoveManaged : Resources.JavaRemoveExternal
                    }),
                Text(Target?.Path ?? string.Empty)
                    .FontSize(12f).Foreground(Theme.Muted)
                    .Wrap()
                    .FlowDirection(FlowDirection.LeftToRight),
                Text(Target?.Managed == true ? Resources.JavaRemoveManaged : Resources.JavaRemoveExternal)
                    .FontSize(13f).Foreground(Theme.Ink)
                    .Wrap(),
            ])
                .Spacing(10f)
                .Visible(Operation == JavaOperation.Remove),
            ProgressBar()
                .Indeterminate(true)
                .Thickness(3f)
                .Visible(State.Loading || State.Active),
            Text(Status)
                .FontSize(13f).Foreground(Theme.Ink)
                .Wrap()
                .Visible((State.Loading || State.Active) && Status.Length > 0)
                .Automation(new()
                {
                    Name = Status,
                    AutomationId = "java.operation.status",
                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                }),
            Text(State.Progress)
                .FontSize(12f).Foreground(Theme.Muted)
                .Wrap()
                .Visible(State.Active && State.Progress.Length > 0),
            Text(State.Error.Length > 0 ? State.Error : BrowseError)
                .FontSize(12f).Foreground(Theme.Error)
                .Wrap()
                .Visible(State.Error.Length > 0 || BrowseError.Length > 0)
                .Automation(new()
                {
                    Name = State.Error.Length > 0 ? State.Error : BrowseError,
                    AutomationId = "java.operation.error",
                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                }),
        ])
            .Spacing(14f))
        .Padding(24f, 20f);
}