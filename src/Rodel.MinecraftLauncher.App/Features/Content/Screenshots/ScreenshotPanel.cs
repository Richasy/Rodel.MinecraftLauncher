// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Screenshots;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.WindowsAppSDK;
using System.Diagnostics;
using System.Text.Json;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal enum ScreenshotResultKind
{
    Export,
    Recycle,
    Delete
}

/// <summary>Coordinates one instance gallery and desktop actions while all file management remains in Core.</summary>
internal sealed partial class ScreenshotPanel : Control
{
    private readonly ContentPage _page;
    private readonly LauncherClient _client;
    private readonly ScreenshotStore _store;
    private readonly ScreenshotGallery _gallery;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private MenuFlyout? _menu;
    private partial bool Active { get; set; }
    private bool _disposed;
    private partial Exception? ActionFailure { get; set; }
    private partial ScreenshotBatchResult? LastResult { get; set; }
    private partial ScreenshotResultKind ResultKind { get; set; }
    private partial string OperationLabel { get; set; }
    internal partial bool Busy { get; private set; }

    internal ScreenshotPanel(ContentPage page, LauncherClient client)
    {
        _page = page;
        _client = client;
        _store = Own(new ScreenshotStore((id, ct) => client.Screenshots.ListAsync(id, ct), new DispatcherQueueScheduler()));
        _gallery = new(_store, Open, CreateMenu);
        OperationLabel = string.Empty;
    }

    internal string InstanceId => _store.InstanceId;
    private bool Current => Active && !_disposed && InstanceId.Length > 0 && InstanceId == _page.Home.SelectedId;
    private IReadOnlyList<ScreenshotFile> Files => Current ? _store.VisibleFiles : [];
    private IReadOnlyList<ScreenshotFile> Selected => Current ? _store.Selected : [];
    private string Error => ActionFailure is not null ? string.Format(Resources.ScreenshotOperationFailed, ErrorMessage(ActionFailure))
        : Current && _store.Error.Length > 0 ? string.Format(Resources.ScreenshotLoadFailed, _store.Error)
        : Current && _store.Catalog?.Warnings is { Count: > 0 } warnings ? string.Format(Resources.ScreenshotLoadFailed, warnings[0].Message) : string.Empty;
    private string Count => string.Format(Resources.ScreenshotCount, Files.Count, Current ? _store.Files.Count : 0);
    private string EmptyMessage => _store.Loading ? Resources.ContentLoading : _store.Query.Length > 0 ? Resources.ScreenshotNoMatches : Resources.ScreenshotEmpty;

    internal Task SelectAsync(string id, bool force = false)
    {
        if (id != InstanceId)
        {
            _operation?.Cancel();
            _menu?.Hide();
            ActionFailure = null;
            LastResult = null;
        }

        return _store.SelectAsync(id, force);
    }

    internal void Activate(bool active)
    {
        Active = active;
        if (!active)
        {
            _operation?.Cancel();
            _menu?.Hide();
            _store.Suspend();
        }
    }

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Star(), GridLength.Auto],
        [
            Border(Text(Error)
                    .FontSize(12f).Foreground(LauncherTheme.Error).Wrap()
                    .Automation(new() { Name = Error, AutomationId = "screenshot.error" }))
                .Padding(20f, 8f)
                .Visible(Error.Length > 0)
                .Cell(0, 0),
            Grid(
                [GridLength.Star(), GridLength.Pixels(144f), GridLength.Auto, GridLength.Auto, GridLength.Auto],
                [GridLength.Auto],
                [
                    TextBox(_store.Query, value => _store.ChangeQuery(value))
                        .Style(LauncherStyles.Input)
                        .Placeholder(Resources.ScreenshotSearch)
                        .MinHeight(36f)
                        .Enabled(!Busy)
                        .Automation(new() { Name = Resources.ScreenshotSearch, AutomationId = "screenshot.search" })
                        .Cell(0, 0),
                    LauncherControls.Choice([Resources.ScreenshotNewest, Resources.ScreenshotOldest, Resources.ScreenshotNameSort])
                        .SelectedIndex((int)_store.Sort)
                        .OnSelectionChanged(index => _store.ChangeSort(index))
                        .MinHeight(36f)
                        .Enabled(!Busy)
                        .Automation(new() { Name = Resources.ScreenshotSort, AutomationId = "screenshot.sort" })
                        .Cell(1, 0),
                    LauncherControls.OutlineCommand(Row([LauncherIcons.Line("folder", 18f)]), () => _ = OpenFolderAsync())
                        .Style(LauncherButtons.FlatIconButton)
                        .Width(32f).Height(36f)
                        .VAlign(VerticalAlignment.Center)
                        .Enabled(Current && !Busy)
                        .ToolTip(Resources.ScreenshotOpenFolder)
                        .Automation(new() { Name = Resources.ScreenshotOpenFolder, AutomationId = "screenshot.folder" })
                        .Cell(2, 0),
                    LauncherControls.OutlineCommand(Row([LauncherIcons.Line("refresh", 18f)]), () => Refresh())
                        .Style(LauncherButtons.FlatIconButton)
                        .Width(32f).Height(36f)
                        .VAlign(VerticalAlignment.Center)
                        .Enabled(Current && !Busy)
                        .ToolTip(Resources.ContentRefresh)
                        .Automation(new() { Name = Resources.ContentRefresh, AutomationId = "screenshot.refresh" })
                        .Cell(3, 0),
                    LauncherControls.OutlineCommand(Row([LauncherIcons.Line("check", 18f)]), () => _store.BeginSelection())
                        .Style(LauncherButtons.FlatIconButton)
                        .Width(32f).Height(36f)
                        .VAlign(VerticalAlignment.Center)
                        .Enabled(Current && !Busy && Files.Count > 0)
                        .Visible(!_store.Selecting)
                        .ToolTip(Resources.ScreenshotSelect)
                        .Automation(new() { Name = Resources.ScreenshotSelect, AutomationId = "screenshot.selection.begin" })
                        .Cell(4, 0),
                ])
                .ColumnSpacing(8f)
                .Padding(20f, 0f, 20f, 8f)
                .Cell(0, 1),
            Row([
                Text(OperationLabel).FontSize(12f).Foreground(LauncherTheme.Muted).Weight(1f),
                ProgressBar().Indeterminate().Thickness(3f).Width(100f),
                LauncherControls.OutlineCommand(Resources.DialogCancel, () => _operation?.Cancel())
                    .Style(LauncherButtons.DialogSecondary)
                    .Automation(new() { Name = Resources.DialogCancel, AutomationId = "screenshot.cancel" }),
            ])
                .Spacing(12f)
                .Padding(20f, 4f)
                .Visible(Busy && OperationLabel.Length > 0)
                .Cell(0, 2),
            Overlay([
                _gallery.WithItems(LauncherAppearance.Capture(), Files, Busy),
                Grid([GridLength.Star()], [GridLength.Star(), GridLength.Auto, GridLength.Star()], [
                    Border(Text(EmptyMessage)
                            .FontSize(14f).Foreground(LauncherTheme.Muted)
                            .Align(TextAlignment.Center).Wrap()
                            .Automation(new() { Name = EmptyMessage, AutomationId = "screenshot.empty" }))
                        .Padding(24f)
                        .Cell(0, 1),
                ]).Visible(Files.Count == 0 && (_store.Loading || Error.Length == 0)),
            ])
                .Cell(0, 3),
            Border(new ScreenshotFooter(LauncherAppearance.Capture(), Count, Files.Count, Selected.Count, _store.Selecting, Busy,
                    LastResult is not null, () => _store.SelectAll(), () => _store.ClearSelection(),
                    CopySelected, () => _ = ExportAsync([.. Selected]),
                    () => ShowDialog([.. Selected], false), ShowReport))
                .Padding(20f, 10f)
                .BorderBrush(LauncherTheme.Edge).BorderThickness(0f, 1f, 0f, 0f)
                .Cell(0, 4),
        ]);

    private void Refresh()
    {
        if (!Current || Busy) return;
        ActionFailure = null;
        _ = _store.SelectAsync(InstanceId, true);
    }

    private void Open(ScreenshotFile file) => _ = RunAsync(async (id, ct) =>
    {
        var current = await Task.Run(() => _client.Screenshots.GetFile(id, file), ct);
        ct.ThrowIfCancellationRequested();
        await Task.Run(() => { using var process = Process.Start(new ProcessStartInfo(current.Path) { UseShellExecute = true }); }, ct);
    });

    private Task OpenFolderAsync() => RunAsync(async (id, ct) =>
    {
        var path = await Task.Run(() => _client.Screenshots.EnsureDirectory(id), ct);
        ct.ThrowIfCancellationRequested();
        await Task.Run(() => { using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }, ct);
    });

    private Task RevealAsync(ScreenshotFile file) => RunAsync(async (id, ct) =>
    {
        var current = await Task.Run(() => _client.Screenshots.GetFile(id, file), ct);
        ct.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.ArgumentList.Add("/select,");
        start.ArgumentList.Add(current.Path);
        await Task.Run(() => { using var process = Process.Start(start); }, ct);
    });

    private void CopySelected()
    {
        if (Selected is [var file]) _ = CopyAsync([file], false);
    }

    private Task CopyAsync(ScreenshotFile[] files, bool image) => RunAsync(async (id, ct) =>
    {
        if (files.Length == 0) return;
        var items = new List<StorageFile>();
        foreach (var file in files)
        {
            var current = await Task.Run(() => _client.Screenshots.GetFile(id, file), ct);
            items.Add(await StorageFile.GetFileFromPathAsync(current.Path));
        }

        ct.ThrowIfCancellationRequested();
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        if (image) package.SetBitmap(RandomAccessStreamReference.CreateFromFile(items[0]));
        else package.SetStorageItems(items, readOnly: true);
        Clipboard.SetContent(package);
        Clipboard.Flush();
        LauncherToast.Show(_page.Window, Resources.ScreenshotCopied);
    });

    private Task ExportAsync(ScreenshotFile[] files) => RunAsync(async (id, ct) =>
    {
        if (files.Length == 0) return;
        var directory = await _page.Window.CreateFolderPicker().PickSingleFolderAsync();
        if (directory is null) return;
        ct.ThrowIfCancellationRequested();
        var result = await _client.Screenshots.ExportAsync(id, files, directory.Path, ct);
        if (!_disposed && InstanceId == id) RecordResult(result, ScreenshotResultKind.Export);
    }, Resources.ScreenshotExport);

    private async Task RunAsync(Func<string, CancellationToken, Task> action, string? progressLabel = null)
    {
        if (!Current || Busy) return;
        var id = InstanceId;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        Busy = true;
        ActionFailure = null;
        OperationLabel = progressLabel ?? string.Empty;
        try { await action(id, operation.Token); }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!_disposed && InstanceId == id) ActionFailure = error;
        }
        finally
        {
            if (ReferenceEquals(_operation, operation))
            {
                _operation = null;
                if (!_disposed) { Busy = false; OperationLabel = string.Empty; }
            }
        }
    }

    private MenuFlyout? CreateMenu(ScreenshotFile file)
    {
        if (!Current || Busy || !_store.Files.Contains(file)) return null;
        var instanceId = InstanceId;
        MenuFlyout? menu = null;
        void Queue(Action action)
        {
            menu?.Hide();
            _page.Window.RequestAnimationFrame(_ =>
            {
                if (Current && InstanceId == instanceId && !Busy && _store.Files.Contains(file)) action();
            });
        }

        menu = new MenuFlyout(
            new MenuFlyoutItem(Resources.ScreenshotOpen, () => Queue(() => Open(file))),
            new MenuFlyoutItem(Resources.ScreenshotCopyImage, () => Queue(() => _ = CopyAsync([file], true))),
            new MenuFlyoutItem(Resources.ScreenshotCopyFiles, () => Queue(() => _ = CopyAsync([file], false))),
            new MenuFlyoutItem(Resources.ScreenshotExport, () => Queue(() => _ = ExportAsync([file]))),
            new MenuFlyoutItem(Resources.ScreenshotRename, () => Queue(() => ShowDialog([file], true))),
            new MenuFlyoutItem(Resources.ScreenshotReveal, () => Queue(() => _ = RevealAsync(file))),
            new MenuFlyoutItem(Resources.ScreenshotDeleteTitle, () => Queue(() => ShowDialog([file], false))))
            .Style(LauncherMenus.CommandMenu);
        _menu = menu;
        return menu;
    }

    private void ShowDialog(ScreenshotFile[] files, bool rename)
    {
        if (!Current || Busy || files.Length == 0 || ModalPopup.IsOpen(_page.Window)) return;
        var id = InstanceId;
        ScreenshotActionDialog? dialog = null;
        _page.ShowPopup(close => dialog = new(_page.Window, files, rename, async (name, permanent, ct) =>
        {
            if (rename)
            {
                await _client.Screenshots.RenameAsync(id, files[0], name, ct);
            }
            else
            {
                var result = permanent
                    ? await _client.Screenshots.DeleteAsync(id, files, ct)
                    : await _client.Screenshots.RecycleAsync(id, files, ct);
                if (!_disposed && InstanceId == id)
                    RecordResult(result, permanent ? ScreenshotResultKind.Delete : ScreenshotResultKind.Recycle);
            }

            if (!_disposed && InstanceId == id) await _store.SelectAsync(id, true);
        }, close), () => dialog?.CanClose != false,
            rename ? Resources.ScreenshotRename : Resources.ScreenshotDeleteTitle, "screenshot.dialog",
            opened: () => dialog?.FocusInitial(), requestClose: () => dialog?.RequestClose());
    }

    private void RecordResult(ScreenshotBatchResult result, ScreenshotResultKind kind)
    {
        LastResult = result;
        ResultKind = kind;
        if (!Current) return;
        LauncherToast.Show(_page.Window, result.Canceled ? Resources.InstanceOperationCanceled : Summary(result, kind));
    }

    private static string Summary(ScreenshotBatchResult result, ScreenshotResultKind kind) => string.Format(
        kind switch
        {
            ScreenshotResultKind.Export => Resources.ScreenshotExportResult,
            ScreenshotResultKind.Delete => Resources.ScreenshotPermanentDeleteResult,
            _ => Resources.ScreenshotRecycleResult,
        }, result.Completed, result.Items.Count - result.Completed);

    private void ShowReport()
    {
        if (!Current || LastResult is not { } result || ModalPopup.IsOpen(_page.Window)) return;
        var lines = new List<string> { result.Canceled ? Resources.InstanceOperationCanceled : Summary(result, ResultKind) };
        foreach (var item in result.Items)
        {
            lines.Add(item.FileName);
            lines.Add(item.ErrorCode is { } code ? ErrorMessage(code, item.Error ?? string.Empty) : item.Destination ?? string.Empty);
        }

        var readable = string.Join(Environment.NewLine, lines);
        var raw = JsonSerializer.Serialize(result, ScreenshotJsonContext.Default.ScreenshotBatchResult);
        DetailReportPopup? report = null;
        _page.ShowPopup(close =>
        {
            report = new(_page.Window, Resources.ContentOperationDetails, readable, close,
                Math.Min(600f, _page.Window.ClientSize.Width - 72f), "screenshot.report");
            report.SetReport(readable, raw);
            return report;
        }, () => report?.Exporting != true, Resources.ContentOperationDetails, "screenshot.report.popup");
    }

    internal static string ErrorMessage(Exception error) => error is LauncherOperationException operation
        ? ErrorMessage(operation.Code, operation.Message) : error.Message;

    private static string ErrorMessage(string code, string fallback) => code switch
    {
        "SCREENSHOT_CHANGED" => Resources.ScreenshotChanged,
        "SCREENSHOT_DESTINATION_EXISTS" => Resources.ScreenshotDestinationExists,
        "SCREENSHOT_RECYCLE_UNAVAILABLE" => Resources.ScreenshotRecycleUnavailable,
        _ => fallback,
    };

    private void OnActivated(object? sender, EventArgs args)
    {
        if (Current && !Busy && !ModalPopup.IsOpen(_page.Window)) Refresh();
    }

    protected override IDisposable? OnMounted()
    {
        _page.Window.Activated += OnActivated;
        return _gallery.BindKeyboard(_page.Window);
    }

    protected override void OnDispose()
    {
        _disposed = true;
        Active = false;
        _operation?.Cancel();
        _lifetime.Cancel();
        _lifetime.Dispose();
        _menu?.Hide();
        _page.Window.Activated -= OnActivated;
        base.OnDispose();
    }
}