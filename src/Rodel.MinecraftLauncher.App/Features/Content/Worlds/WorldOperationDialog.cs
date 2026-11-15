// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Worlds;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using Sprout.WindowsAppSDK;
using System.Globalization;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed partial class WorldOperationDialog : Control
{
    private readonly LauncherClient _client;
    private readonly ContentController _controller;
    private readonly Window _window;
    private readonly string _instanceId;
    private readonly WorldAction _action;
    private readonly WorldItem? _world;
    private readonly Action _close;
    private readonly PopupPreparationClose _closing;
    private readonly PopupFocusRecovery _focus;
    private readonly WorldOperationFields _body;
    private readonly CancellationTokenSource _lifetime = new();
    private WorldArchiveRecord[] _records = [];
    private long _targetRevision;
    private bool _submitting;
    private partial bool TargetChecking { get; set; }
    private partial bool TargetOccupied { get; set; }
    private partial string TargetReadError { get; set; }
    internal Func<WorldArchiveItem, WorldArchiveRow> CreateArchive { get; }

    internal WorldOperationDialog(
        LauncherClient client,
        ContentController controller,
        Window window,
        string instanceId,
        WorldAction action,
        WorldItem? world,
        string? source,
        Action close)
    {
        _client = client;
        _controller = controller;
        _window = window;
        _instanceId = instanceId;
        _action = action;
        _world = world;
        _close = close;
        _closing = new(close, () => controller.State.Active, () => CanClose, () => !controller.State.CancelRequested && controller.State.Failure is not null);
        _focus = new(window);
        _body = new(this);
        CreateArchive = item => new(item, this);
        State = controller.State;
        PathText = source ?? string.Empty;
        FolderName = string.Empty;
        Error = string.Empty;
        TargetReadError = string.Empty;
        Archives = [];
        Selected = -1;
        Reading = Selecting;
        Picking = false;
    }

    internal partial ContentState State { get; private set; }
    internal partial string PathText { get; set; }
    internal partial string FolderName { get; private set; }
    internal partial string Error { get; private set; }
    internal partial IReadOnlyList<WorldArchiveItem> Archives { get; private set; }
    internal partial int Selected { get; private set; }
    internal partial bool Reading { get; private set; }
    internal partial bool Picking { get; private set; }
    internal bool Importing => _action == WorldAction.Import;
    private FileDropHandler Drop => field ??= new(AcceptDrop, ReceiveDrop, value => Dropping = value);
    private partial bool Dropping { get; set; }
    internal bool Exporting => _action == WorldAction.Export;
    internal bool Recovering => _action == WorldAction.Recover;
    internal bool Selecting => _action is WorldAction.Restore or WorldAction.Recover;
    internal bool CanClose => !_controller.State.Active && !Picking;
    internal bool EmptyChoices => Selecting && !Reading && Archives.Count == 0;
    internal bool HasSelection => Selected >= 0 && Selected < _records.Length;
    internal string EmptyTitle => Recovering ? Resources.WorldRecoveryEmptyTitle : Resources.WorldBackupEmptyTitle;
    internal string EmptyDescription => Recovering ? Resources.WorldRecoveryEmptyDescription : Resources.WorldBackupEmptyDescription;
    private string ChosenFolder => FolderName.Trim();
    internal bool CanCompress => Recovering && HasSelection && _records[Selected].Storage != "zip";
    internal string CompressionInfo => Recovering && HasSelection && _records[Selected].Storage == "zip" ? string.Format(Resources.WorldCompressedSize, (_records[Selected].OriginalBytes ?? 0) / 1048576d, (_records[Selected].ArchiveBytes ?? 0) / 1048576d) : string.Empty;
    internal string TargetProblem
    {
        get
        {
            if (!Recovering || !HasSelection)
                return string.Empty;
            var folder = ChosenFolder;
            if (folder.Length == 0 || folder is "." or ".." || folder.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || folder.TrimEnd(' ', '.') != folder)
                return Resources.WorldFolderInvalid;
            return TargetReadError.Length > 0
                ? TargetReadError
                : TargetOccupied ? Resources.WorldTargetOccupied : string.Empty;
        }
    }

    private bool CanSubmit => CanClose && !Reading && !TargetChecking && (Selecting ? Selected >= 0 && Selected < _records.Length : (!Importing && !Exporting) || !string.IsNullOrWhiteSpace(PathText)) && TargetProblem.Length == 0;
    private string Title => WorldText.Action(_action);
    internal string Confirmation => _action switch
    {
        WorldAction.Remove => string.Format(Resources.WorldRemoveNotice, _world?.Name),
        WorldAction.Restore => Resources.WorldRestoreNotice,
        _ => string.Empty,
    };

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Auto, GridLength.Auto],
        [
            Border(Grid(
                    [GridLength.Star(), GridLength.Auto],
                    [GridLength.Auto],
                    [
                        Text(Title)
                            .FontSize(19f).FontWeight(600).Foreground(LauncherTheme.Ink)
                            .VAlign(VerticalAlignment.Center)
                            .Cell(0, 0),
                        Text(_world?.Name ?? string.Empty)
                            .FontSize(12f).Foreground(LauncherTheme.Muted)
                            .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                            .MaxWidth(240f)
                            .VAlign(VerticalAlignment.Center)
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(12f))
                .Padding(24f, 20f)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 0f, 0f, 1f)
                .Cell(0, 0),
            DropTarget(Column([ScrollView(_body)
                .MaxHeight(Math.Max(100f, _window.ClientSize.Height - 240f)), new FileDropHint(LauncherAppearance.Capture(),Dropping).Visible(Dropping && Importing)]).Spacing(8f), Drop)
                .Cell(0, 1),
            Border(Grid(
                    [GridLength.Star(), GridLength.Star()],
                    [GridLength.Auto],
                    [
                        LauncherControls.Command(Title, () => _ = SubmitAsync())
                            .Style(LauncherButtons.DialogPrimary)
                            .MinHeight(40f)
                            .Enabled(CanSubmit)
                            .Visible(!EmptyChoices)
                            .Automation(new()
                            {
                                Name = Title,
                                AutomationId = "world.operation.confirm"
                            })
                            .Cell(0, 0),
                        LauncherControls.OutlineCommand(EmptyChoices ? Resources.CloseLaunchSettings : Resources.DialogCancel, () =>
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
                                Name = EmptyChoices ? Resources.CloseLaunchSettings : Resources.DialogCancel,
                                AutomationId = "world.operation.cancel"
                            })
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(12f))
                .Padding(24f, 16f)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 1f, 0f, 0f)
                .Cell(0, 2),
        ])
        .Width(Math.Min(600f, _window.ClientSize.Width - 48f));

    internal void Choose(int index)
    {
        if (!CanClose || index < 0 || index >= _records.Length)
            return;
        Selected = index;
        ChangeFolderName(_records[index].World!);
    }

    internal void ChangeFolderName(string value)
    {
        FolderName = value;
        _ = CheckTargetAsync();
    }

    private async Task CheckTargetAsync()
    {
        var revision = ++_targetRevision;
        TargetOccupied = false;
        TargetReadError = string.Empty;
        TargetChecking = false;
        if (!Recovering || !HasSelection || TargetProblem.Length > 0)
            return;
        TargetChecking = true;
        var folder = ChosenFolder;
        var cancellation = _lifetime.Token;
        try
        {
            await Task.Delay(150, cancellation);
            if (revision != _targetRevision)
                return;
            var occupied = await Task.Run(() =>
                {
                    var target = System.IO.Path.Combine(_client.Instances.Get(_instanceId).GameDirectory, "saves", folder);
                    return Directory.Exists(target) || File.Exists(target);
                }, cancellation);
            if (!cancellation.IsCancellationRequested && revision == _targetRevision)
                TargetOccupied = occupied;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && revision == _targetRevision)
                TargetReadError = error.Message;
        }
        finally
        {
            if (!cancellation.IsCancellationRequested && revision == _targetRevision)
                TargetChecking = false;
        }
    }

    internal async Task CompressSelectedAsync()
    {
        if (!CanClose || !CanCompress)
            return;
        var id = _records[Selected].Id!;
        var targetName = FolderName;
        await _controller.ManageWorldAsync(new(_instanceId, WorldAction.Compress, Source: id));
        if (_lifetime.IsCancellationRequested || _controller.State.Outcome != "succeeded")
            return;
        await ReadChoicesAsync();
        var index = Array.FindIndex(_records, row => row.Id == id);
        if (index >= 0)
        {
            Choose(index);
            ChangeFolderName(targetName);
        }
    }

    private bool AcceptDrop(IReadOnlyList<string> paths) => Importing && CanClose && paths.Count == 1
        && (Directory.Exists(paths[0]) || (File.Exists(paths[0]) && System.IO.Path.GetExtension(paths[0]).Equals(".zip", StringComparison.OrdinalIgnoreCase)));

    private void ReceiveDrop(IReadOnlyList<string> paths) => PathText = paths[0];

    internal async Task BrowseAsync(bool folder)
    {
        if (!CanClose)
            return;
        var focus = _window.FocusedElement;
        Picking = true;
        try
        {
            string? path;
            if (folder)
            {
                path = (await _window.CreateFolderPicker().PickSingleFolderAsync())?.Path;
            }
            else
            {
                var picker = _window.CreateFileOpenPicker();
                picker.FileTypeFilter.Add(".zip");
                path = (await picker.PickSingleFileAsync())?.Path;
            }

            if (!_lifetime.IsCancellationRequested && path is not null)
                PathText = Exporting ? System.IO.Path.Combine(path, (_world?.Folder ?? "world") + ".zip") : path;
        }
        catch (Exception error)
        {
            if (!_lifetime.IsCancellationRequested)
                Error = error.Message;
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
            {
                Picking = false;
                _focus.Request(this, focus);
            }
        }
    }

    private async Task ReadChoicesAsync()
    {
        if (!Selecting)
            return;
        Reading = true;
        try
        {
            var rows = await Task.Run(() => _action == WorldAction.Restore ? _client.Worlds.ListBackups(_instanceId, _world!.Folder) : _client.Worlds.ListRecovery(_instanceId), _lifetime.Token);
            if (_lifetime.IsCancellationRequested)
                return;
            _records = [.. rows.Where(row => !row.HasError).OrderByDescending(row => row.CreatedAt, StringComparer.Ordinal)];
            var failures = rows.Where(row => row.HasError).Select(row => row.ErrorText!).ToArray();
            if (failures.Length > 0)
                Error = string.Join(Environment.NewLine, failures);
            Archives = [.._records.Select((row, index) => new WorldArchiveItem(index, row.Name ?? row.World!, Recovering
                            ? row.Operation == "restore" ? Resources.WorldBeforeRestore : Resources.WorldRemoved
                            : Resources.WorldBackup, DateTimeOffset.TryParse(row.CreatedAt, CultureInfo.InvariantCulture, out var date) ? date.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : Resources.ValueNotAvailable))];
            Selected = -1;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!_lifetime.IsCancellationRequested)
                Error = error.Message;
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
                Reading = false;
        }
    }

    private async Task SubmitAsync()
    {
        if (!CanSubmit)
            return;
        Error = string.Empty;
        var source = Selecting
            ? _records[Selected].Id
            : Importing ? PathText.Trim() : null;
        var task = new ManageWorldTask(
            _instanceId,
            _action,
            Importing || Recovering
                ? string.IsNullOrWhiteSpace(FolderName) ? null : FolderName.Trim()
                : _world?.Folder,
            source,
            Exporting ? PathText.Trim() : null);
        _submitting = true;
        try
        {
            await _controller.ManageWorldAsync(task);
        }
        finally
        {
            _submitting = false;
        }
    }

    private void OnChanged(ContentState state)
    {
        if (_closing.IsClosing)
            return;
        if (_closing.Observe(_submitting && !state.Active && state.Outcome == "succeeded"))
        {
            if (state.Outcome == "succeeded")
                LauncherToast.Show(_window, Resources.FeatureTaskSucceeded);
            return;
        }

        State = state;
    }

    internal void RequestClose()
    {
        if (_closing.Request())
            _ = _controller.CancelAsync();
    }

    protected override IDisposable? OnMounted()
    {
        _controller.Changed += OnChanged;
        _ = ReadChoicesAsync();
        return PopupKeyboardClose.Bind(_window, RequestClose, () => !Picking);
    }

    protected override void OnDispose()
    {
        _closing.Dispose();
        _controller.Changed -= OnChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _focus.Dispose();
        base.OnDispose();
    }
}

internal sealed partial class WorldOperationFields(WorldOperationDialog dialog) : Control
{
    private Ui Body => Column([
        Text(dialog.Confirmation)
            .FontSize(13f).Foreground(LauncherTheme.Ink)
            .Wrap()
            .Margin(new Thickness(0f, 0f, 0f, 12f))
            .Visible(dialog.Confirmation.Length > 0 && !dialog.EmptyChoices),
        Column([
            Text(dialog.Importing ? Resources.WorldSource : Resources.WorldDestination)
                .FontSize(13f).Foreground(LauncherTheme.Muted),
            Grid(
                [GridLength.Star(), GridLength.Auto, GridLength.Auto],
                [GridLength.Auto],
                [
                    TextBox()
                        .Text(dialog.PathText)
                        .ReadOnly()
                        .Placeholder(dialog.Importing ? Resources.PathSourcePlaceholder : Resources.PathFolderPlaceholder)
                        .Style(LauncherStyles.Input)
                        .MinHeight(40f)
                        .Enabled(dialog.CanClose)
                        .Automation(new()
                        {
                            Name = dialog.Importing ? Resources.WorldSource : Resources.WorldDestination,
                            AutomationId = "world.operation.path"
                        })
                        .Cell(0, 0),
                    LauncherControls.OutlineCommand(
                        Row([
                            LauncherIcons.Line("folder", 18f)
                        ]),
                        () => _ = dialog.BrowseAsync(true))
                        .Style(LauncherButtons.BrowseButton)
                        .Width(40f).Height(40f)
                        .Enabled(dialog.CanClose)
                        .Visible(dialog.Importing)
                        .ToolTip(Resources.WorldChooseFolder)
                        .Automation(new()
                        {
                            Name = Resources.WorldChooseFolder,
                            AutomationId = "world.operation.choose-folder"
                        })
                        .Cell(1, 0),
                    LauncherControls.OutlineCommand(
                        Row([
                            LauncherIcons.Line("archive", 18f)
                        ]),
                        () => _ = dialog.BrowseAsync(false))
                        .Style(LauncherButtons.BrowseButton)
                        .Width(40f).Height(40f)
                        .Enabled(dialog.CanClose)
                        .Visible(dialog.Importing)
                        .ToolTip(Resources.WorldChooseZip)
                        .Automation(new()
                        {
                            Name = Resources.WorldChooseZip,
                            AutomationId = "world.operation.choose-zip"
                        })
                        .Cell(2, 0),
                ])
                .ColumnSpacing(dialog.Importing ? 8f : 0f),
            Row([
                LauncherControls.OutlineCommand(Resources.WorldChooseFolder, () => _ = dialog.BrowseAsync(true))
                    .Enabled(dialog.CanClose),
            ])
                .Visible(dialog.Exporting),
        ])
            .Spacing(8f)
            .Margin(new Thickness(0f, 0f, 0f, 12f))
            .Visible(dialog.Importing || dialog.Exporting),
        Column([
            Text(Resources.WorldFolderName)
                .FontSize(13f).Foreground(LauncherTheme.Muted),
            TextBox(dialog.FolderName, dialog.ChangeFolderName)
                .Style(LauncherStyles.Input)
                .MinHeight(40f)
                .Enabled(dialog.CanClose)
                .Automation(new()
                {
                    Name = Resources.WorldFolderName,
                    AutomationId = "world.operation.name"
                }),
        ])
            .Spacing(8f)
            .Margin(new Thickness(0f, 0f, 0f, 12f))
            .Visible(dialog.Importing),
        Column([
            Text(Resources.WorldSelectRecord)
                .FontSize(13f).Foreground(LauncherTheme.Muted),
            ItemsRepeater(dialog.Archives, static row => row.Index, dialog.CreateArchive, new StackLayout { EstimatedItemExtent = 90f }).KeyboardNavigation(),
        ])
            .Spacing(8f)
            .Margin(new Thickness(0f, 0f, 0f, 12f))
            .Visible(dialog.Selecting && dialog.Archives.Count > 0),
        Column([
            Text(Resources.WorldRecoveryFolder)
                .FontSize(12f).Foreground(LauncherTheme.Muted),
            TextBox(dialog.FolderName, dialog.ChangeFolderName)
                .Style(LauncherStyles.Input)
                .MinHeight(40f)
                .Enabled(dialog.CanClose)
                .Automation(new()
                {
                    Name = Resources.WorldRecoveryFolder,
                    AutomationId = "world.operation.recovery-name"
                }),
        ])
            .Spacing(8f)
            .Margin(new Thickness(0f, 0f, 0f, 12f))
            .Visible(dialog.Recovering && dialog.HasSelection),
        Text(dialog.CompressionInfo)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Margin(new Thickness(0f, 0f, 0f, 8f))
            .Visible(dialog.CompressionInfo.Length > 0)
            .Automation(new()
            {
                Name = dialog.CompressionInfo,
                AutomationId = "world.operation.compression-info"
            }),
        LauncherControls.OutlineCommand(Resources.WorldCompress, () => _ = dialog.CompressSelectedAsync())
            .Style(LauncherButtons.DialogHeaderButton)
            .Visible(dialog.CanCompress)
            .Enabled(dialog.CanClose)
            .HAlign(HorizontalAlignment.Left)
            .Margin(new Thickness(0f, 0f, 0f, 8f))
            .Automation(new()
            {
                Name = Resources.WorldCompress,
                AutomationId = "world.operation.compress"
            }),
        Text(dialog.TargetProblem)
            .FontSize(12f).Foreground(LauncherTheme.Warning)
            .Wrap()
            .Margin(new Thickness(0f, 0f, 0f, 8f))
            .Visible(dialog.TargetProblem.Length > 0)
            .Automation(new()
            {
                Name = dialog.TargetProblem,
                AutomationId = "world.operation.target-problem"
            }),
        Border(Column([
                LauncherIcons.Line("instances", 32f, LauncherTheme.Muted)
                    .HAlign(HorizontalAlignment.Center),
                Text(dialog.EmptyTitle)
                    .FontSize(14f).FontWeight(600).Foreground(LauncherTheme.Ink)
                    .Wrap()
                    .Align(TextAlignment.Center)
                    .Automation(new()
                    {
                        Name = dialog.EmptyTitle,
                        AutomationId = "world.recovery.empty-title"
                    }),
                Text(dialog.EmptyDescription)
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Wrap()
                    .Align(TextAlignment.Center)
                    .Automation(new()
                    {
                        Name = dialog.EmptyDescription,
                        AutomationId = "world.recovery.empty-description"
                    }),
            ])
                .Spacing(8f))
            .Padding(16f, 12f)
            .Visible(dialog.EmptyChoices && dialog.Error.Length == 0),
        Text(Resources.ContentLoading)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Margin(new Thickness(0f, 0f, 0f, 8f))
            .Visible(dialog.Reading),
        Text(dialog.Error)
            .Foreground(LauncherTheme.Error)
            .Wrap()
            .Margin(new Thickness(0f, 0f, 0f, 8f))
            .Visible(dialog.Error.Length > 0),
        Text(dialog.State.Status)
            .FontSize(12f).Foreground(dialog.State.Failure is null ? LauncherTheme.Ink : LauncherTheme.Error)
            .Wrap()
            .Margin(new Thickness(0f, 0f, 0f, 8f))
            .Visible(dialog.State.Active && dialog.State.Status.Length > 0),
        Text(dialog.State.Failure?["error"]?["message"]?.ToString() ?? string.Empty)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Margin(new Thickness(0f, 0f, 0f, 8f))
            .Visible(dialog.State.Failure is not null),
        Text(dialog.State.Progress)
            .FontSize(11f).Foreground(LauncherTheme.Muted)
            .Margin(new Thickness(0f, 0f, 0f, 8f))
            .Visible(dialog.State.Active && dialog.State.Progress.Length > 0),
        ProgressBar()
            .Indeterminate(dialog.Reading || dialog.State.Percent is null)
            .Value(dialog.State.Percent ?? 0)
            .Thickness(3f)
            .Visible(dialog.Reading || dialog.State.Active),
    ])
        .Padding(24f, 16f);
}