// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Content;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed partial class DependencyRepairDialog : Control
{
    private readonly LauncherClient _client;
    private readonly ContentController _controller;
    private readonly Window _window;
    private readonly string _instanceId;
    private readonly string _sessionId;
    private readonly string _instanceName;
    private readonly Action _close;
    private readonly PopupPreparationClose _closing;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PopupFocusRecovery _focus;
    private readonly DependencyRepairBody _body;
    private readonly Dictionary<string, string> _selections = [with(StringComparer.Ordinal)];
    private readonly Dictionary<string, string> _invalidInputs = [with(StringComparer.Ordinal)];
    private readonly Dictionary<string, ContentVersion[]> _catalogues = [with(StringComparer.Ordinal)];
    private readonly Dictionary<string, int> _keys = [with(StringComparer.Ordinal)];
    private CancellationTokenSource? _planning;
    private long _revision;
    private DependencyRepairReview? _review;

    internal DependencyRepairDialog(
        LauncherClient client,
        ContentController controller,
        Window window,
        string instanceId,
        string sessionId,
        string instanceName,
        Action close)
    {
        _client = client;
        _controller = controller;
        _window = window;
        _instanceId = instanceId;
        _sessionId = sessionId;
        _instanceName = instanceName;
        _close = close;
        _closing = new(close, () => controller.State.Active, () => CanClose, () => !controller.State.CancelRequested && controller.State.Failure is not null);
        _focus = new(window);
        _body = new(this);
        CreateRow = row => new(row, Choose, ChangeVersionText);
        State = controller.State;
        Issues = string.Empty;
        Context = string.Empty;
        Rows = [];
        Ready = false;
        Planning = true;
        Viewport = window.ClientSize;
    }

    internal partial ContentState State { get; private set; }
    internal partial string Issues { get; private set; }
    internal partial string Context { get; private set; }
    internal partial IReadOnlyList<DependencyRepairRow> Rows { get; private set; }
    internal partial bool Ready { get; private set; }
    internal partial bool Planning { get; private set; }
    internal partial bool NeedsReview { get; private set; }
    private partial Size Viewport { get; set; }
    internal Func<DependencyRepairRow, DependencyRepairRowView> CreateRow { get; }
    internal bool CanClose => !_controller.State.Active;
    internal bool Editable => !State.Active && !Planning;
    private float DialogWidth => Math.Min(720f, Viewport.Width - 48f);

    private int KeyFor(string project)
    {
        if (!_keys.TryGetValue(project, out var key))
            _keys[project] = key = _keys.Count;
        return key;
    }

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Auto, GridLength.Auto],
        [
            Border(Column([
                    Text(Resources.DependencyRepairTitle)
                        .FontSize(19f).FontWeight(600).Foreground(LauncherTheme.Ink)
                        .Wrap(),
                    Text(_instanceName)
                        .FontSize(12f).Foreground(LauncherTheme.Muted)
                        .Wrap(),
                ])
                    .Spacing(5f))
                .Padding(24f, 20f, 24f, 16f)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 0f, 0f, 1f)
                .Cell(0, 0),
            ScrollView(_body)
                .MaxHeight(Math.Max(100f, Viewport.Height - 220f))
                .Automation(new()
                {
                    Name = Resources.DependencyRepairTitle,
                    AutomationId = "content.repair.review"
                })
                .Cell(0, 1),
            Border(Grid(
                    [GridLength.Star(), GridLength.Star()],
                    [GridLength.Auto],
                    [
                        LauncherControls.Command(Resources.DependencyRepairConfirm, () => _ = ConfirmAsync())
                            .Style(LauncherButtons.DialogPrimary)
                            .MinHeight(40f)
                            .Enabled(Ready && !State.Active && !Planning)
                            .Automation(new()
                            {
                                Name = Resources.DependencyRepairConfirm,
                                AutomationId = "content.repair.confirm"
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
                            .Enabled(!State.CancelRequested)
                            .Automation(new()
                            {
                                Name = Resources.DialogCancel,
                                AutomationId = "content.repair.cancel"
                            })
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(12f))
                .Padding(24f, 16f, 24f, 20f)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 1f, 0f, 0f)
                .Cell(0, 2),
        ])
        .Width(DialogWidth);

    private void Choose(string project, int index)
    {
        var row = Rows.FirstOrDefault(item => item.ProjectId == project);
        if (!Editable || row is null || index < 0 || index >= row.Versions.Count)
            return;
        SelectVersion(row, index, row.Versions[index].Number);
    }

    private void ChangeVersionText(string project, string text)
    {
        var row = Rows.FirstOrDefault(item => item.ProjectId == project);
        if (!Editable || row is null || row.VersionText == text)
            return;
        var matches = row.Versions.Select((version, index) => (version, index)).Where(item => item.version.Number == text || item.version.Id == text).Take(2).ToArray();
        SelectVersion(row, matches.Length == 1 ? matches[0].index : -1, text);
    }

    private void SelectVersion(DependencyRepairRow row, int index, string text)
    {
        if (row.Selected == index && row.VersionText == text && row.Verified)
            return;
        Ready = false;
        Rows = [..Rows.Select(item => item.ProjectId == row.ProjectId ? item with
                {
                    Selected = index,
                    VersionText = text,
                    Verified = false
                } : item)];
        if (index < 0)
        {
            _invalidInputs[row.ProjectId] = text;
            return;
        }

        _invalidInputs.Remove(row.ProjectId);
        _selections[row.ProjectId] = row.Versions[index].Id;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var revision = ++_revision;
        _planning?.Cancel();
        _planning?.Dispose();
        _planning = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var ct = _planning.Token;
        var selections = new Dictionary<string, string>(_selections, StringComparer.Ordinal);
        Planning = true;
        Ready = false;
        NeedsReview = false;
        Issues = string.Empty;
        _review = null;
        Rows = [.. Rows.Select(row => row with { Editable = false })];
        try
        {
            var review = await Task.Run(() => _client.PlanDependencyRepairAsync(instanceId: _instanceId, sessionId: _sessionId, cancellationToken: ct, versionSelections: selections), ct);
            if (ct.IsCancellationRequested || revision != _revision)
                return;
            var plan = review.Plan;
            var minecraft = review.Context.MinecraftVersion!;
            var loader = review.Context.Loader!;
            var rows = new List<DependencyRepairRow>();
            foreach (var file in plan.Nodes)
            {
                var project = file.ProjectId;
                var key = project + "\0" + minecraft + "\0" + loader;
                if (!_catalogues.TryGetValue(key, out var versions))
                {
                    var values = await _client.ContentPlatform.GetVersionsAsync(
                        "modrinth",
                        project,
                        minecraft,
                        loader,
                        ct);
                    if (ct.IsCancellationRequested || revision != _revision)
                        return;
                    versions = [.. values.OfType<JsonObject>().Select(ContentVersion.Read)];
                    _catalogues[key] = versions;
                }

                var selected = Array.FindIndex(versions, version => version.Id == file.VersionId);
                var invalid = _invalidInputs.GetValueOrDefault(project);
                rows.Add(new(
                        project,
                        file.Title ?? project,
                        file.IconUrl,
                        string.Join(", ", file.RequestedBy ?? []),
                        versions,
                        invalid is null ? selected : -1,
                        invalid ?? (selected >= 0 ? versions[selected].Number : file.Upstream["version_number"]?.ToString() ?? string.Empty),
                        invalid is null && selected >= 0,
                        DialogWidth < 560f,
                        true)
                {
                    Key = KeyFor(project),
                    ReusesLocal = file.ReuseLocal is not null
                });
            }

            if (ct.IsCancellationRequested || revision != _revision)
                return;
            _review = review;
            var canApply = plan.CanApply;
            if (!canApply)
            {
                rows.AddRange(Rows.Where(old => rows.All(item => item.ProjectId != old.ProjectId)).Select(old => old with { Verified = false }));
            }

            Rows = rows;
            Context = string.Format(Resources.DependencyRepairContext, minecraft, loader);
            var issues = new StringBuilder();
            foreach (var item in review.Unresolved)
            {
                issues.AppendFormat(
                    Resources.DependencyRepairUnresolved,
                    item.DependencyId,
                    item.RequestedBy,
                    item.Range,
                    item.Reason).AppendLine();
            }

            if (plan.Nodes.Count == 0)
                issues.AppendLine(Resources.DependencyRepairEmpty);
            if (plan.Warnings is { Count: > 0 } warnings)
                issues.AppendLine(Resources.DependencyRepairWarnings).AppendLine(JsonSerializer.SerializeToNode(warnings, ContentJsonContext.PlanFindings)!.ToJsonString(new() { WriteIndented = true }));
            Issues = issues.ToString();
            Ready = canApply && Rows.All(row => row.Verified);
            NeedsReview = !Ready;
            _focus.RequestIfLost(this);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!ct.IsCancellationRequested && revision == _revision)
            {
                Issues = string.Format(Resources.DependencyRepairFailed, exception.Message);
                NeedsReview = true;
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested && revision == _revision)
            {
                Planning = false;
                Rows = [.. Rows.Select(row => row with { Editable = true })];
            }
        }
    }

    private async Task ConfirmAsync()
    {
        if (!Ready || Planning || State.Active || _review is null)
            return;
        var reviewId = _review.Id;
        Ready = false;
        _review = null;
        await _controller.RepairDependenciesAsync(_instanceId, reviewId);
        if (_lifetime.IsCancellationRequested)
            return;
        if (_controller.State.Outcome != "succeeded")
        {
            NeedsReview = true;
            _focus.RequestIfLost(this);
        }
    }

    internal async Task ReviewAgainAsync()
    {
        if (!Editable || _lifetime.IsCancellationRequested)
            return;
        foreach (var row in Rows.Where(row => row.Verified && row.Selected >= 0 && row.Selected < row.Versions.Count))
            _selections[row.ProjectId] = row.Versions[row.Selected].Id;
        _controller.ClearOutcome();
        await LoadAsync();
    }

    private void OnChanged(ContentState state)
    {
        if (_closing.IsClosing)
            return;
        var wasActive = State.Active;
        if (_closing.Observe(wasActive && !state.Active && state.Outcome == "succeeded"))
            return;
        State = state;
        Rows = [.. Rows.Select(row => row with { Editable = !state.Active && !Planning })];
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs e)
    {
        Viewport = e.Size;
        Rows = [.. Rows.Select(row => row with { Compact = DialogWidth < 560f })];
    }

    internal void RequestClose()
    {
        if (_closing.Request())
            _ = _controller.CancelAsync();
    }

    protected override IDisposable? OnMounted()
    {
        _controller.Changed += OnChanged;
        _window.SizeChanged += OnSizeChanged;
        _ = LoadAsync();
        return PopupKeyboardClose.Bind(_window, RequestClose, () => true);
    }

    protected override void OnDispose()
    {
        _closing.Dispose();
        _controller.Changed -= OnChanged;
        _window.SizeChanged -= OnSizeChanged;
        _lifetime.Cancel();
        _planning?.Cancel();
        _planning?.Dispose();
        _lifetime.Dispose();
        _focus.Dispose();
        base.OnDispose();
    }
}

internal sealed partial class DependencyRepairBody(DependencyRepairDialog dialog) : Control
{
    private Ui Body => Column([
        Text(dialog.Context)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Visible(dialog.Context.Length > 0),
        Text(Resources.DependencyRepairPlanning)
            .Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(dialog.Planning)
            .Automation(new()
            {
                Name = Resources.DependencyRepairPlanning,
                AutomationId = "content.repair.planning"
            }),
        ItemsRepeater(dialog.Rows, static row => row.Key, dialog.CreateRow, new StackLayout { EstimatedItemExtent = 104f }).KeyboardNavigation(singleTabStop: false),
        Text(dialog.Issues)
            .Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(dialog.Issues.Length > 0)
            .Automation(new()
            {
                Name = dialog.Issues,
                AutomationId = "content.repair.issues"
            }),
        LauncherControls.OutlineCommand(dialog.Rows.Count == 0 ? Resources.ContentRetryCatalog : Resources.ContentReReview, () => _ = dialog.ReviewAgainAsync())
            .MinHeight(36f)
            .HAlign(HorizontalAlignment.Left)
            .Enabled(dialog.Editable)
            .Visible(dialog.NeedsReview && !dialog.State.Active && !dialog.Planning)
            .Automation(new()
            {
                Name = dialog.Rows.Count == 0 ? Resources.ContentRetryCatalog : Resources.ContentReReview,
                AutomationId = "content.repair.retry"
            }),
        Text(dialog.State.Status)
            .Foreground(dialog.State.Failure is null ? LauncherTheme.Ink : LauncherTheme.Error)
            .Wrap()
            .Visible(dialog.State.Active && dialog.State.Status.Length > 0)
            .Automation(new()
            {
                Name = dialog.State.Status,
                AutomationId = "content.repair.status",
                LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
            }),
        Text(dialog.State.Failure?["error"]?["message"]?.ToString() ?? string.Empty)
            .Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(dialog.State.Failure is not null),
        Text(dialog.State.Progress)
            .FontSize(11f).Foreground(LauncherTheme.Muted)
            .Visible(dialog.State.Active && dialog.State.Progress.Length > 0),
        ProgressBar()
            .Indeterminate(dialog.Planning || dialog.State.Percent is null)
            .Value(dialog.State.Percent ?? 0)
            .Thickness(3f)
            .Visible(dialog.Planning || dialog.State.Active),
    ])
        .Spacing(12f)
        .Padding(24f, 20f);
}