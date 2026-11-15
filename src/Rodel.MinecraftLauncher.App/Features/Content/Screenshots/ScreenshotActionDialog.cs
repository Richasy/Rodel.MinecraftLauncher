// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Screenshots;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

/// <summary>Owns an exact rename/removal confirmation and waits for cancellation before releasing its modal scope.</summary>
internal sealed partial class ScreenshotActionDialog : Control
{
    private readonly Window _window;
    private readonly ScreenshotFile[] _files;
    private readonly bool _rename;
    private readonly Func<string, bool, CancellationToken, Task> _submit;
    private readonly Action _close;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private bool _closeRequested;
    private bool _disposed;
    private bool _focusPending;
    private partial string Name { get; set; }
    private partial bool Busy { get; set; }
    private partial Exception? Failure { get; set; }
    internal bool CanClose => !Busy;
    private string Title => _rename ? Resources.ScreenshotRename : Resources.ScreenshotDeleteTitle;

    internal ScreenshotActionDialog(Window window, ScreenshotFile[] files, bool rename, Func<string, bool, CancellationToken, Task> submit, Action close)
    {
        _window = window;
        _files = files;
        _rename = rename;
        _submit = submit;
        _close = close;
        Name = rename ? files[0].FileName : string.Empty;
    }

    private Ui Body => Column([
        Border(Text(Title).FontSize(19f).FontWeight(600).Foreground(LauncherTheme.Ink).Wrap())
            .Padding(24f, 20f)
            .BorderBrush(LauncherTheme.Edge).BorderThickness(0f, 0f, 0f, 1f),
        ScrollView(new ScreenshotDialogFields(LauncherAppearance.Capture(), _rename, Name, value => Name = value,
                _files, Busy, Failure is null ? string.Empty : ScreenshotPanel.ErrorMessage(Failure)))
            .MaxHeight(Math.Max(100f, _window.ClientSize.Height - 260f)),
        Border(Grid(_rename ? [GridLength.Star(), GridLength.Star()] : [GridLength.Star(), GridLength.Star(), GridLength.Star()], [GridLength.Auto], [
            LauncherControls.Command(_rename ? Resources.DialogConfirm : Resources.DialogCancel, () => PrimaryAction())
                .Style(LauncherButtons.DialogPrimary).AsPrimary()
                .Enabled(!_rename || (!Busy && !string.IsNullOrWhiteSpace(Name)))
                .Automation(new()
                {
                    Name = _rename ? Resources.DialogConfirm : Resources.DialogCancel,
                    AutomationId = _rename ? "screenshot.dialog.confirm" : "screenshot.dialog.cancel"
                })
                .Cell(_rename ? 0 : 2, 0),
            LauncherControls.OutlineCommand(_rename ? Resources.DialogCancel : Resources.ScreenshotDelete, () => SecondaryAction())
                .Style(LauncherButtons.DialogSecondary)
                .Enabled(_rename || !Busy)
                .Automation(new()
                {
                    Name = _rename ? Resources.DialogCancel : Resources.ScreenshotDelete,
                    AutomationId = _rename ? "screenshot.dialog.cancel" : "screenshot.dialog.confirm"
                })
                .Cell(_rename ? 1 : 0, 0),
            LauncherControls.OutlineCommand(Resources.ScreenshotDeletePermanently, () => _ = SubmitAsync(permanent: true))
                .Style(LauncherButtons.DialogSecondary)
                .Enabled(!Busy)
                .Visible(!_rename)
                .Automation(new() { Name = Resources.ScreenshotDeletePermanently, AutomationId = "screenshot.dialog.permanent" })
                .Cell(1, 0),
        ]).ColumnSpacing(12f))
            .Padding(24f, 16f)
            .BorderBrush(LauncherTheme.Edge).BorderThickness(0f, 1f, 0f, 0f),
    ]).Width(Math.Min(_rename ? 500f : 620f, _window.ClientSize.Width - 64f));

    private void PrimaryAction()
    {
        if (_rename) _ = SubmitAsync();
        else RequestClose();
    }

    private void SecondaryAction()
    {
        if (_rename) RequestClose();
        else _ = SubmitAsync();
    }

    internal void FocusInitial()
    {
        _focusPending = true;
        TryFocusInitial();
    }

    private void TryFocusInitial()
    {
        if (!_focusPending || _disposed) return;
        if (Semantics is not { } semantics || Interaction is not { } interactions) return;
        var target = SettingsSearchLocator.Find(semantics, interactions, _rename ? "screenshot.dialog.name" : "screenshot.dialog.cancel");
        if (target?.CanTakeFocus == true
            && (_window.SetFocus(target, FocusState.Programmatic) || ReferenceEquals(_window.FocusedElement, target)))
        {
            _focusPending = false;
        }
    }

    internal void RequestClose()
    {
        if (!Busy) { _close(); return; }

        _closeRequested = true;
        _operation?.Cancel();
    }

    private async Task SubmitAsync(bool permanent = false)
    {
        if (Busy || (_rename && string.IsNullOrWhiteSpace(Name))) return;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        Busy = true;
        Failure = null;
        var completed = false;
        try { await _submit(Name, permanent, operation.Token); completed = true; }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception error) { if (!_disposed) Failure = error; }
        finally
        {
            _operation = null;
            if (!_disposed)
            {
                Busy = false;
                if (completed || _closeRequested) _close();
            }
        }
    }

    protected override IDisposable? OnMounted()
    {
        _window.LayoutUpdated += TryFocusInitial;
        return PopupKeyboardClose.Bind(_window, RequestClose, () => !_disposed);
    }

    protected override void OnDispose()
    {
        _disposed = true;
        _window.LayoutUpdated -= TryFocusInitial;
        _operation?.Cancel();
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnDispose();
    }
}

[View]
internal readonly partial record struct ScreenshotDialogFields(
    LauncherAppearance Theme,
    bool Rename,
    string Name,
    Action<string> ChangeName,
    IReadOnlyList<ScreenshotFile> Files,
    bool Busy,
    string Error)
{
    internal string SelectedNames => string.Join(Environment.NewLine, Files.Select(static file => file.FileName));

    /// <summary>Keeps long selected-file lists inside the dialog's scrollable body while its actions stay fixed.</summary>
    public Ui Body => Column([
        Text(Resources.ScreenshotFileName).FontSize(12f).Foreground(Theme.Muted).Visible(Rename),
        Flow(FlowDirection.LeftToRight, TextBox(Name, ChangeName)
                .Style(LauncherStyles.Input).MinHeight(40f).Enabled(!Busy)
                .Automation(new() { Name = Resources.ScreenshotFileName, AutomationId = "screenshot.dialog.name" }))
            .Visible(Rename),
        Text(Resources.ScreenshotDeleteNotice).FontSize(13f).Foreground(Theme.Muted).Wrap().Visible(!Rename),
        Flow(FlowDirection.LeftToRight,
            Text(SelectedNames)
                .FontSize(12f).Foreground(Theme.Ink).Wrap()
                .Automation(new() { Name = SelectedNames, AutomationId = "screenshot.dialog.files" }))
            .Visible(!Rename),
        Text(Error).FontSize(12f).Foreground(Theme.Error).Wrap().Visible(Error.Length > 0)
            .Automation(new() { Name = Error, AutomationId = "screenshot.dialog.error" }),
        ProgressBar().Indeterminate().Thickness(3f).Visible(Busy),
    ])
        .Spacing(8f)
        .Padding(24f, 20f);
}