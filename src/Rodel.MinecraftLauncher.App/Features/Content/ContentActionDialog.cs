// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Layout;
using Sprout.WindowsAppSDK;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed partial class ContentActionDialog : Control
{
    private readonly ContentController _controller;
    private readonly Window _window;
    private readonly string _instanceId;
    private readonly string _instanceName;
    private readonly ContentAction _action;
    private readonly ContentKind _kind;
    private readonly ContentItem? _item;
    private readonly Action _close;
    private readonly PopupPreparationClose _closing;
    private readonly PopupFocusRecovery _focus;

    internal ContentActionDialog(
        ContentController controller,
        Window window,
        string instanceId,
        string instanceName,
        ContentAction action,
        ContentItem? item,
        Action close)
    {
        _controller = controller;
        _window = window;
        _instanceId = instanceId;
        _instanceName = instanceName;
        _action = action;
        _kind = controller.State.Kind;
        _item = item;
        _close = close;
        _closing = new(close, () => controller.State.Active, () => CanClose, () => !controller.State.CancelRequested && controller.State.Failure is not null);
        _focus = new(window);
        State = controller.State;
        Source = string.Empty;
    }

    internal partial ContentState State { get; private set; }
    internal partial string Source { get; private set; }
    private partial bool Picking { get; set; }
    internal bool CanClose => !_controller.State.Active && !Picking;
    internal bool IsImport => _action == ContentAction.Import;
    internal string Explanation => IsImport ? string.Format(Resources.ContentImportScopeByKind, _kind.Title(), _kind.Directory()) : string.Format(Resources.ContentRemoveScope, _item?.FileName, _kind.Title(), _kind.Directory());
    private string Title => _action == ContentAction.Import ? Resources.ContentImport : Resources.ContentRemove;

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Auto, GridLength.Auto],
        [
            Border(Column([
                    Text(Title)
                        .FontSize(19f).FontWeight(600).Foreground(LauncherTheme.Ink)
                        .Wrap(),
                    Text(string.Format(Resources.ContentTargetContext, _instanceName, _kind.Title()))
                        .FontSize(12f).Foreground(LauncherTheme.Muted)
                        .Wrap(),
                ])
                    .Spacing(5f))
                .Padding(24f, 20f, 24f, 16f)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 0f, 0f, 1f)
                .Cell(0, 0),
            ScrollView(new ContentActionBody(
LauncherAppearance.Capture(),                    Explanation,
                    Source,
                    IsImport,
                    CanClose,
                    State.Active ? State.Status : string.Empty,
                    State.Failure?["error"]?["message"]?.ToString() ?? string.Empty,
                    State.Failure is not null,
                    Resources.ContentSource,
                    string.Format(Resources.BrowsePath, Resources.ContentSource),
                    value => Source = value,
                    () => _ = BrowseAsync(),
                    LauncherStyles.Input,
                    LauncherButtons.BrowseButton,
                    LauncherTheme.Ink,
                    LauncherTheme.Muted,
                    LauncherTheme.Error))
                .MaxHeight(Math.Max(80f, _window.ClientSize.Height - 240f))
                .Automation(new()
                {
                    Name = Title,
                    AutomationId = "content.operation.scroll"
                })
                .Cell(0, 1),
            Border(Grid(
                    [GridLength.Star(), GridLength.Star()],
                    [GridLength.Auto],
                    [
                        LauncherControls.Command(Resources.DialogConfirm, () => _ = ConfirmAsync())
                            .Style(LauncherButtons.DialogPrimary)
                            .MinHeight(40f)
                            .Enabled(CanClose && (_action != ContentAction.Import || !string.IsNullOrWhiteSpace(Source)))
                            .Automation(new()
                            {
                                Name = Resources.DialogConfirm,
                                AutomationId = "content.operation.confirm"
                            })
                            .Cell(0, 0),
                        LauncherControls.OutlineCommand(Resources.DialogCancel, () =>
                            {
                                if (State.Active)
                                    _controller.Cancel();
                                else
                                    _close();
                            })
                            .Style(LauncherButtons.DialogSecondary)
                            .MinHeight(40f)
                            .Enabled(!Picking && !State.CancelRequested)
                            .Automation(new()
                            {
                                Name = Resources.DialogCancel,
                                AutomationId = "content.operation.cancel"
                            })
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(12f))
                .Padding(24f, 16f, 24f, 20f)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 1f, 0f, 0f)
                .Cell(0, 2),
        ])
        .Width(Math.Min(480f, _window.ClientSize.Width - 48f));

    internal async Task BrowseAsync()
    {
        if (!CanClose)
            return;
        var returnFocus = _window.FocusedElement;
        Picking = true;
        try
        {
            var picker = _window.CreateFileOpenPicker();
            foreach (var extension in _kind == ContentKind.Mods ? new[]
            {
                ".jar",
                ".zip",
                ".litemod",
                ".disabled"
            }
                : [".zip", ".disabled"])
            {
                picker.FileTypeFilter.Add(extension);
            }

            var result = await picker.PickSingleFileAsync();
            if (result is not null)
                Source = result.Path;
        }
        catch (Exception exception)
        {
            _controller.ReportError(exception, _instanceName);
        }
        finally
        {
            Picking = false;
            _focus.Request(this, returnFocus);
        }
    }

    private async Task ConfirmAsync()
    {
        if (_closing.IsClosing || !CanClose || (_action == ContentAction.Import && string.IsNullOrWhiteSpace(Source)))
            return;
        // A removal is already confirmed. Keep execution and its errors on the content page.
        if (_action == ContentAction.Remove)
            _closing.Request();
        await _controller.ExecuteAsync(_instanceId, _action, _action == ContentAction.Import ? Source.Trim() : _item!.FileName, _kind);
        if (_action == ContentAction.Remove && _controller.State.Outcome == "succeeded")
            Controls.LauncherToast.Show(_window, Resources.FeatureTaskSucceeded);
    }

    private void OnChanged(ContentState state)
    {
        if (_closing.IsClosing)
            return;
        var wasActive = State.Active;
        if (_closing.Observe(wasActive && !state.Active && state.Outcome == "succeeded"))
        {
            if (state.Outcome == "succeeded")
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