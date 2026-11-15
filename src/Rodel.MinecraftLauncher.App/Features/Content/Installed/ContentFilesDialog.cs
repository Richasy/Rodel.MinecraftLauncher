// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Localization;
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
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed partial class ContentFilesDialog : Control
{
    private readonly LauncherClient _client;
    private readonly ContentController _controller;
    private readonly HomeController _home;
    private readonly Window _window;
    private readonly Action _close;
    private readonly PopupPreparationClose _closing;
    private readonly ContentFilesBody _body;
    private readonly PopupFocusRecovery _focus;
    private readonly ContentIconReader _icons = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, string> _kinds = [with(StringComparer.OrdinalIgnoreCase)];
    private readonly Dictionary<string, int> _keys = [with(StringComparer.OrdinalIgnoreCase)];
    private CancellationTokenSource? _planning;
    private ContentReview? _review;
    private long _revision;
    private bool _disposed;
    private string[] _sources;

    internal ContentFilesDialog(
        LauncherClient client,
        ContentController controller,
        HomeController home,
        Window window,
        string instanceId,
        IReadOnlyList<string> sources,
        Action close)
    {
        _client = client;
        _controller = controller;
        _home = home;
        _window = window;
        _close = close;
        _closing = new(close, () => _controller.State.Active, () => CanClose, () => !_controller.State.CancelRequested && _controller.State.Failure is not null);
        _body = new(this);
        _focus = new(window);
        CreateRow = row => new(row, ChooseKind, Remove);
        State = controller.State;
        Home = home.State;
        TargetId = instanceId;
        _sources = [.. sources.Distinct(StringComparer.OrdinalIgnoreCase)];
        Rows = [];
        Issues = string.Empty;
        Ready = false;
        Planning = false;
        Viewport = window.ClientSize;
    }

    internal partial ContentState State { get; private set; }
    internal partial HomePageState Home { get; private set; }
    internal string TargetId { get; }
    internal partial IReadOnlyList<ContentFileReviewRow> Rows { get; private set; }
    internal partial string Issues { get; private set; }
    internal partial bool Ready { get; private set; }
    internal partial bool Planning { get; private set; }
    private partial Size Viewport { get; set; }
    internal Func<ContentFileReviewRow, ContentFileReviewView> CreateRow { get; }
    internal bool CanClose => !_controller.State.Active;
    internal bool Editable => !State.Active;
    internal InstanceSummary[] Instances => [.. Home.Instances.Where(instance => !instance.Unreadable)];
    internal bool HasTarget => Instances.Any(instance => instance.Id == TargetId);
    internal string TargetName => Instances.FirstOrDefault(instance => instance.Id == TargetId)?.DisplayName ?? Resources.ContentNoTarget;
    private string ConfirmLabel => Rows.Count > 0 ? string.Format(Resources.ContentFilesConfirm, Rows.Count) : Resources.ContentReviewConfirm;

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Auto, GridLength.Auto],
        [
            Border(Grid(
                    [GridLength.Star(), GridLength.Auto],
                    [GridLength.Auto],
                    [
                        Text(Resources.ContentFromFiles)
                            .FontSize(19f).FontWeight(600).Foreground(LauncherTheme.Ink)
                            .Wrap()
                            .VAlign(VerticalAlignment.Center)
                            .Cell(0, 0),
                        Border(Text(TargetName)
                                .FontSize(12f).Foreground(LauncherTheme.Muted)
                                .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed())
                            .Background(LauncherTheme.ControlBackground)
                            .BorderBrush(LauncherTheme.Edge)
                            .BorderThickness(1f)
                            .Padding(8f, 4f)
                            .MaxWidth(Math.Min(220f, Math.Max(80f, (Viewport.Width - 96f) * 0.35f)))
                            .VAlign(VerticalAlignment.Center)
                            .Visible(HasTarget && !string.IsNullOrWhiteSpace(TargetName))
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(HasTarget && !string.IsNullOrWhiteSpace(TargetName) ? 12f : 0f))
                .Padding(24f, 20f, 24f, 16f)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 0f, 0f, 1f)
                .Cell(0, 0),
            ScrollView(_body)
                .MaxHeight(Math.Max(100f, Viewport.Height - 260f))
                .Automation(new()
                {
                    Name = Resources.ContentFromFiles,
                    AutomationId = "content.files.scroll"
                })
                .Cell(0, 1),
            Border(Grid(
                    [GridLength.Star(), GridLength.Star()],
                    [GridLength.Auto],
                    [
                        LauncherControls.Command(ConfirmLabel, () => _ = ConfirmAsync())
                            .Style(LauncherButtons.DialogPrimary)
                            .MinHeight(40f)
                            .Enabled(Ready && HasTarget && Editable && !Planning)
                            .Automation(new()
                            {
                                Name = ConfirmLabel,
                                AutomationId = "content.files.confirm"
                            })
                            .Cell(0, 0),
                        LauncherControls.OutlineCommand(Resources.DialogCancel, () => Cancel())
                            .Style(LauncherButtons.DialogSecondary)
                            .MinHeight(40f)
                            .Enabled(!State.CancelRequested)
                            .Automation(new()
                            {
                                Name = Resources.DialogCancel,
                                AutomationId = "content.files.cancel"
                            })
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(12f))
                .Padding(24f, 16f, 24f, 20f)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 1f, 0f, 0f)
                .Cell(0, 2),
        ])
        .Width(Math.Min(680f, Viewport.Width - 48f));

    private void ChooseKind(string source, int index)
    {
        var row = Rows.FirstOrDefault(item => item.Source == source);
        if (!Editable || row is null || index < 0 || index >= row.Candidates.Count || row.Selected == index)
            return;
        if (row.Candidates[index] is { } kind)
            _kinds[source] = kind.Directory();
        else
            _kinds.Remove(source);
        Rows = [.. Rows.Select(item => item.Source == source ? item with { Selected = index } : item)];
        _ = ReviewAsync();
    }

    private void Remove(string source)
    {
        if (!Editable)
            return;
        var row = Rows.FirstOrDefault(item => item.Source == source);
        if (row is null)
            return;
        // Core returns absolute source keys; preserve the original spelling of all remaining inputs.
        _sources = [.. _sources.Where(input => !SameSource(input, source))];
        _kinds.Remove(source);
        Rows = WithSeparators(Rows.Where(item => item.Source != source));
        _ = ReviewAsync();
        _focus.RequestIfLost(this);
    }

    private static bool SameSource(string input, string source)
    {
        try
        {
            return string.Equals(System.IO.Path.GetFullPath(input), source, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return string.Equals(input, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task ReviewAsync()
    {
        var revision = ++_revision;
        _planning?.Cancel();
        _planning?.Dispose();
        _planning = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var ct = _planning.Token;
        Ready = false;
        _review = null;
        Issues = string.Empty;
        Planning = false;
        var sources = _sources;
        if (!HasTarget || sources.Length == 0 || State.Active)
            return;
        var target = TargetId;
        var kinds = new Dictionary<string, string>(_kinds, StringComparer.OrdinalIgnoreCase);
        Planning = true;
        try
        {
            var review = await Task.Run(() => _client.ReviewContentFilesAsync(target, sources, kinds, ct), ct);
            if (ct.IsCancellationRequested || revision != _revision || _disposed)
                return;
            var rows = new List<ContentFileReviewRow>();
            foreach (var item in review.Items!)
            {
                var source = item.Source!;
                if (!_keys.TryGetValue(source, out var key))
                    _keys[source] = key = _keys.Count;
                var candidates = (item.Candidates ?? []).Select(candidate => ReadKind(candidate.Kind)).Where(candidate => candidate.HasValue).Distinct().ToArray();
                if (candidates.Length == 0)
                    candidates = [null, ContentKind.Mods, ContentKind.ResourcePacks, ContentKind.Shaders];
                var kind = ReadKind(item.Kind);
                var content = item.Inspection is { } inspection
                    ? ContentItem.Read(inspection) with { StoredPath = source }
                    : new ContentItem(item.FileName, item.FileName, string.Empty, false, false);
                var iconItem = content;
                if (kind is { } contentKind)
                    content = content with { ReadIcon = token => _icons.ReadLocalAsync(string.Empty, iconItem, contentKind, token) };
                rows.Add(new(
                        key,
                        source,
                        item.FileName,
                        content,
                        item.Issue ?? string.Empty,
                        item.Action == "replace"
                            ? string.Join(", ", item.Replacements!.Select(entry => entry.FileName))
                            : string.Empty,
                        candidates,
                        Array.IndexOf(candidates, kind),
                        Editable));
            }

            Rows = WithSeparators(rows);
            _review = review;
            Ready = rows.Count > 0 && review.CanApply == true && target == TargetId;
            _focus.RequestIfLost(this);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!ct.IsCancellationRequested && revision == _revision && !_disposed)
                Issues = string.Format(Resources.ContentReviewFailed, LauncherErrorText.Describe(exception));
        }
        finally
        {
            if (!ct.IsCancellationRequested && revision == _revision && !_disposed)
                Planning = false;
        }
    }

    private static ContentFileReviewRow[] WithSeparators(IEnumerable<ContentFileReviewRow> rows)
    {
        var items = rows.ToArray();
        return [.. items.Select((row, index) => row with { HasSeparator = index < items.Length - 1 })];
    }

    private static ContentKind? ReadKind(string? kind) => kind switch
    {
        "mods" => ContentKind.Mods,
        "resourcepacks" => ContentKind.ResourcePacks,
        "shaderpacks" => ContentKind.Shaders,
        _ => null,
    };

    private async Task ConfirmAsync()
    {
        if (!Ready || !HasTarget || !Editable || Planning || _review is null)
            return;
        var target = TargetId;
        var reviewId = _review.Id;
        Ready = false;
        await _controller.ApplyReviewAsync(target, ContentKind.Mods, reviewId);
        if (_disposed)
            return;
        if (_controller.State.Outcome != "succeeded")
        {
            await ReviewAsync();
            _focus.RequestIfLost(this);
        }
    }

    private void Cancel()
    {
        if (_controller.State.Active)
            _controller.Cancel();
        else
            _close();
    }

    private void OnChanged(ContentState state)
    {
        if (_closing.IsClosing)
            return;
        var wasActive = State.Active;
        if (_closing.Observe(wasActive && !state.Active && state.Outcome == "succeeded"))
            return;
        State = state;
        if (state.Active)
            Ready = false;
        Rows = [.. Rows.Select(row => row with { Editable = Editable })];
        if (wasActive && !state.Active)
            _focus.RequestIfLost(this);
    }

    private void OnHomeChanged(HomePageState state)
    {
        var hadTarget = HasTarget;
        Home = state;
        if (hadTarget != HasTarget && !State.Active)
        {
            Rows = [];
            _ = ReviewAsync();
        }
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs e) => Viewport = e.Size;

    internal void RequestClose()
    {
        if (_closing.Request())
            _ = _controller.CancelAsync();
    }

    protected override IDisposable? OnMounted()
    {
        _controller.Changed += OnChanged;
        _home.StateChanged += OnHomeChanged;
        _window.SizeChanged += OnSizeChanged;
        State = _controller.State;
        Home = _home.State;
        _ = ReviewAsync();
        return PopupKeyboardClose.Bind(_window, RequestClose, () => true);
    }

    protected override void OnDispose()
    {
        _closing.Dispose();
        _disposed = true;
        _controller.Changed -= OnChanged;
        _home.StateChanged -= OnHomeChanged;
        _window.SizeChanged -= OnSizeChanged;
        _lifetime.Cancel();
        _planning?.Cancel();
        _planning?.Dispose();
        _lifetime.Dispose();
        _focus.Dispose();
        _icons.Dispose();
        base.OnDispose();
    }
}

internal sealed partial class ContentFilesBody(ContentFilesDialog dialog) : Control
{
    private Ui Body => Column([
        Text(Resources.ContentFilesReviewing)
            .Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(dialog.Planning)
            .Automation(new()
            {
                Name = Resources.ContentFilesReviewing,
                AutomationId = "content.files.planning"
            }),
        ItemsRepeater(dialog.Rows, static row => row.Key, dialog.CreateRow, new StackLayout { EstimatedItemExtent = 104f }).KeyboardNavigation(singleTabStop: false),
        Text(dialog.Issues)
            .Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(dialog.Issues.Length > 0)
            .Automation(new()
            {
                Name = dialog.Issues,
                AutomationId = "content.files.issues"
            }),
        Text(dialog.State.Status)
            .Foreground(dialog.State.Failure is null ? LauncherTheme.Ink : LauncherTheme.Error)
            .Wrap()
            .Visible(dialog.State.Active && dialog.State.Status.Length > 0)
            .Automation(new()
            {
                Name = dialog.State.Status,
                AutomationId = "content.files.status",
                LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
            }),
        Text(dialog.State.Failure?["error"]?["message"]?.ToString() ?? string.Empty)
            .Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(dialog.State.Failure is not null),
        Text(dialog.State.Progress)
            .FontSize(11f).Foreground(LauncherTheme.Muted)
            .Wrap()
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