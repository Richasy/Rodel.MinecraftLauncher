// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

/// <summary>Presents a retained download selection without owning its task lifetime.</summary>
internal sealed partial class ContentInstallDialog : Control
{
    private readonly ContentDownloads _downloads;
    private readonly Window _window;
    private readonly HomeController _home;
    private readonly Action _needInstance;
    private readonly Action _close;
    private readonly ContentInstallBody _body;
    private bool _minimized;
    private bool _confirming;
    private IReadOnlyList<InstanceSummary>? _instances;

    internal ContentInstallDialog(
        ContentDownloads downloads,
        ContentDownload entry,
        Window window,
        Action close,
        HomeController home,
        Action needInstance)
    {
        _downloads = downloads;
        Entry = entry;
        _window = window;
        _home = home;
        _needInstance = needInstance;
        _close = close;
        _body = new(this, entry.Install);
        State = entry.State;
        TransferText = entry.Task.Progress;
        TransferSpeed = entry.Speed;
        OnHomeChanged(home.State);
    }

    internal ContentDownload Entry { get; }
    internal ContentInstallStore Install => Entry.Install;
    internal partial ContentState State { get; private set; }
    internal partial string TransferText { get; private set; }
    internal partial string TransferSpeed { get; private set; }
    internal partial InstanceSummary[] Targets { get; private set; }
    internal string ProjectDescription => Entry.Project.Description;
    internal bool CanClose => _minimized || (!State.Active && !_confirming);
    private string CancelText => State.Active ? Resources.DialogCancel : Resources.CloseLaunchSettings;
    private string Title => string.Format(Resources.ContentInstallTitle, Entry.Project.Name);

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
                            .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                            .VAlign(VerticalAlignment.Center)
                            .Cell(0, 0),
                        LauncherControls.OutlineCommand(Row([LauncherIcons.Line("minimize", 18f)]), () => Minimize())
                            .Style(LauncherButtons.DialogHeaderButton)
                            .Width(32f).Height(32f)
                            .Enabled(Entry.Started)
                            .ToolTip(Resources.ContentDownloadMinimize)
                            .Automation(new()
                            {
                                Name = Resources.ContentDownloadMinimize,
                                AutomationId = "content.install.minimize"
                            })
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(12f))
                .Padding(24f, 20f, 24f, 16f)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 0f, 0f, 1f)
                .Cell(0, 0),
            ScrollView(_body)
                .MaxHeight(Math.Max(100f, _window.ClientSize.Height - 220f))
                .Automation(new() { Name = Title, AutomationId = "content.install.scroll" })
                .Cell(0, 1),
            Border(Grid(
                    [GridLength.Star(), GridLength.Star()],
                    [GridLength.Auto],
                    [
                        LauncherControls.Command(Resources.ContentReviewConfirm, () => _ = _downloads.StartAsync(Entry))
                            .Style(LauncherButtons.DialogPrimary)
                            .MinHeight(40f)
                            .Enabled(!State.Active && Install.Ready)
                            .Automation(new()
                            {
                                Name = Resources.ContentReviewConfirm,
                                AutomationId = "content.install.confirm"
                            })
                            .Cell(0, 0),
                        LauncherControls.OutlineCommand(CancelText, () => RequestClose())
                            .Style(LauncherButtons.DialogSecondary)
                            .MinHeight(40f)
                            .Enabled(!State.CancelRequested)
                            .Automation(new() { Name = CancelText, AutomationId = "content.install.cancel" })
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(12f))
                .Padding(24f, 16f, 24f, 20f)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 1f, 0f, 0f)
                .Cell(0, 2),
        ])
        .Width(Math.Min(480f, _window.ClientSize.Width - 48f));

    internal void NeedInstance() => _needInstance();
    internal void ChooseTarget(int index)
    {
        if (Entry.Started || index < 0 || index >= Targets.Length) return;
        _home.Select(Targets[index].Id);
        _ = Install.LoadAsync(new(Targets[index].Id, Targets[index].Name));
    }
    internal void Choose(int index) { if (!Entry.Started) _ = Install.ChooseAsync(index); }
    internal void ChangeVersionText(string text) { if (!Entry.Started) _ = Install.ChangeTextAsync(text); }
    internal Task LoadAsync() => Entry.Started ? Task.CompletedTask : Install.LoadAsync();
    internal Task ReviewAgainAsync() => State.Active ? Task.CompletedTask : Install.ReviewAsync();

    private void Minimize()
    {
        if (!Entry.Started) return;
        _minimized = true;
        _close();
    }

    internal async void RequestClose()
    {
        if (_confirming || State.CancelRequested) return;
        if (!State.Active) { _close(); return; }

        _confirming = true;
        try
        {
            if (await ContentDownloadConfirmation.CancelAsync(_window, Entry.Project.Name))
                await Entry.Task.CancelAsync();
        }
        finally { _confirming = false; }

        if (!Entry.Task.Active && Entry.Task.Outcome is "succeeded" or "canceled") _close();
    }

    private void OnChanged()
    {
        var active = State.Active;
        State = Entry.State;
        TransferText = Entry.Task.Progress;
        TransferSpeed = Entry.Speed;
        if (active && !State.Active && State.Outcome is "succeeded" or "canceled" && !_confirming) _close();
    }
    private void OnHomeChanged(HomePageState state)
    {
        if (ReferenceEquals(_instances, state.Instances)) return;
        _instances = state.Instances;
        Targets = [.. state.Instances.Where(instance => !instance.Unreadable)];
    }
    protected override IDisposable? OnMounted()
    {
        Entry.Task.Changed += OnChanged;
        _home.StateChanged += OnHomeChanged;
        if (!Entry.Loaded) { Entry.Loaded = true; _ = LoadAsync(); }

        return PopupKeyboardClose.Bind(_window, RequestClose, () => !_confirming);
    }
    protected override void OnDispose()
    {
        Entry.Task.Changed -= OnChanged;
        _home.StateChanged -= OnHomeChanged;
        base.OnDispose();
    }
}

internal sealed partial class ContentInstallBody(ContentInstallDialog dialog, ContentInstallStore state) : Control
{
    private IReadOnlyList<ContentPlanRow> PlanRows => state.Review?.Plan is { } plan ? plan.Nodes.Select((node, index) => ContentPlanRow.Read(node, index)).ToArray() : [];
    private bool HasDependencies => state.Review?.Plan?.Nodes.Any(node => node.Dependency) == true;
    private IReadOnlyList<ContentFinding> Findings => state.Review?.Plan is { } plan ? ContentFinding.ReadAll(plan) : [];

    private Ui Body => Column([
        Column([
            Text(Resources.ContentSelectTarget)
                .FontSize(12f).Foreground(LauncherTheme.Muted),
            LauncherControls.Choice([..dialog.Targets.Select(instance => instance.DisplayName)])
                .SelectedIndex(Array.FindIndex(dialog.Targets, instance => instance.Id == state.Target.Id))
                .OnSelectionChanged(dialog.ChooseTarget)
                .Enabled(!dialog.Entry.Started)
                .MinHeight(40f)
                .Visible(dialog.Targets.Length > 0)
                .Automation(new()
                {
                    Name = Resources.ContentSelectTarget,
                    AutomationId = "content.install.target"
                }),
            LauncherControls.Command(Resources.ContentChooseInstance, () => dialog.NeedInstance())
                .Visible(state.Target.Id.Length == 0)
                .Automation(new()
                {
                    Name = Resources.ContentChooseInstance,
                    AutomationId = "content.install.choose-instance"
                }),
        ])
            .Spacing(8f),
        Text(dialog.ProjectDescription)
            .FontSize(12f).Foreground(LauncherTheme.Ink)
            .Wrap(),
        Column([
            Text(Resources.ContentVersionSelection)
                .FontSize(14f).Foreground(LauncherTheme.Ink),
            LauncherControls.Choice([..state.Versions.Select(version => version.Label)])
                .IsEditable(true)
                .Text(state.VersionText)
                .OnTextChanged(dialog.ChangeVersionText)
                .FilterMode(ComboBoxFilterMode.Contains)
                .SelectedIndex(state.Selected)
                .OnSelectionChanged(dialog.Choose)
                .Enabled(!state.Loading && !dialog.Entry.Started && state.Versions.Count > 0)
                .MinHeight(40f)
                .Automation(new()
                {
                    Name = Resources.ContentVersionSelection,
                    AutomationId = "content.install.version"
                }),
        ])
            .Spacing(8f),
        Text(Resources.ContentVersionsLoading)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(state.Loading),
        Text(Resources.ContentFilesReviewing)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Visible(state.Planning),
        Text(Resources.ContentInstallDependencies)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(HasDependencies)
            .Automation(new()
            {
                Name = Resources.ContentInstallDependencies,
                AutomationId = "content.install.dependencies"
            }),
        ItemsRepeater(PlanRows, static row => row.Key, static row => new ContentPlanRowView(LauncherAppearance.Capture(),row), new StackLayout { EstimatedItemExtent = 76f }),
        Text(state.PlanError)
            .Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(state.PlanError.Length > 0),
        ItemsRepeater(Findings, static finding => finding.Key, static finding => new ContentFindingView(LauncherAppearance.Capture(),finding, "content.install.finding."), new StackLayout { EstimatedItemExtent = 48f }),
        Text(Resources.ContentNoCompatibleVersion)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(!state.Loading && state.Target.Id.Length > 0 && state.Versions.Count > 0 && state.Selected < 0),
        Text(Resources.ContentVersionsEmpty)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(!state.Loading && state.Versions.Count == 0 && state.LoadError.Length == 0),
        Text(string.Format(Resources.ContentVersionsFailed, state.LoadError))
            .FontSize(12f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(state.LoadError.Length > 0),
        LauncherControls.OutlineCommand(Resources.ContentRetryCatalog, () => _ = dialog.LoadAsync())
            .Enabled(!state.Loading && !dialog.State.Active)
            .MinHeight(36f)
            .HAlign(HorizontalAlignment.Left)
            .Visible(state.LoadError.Length > 0 || (!state.Loading && state.Versions.Count == 0))
            .Automation(new()
            {
                Name = Resources.ContentRetryCatalog,
                AutomationId = "content.install.retry-versions"
            }),
        LauncherControls.OutlineCommand(Resources.ContentReReview, () => _ = dialog.ReviewAgainAsync())
            .Enabled(!state.Loading && !state.Planning && !dialog.State.Active)
            .MinHeight(36f)
            .HAlign(HorizontalAlignment.Left)
            .Visible(state.NeedsReview && !state.Loading && !state.Planning && !dialog.State.Active && state.Selected >= 0)
            .Automation(new()
            {
                Name = Resources.ContentReReview,
                AutomationId = "content.install.retry-review"
            }),
        Text(dialog.State.Status)
            .FontSize(13f).Foreground(dialog.State.Failure is null ? LauncherTheme.Ink : LauncherTheme.Error)
            .Wrap()
            .Visible(dialog.State.Active && dialog.State.Status.Length > 0)
            .Automation(new()
            {
                Name = dialog.State.Status,
                AutomationId = "content.install.status",
                LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
            }),
        Text(dialog.TransferText)
            .FontSize(11f).Foreground(LauncherTheme.Muted)
            .Visible(dialog.State.Active && dialog.State.Progress.Length > 0),
        Text(dialog.TransferSpeed)
            .FontSize(11f).Foreground(LauncherTheme.Muted)
            .Visible(dialog.State.Active),
        ProgressBar()
            .Indeterminate(dialog.State.Percent is null)
            .Value(dialog.State.Percent ?? 0)
            .Thickness(3f)
            .Visible(dialog.State.Active),
        Text(dialog.State.Failure?["error"]?["message"]?.ToString() ?? string.Empty)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(dialog.State.Failure is not null),
    ])
        .Spacing(12f)
        .Padding(24f, 16f);
}