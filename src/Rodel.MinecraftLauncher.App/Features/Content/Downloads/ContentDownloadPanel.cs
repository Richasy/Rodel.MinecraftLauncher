// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

/// <summary>Provides the content page's anchored download list without owning download execution.</summary>
internal sealed partial class ContentDownloadPanel : Control
{
    private readonly ContentDownloads _downloads;
    private readonly Flyout _flyout;
    private readonly Window _window;
    private readonly Action<ContentDownload> _open;
    private bool _confirming;
    private partial int Revision { get; set; }

    internal ContentDownloadPanel(ContentDownloads downloads, Window window, Action<ContentDownload> open)
    {
        _downloads = downloads;
        _window = window;
        _open = open;
        _flyout = new Flyout(_ => new ContentDownloadList(downloads, Open, Cancel))
        {
            Placement = PopupPlacement.TopEdgeAlignedRight,
            ShouldConstrainToRootBounds = true,
        }.LauncherStyle(appearance => appearance.Menus.ErrorPopup)
            .Automation(new() { Name = Resources.ContentDownloads, AutomationId = "content.downloads.flyout" });
    }

    private string Label { get { _ = Revision; return _downloads.ActiveCount > 0 ? string.Format(Resources.ContentDownloadsActive, _downloads.ActiveCount) : Resources.ContentDownloadsFailed; } }
    private bool HasItems { get { _ = Revision; return _downloads.Items.Count > 0; } }
    private Ui Body => DropDownButton(new ContentDownloadLabel(Label), _flyout)
        .Style(default(DropDownButtonStyle).Button(LauncherButtons.OutlineButton).ChevronVisible(false))
        .MinHeight(48f)
        .Visible(HasItems)
        .Automation(new() { Name = Label, AutomationId = "content.downloads.toggle" });

    internal void Hide() => _flyout.Hide();
    private void Open(ContentDownload entry)
    {
        _flyout.Hide();
        _window.RequestAnimationFrame(_ => _open(entry));
    }
    private async void Cancel(ContentDownload entry)
    {
        if (_confirming) return;
        _confirming = true;
        _flyout.Hide();
        try
        {
            if (entry.Task.Active)
            {
                if (await ContentDownloadConfirmation.CancelAsync(_window, entry.Project.Name))
                    await entry.Task.CancelAsync();
            }
            else
            {
                _downloads.Dismiss(entry);
            }
        }
        finally { _confirming = false; }
    }
    private void OnChanged()
    {
        Revision++;
        if (_downloads.Items.Count == 0) _flyout.Hide();
    }
    protected override IDisposable? OnMounted() { _downloads.Changed += OnChanged; return null; }
    protected override void OnDispose() { _downloads.Changed -= OnChanged; _flyout.Hide(); base.OnDispose(); }
}

internal sealed partial class ContentDownloadList(ContentDownloads downloads, Action<ContentDownload> open, Action<ContentDownload> cancel) : Control
{
    private partial int Revision { get; set; }
    private IReadOnlyList<ContentDownload> Items { get { _ = Revision; return downloads.Items; } }
    private Ui Body => Column([
        Text(Resources.ContentDownloads)
            .FontSize(16f).FontWeight(600).Foreground(LauncherTheme.Ink),
        ScrollView(ItemsRepeater(
                Items,
                entry => entry.Id,
                entry => new ContentDownloadRow(entry, open, cancel),
                static (_, _) => { },
                new StackLayout { EstimatedItemExtent = 80f, Spacing = 10f }).KeyboardNavigation())
            .MaxHeight(360f),
    ])
        .Spacing(16f)
        .Padding(16f)
        .Width(380f);
    private void OnChanged() => Revision++;
    protected override IDisposable? OnMounted() { downloads.Changed += OnChanged; return null; }
    protected override void OnDispose() { downloads.Changed -= OnChanged; base.OnDispose(); }
}

internal sealed partial class ContentDownloadRow(ContentDownload entry, Action<ContentDownload> open, Action<ContentDownload> cancel) : Control
{
    private partial int Revision { get; set; }
    private partial bool Hovered { get; set; }
    private partial bool Focused { get; set; }
    private partial bool CancelFocused { get; set; }
    private ContentState State { get { _ = Revision; return entry.State; } }
    private string CancelText => State.Active ? Resources.DialogCancel : Resources.CloseLaunchSettings;
    private string ProgressTooltip { get { _ = Revision; return string.Join(Environment.NewLine, entry.Task.Progress, entry.Speed); } }
    private static ButtonStyle RowStyle => LauncherButtons.FlatIconButton
        .Padding(16f)
        .ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center)
        .Palette(new ButtonPalette(
            new(LauncherTheme.ControlBackground, LauncherTheme.Ink, Colors.Transparent),
            new(LauncherTheme.SelectionBackground.MultiplyAlpha(.7f), LauncherTheme.Ink, Colors.Transparent),
            new(LauncherTheme.SelectionBackground, LauncherTheme.Ink, Colors.Transparent),
            new(LauncherTheme.ControlBackground, LauncherTheme.Quiet, Colors.Transparent)));
    private Ui Body => HoverArea(
        Overlay([
            LauncherControls.OutlineCommand(Grid(
                    [GridLength.Pixels(42f), GridLength.Star()],
                    [GridLength.Auto],
                    [
                        new ContentRemoteIconView(entry.Project.IconUrl, Resources.ContentIconUnavailable, LauncherTheme.Muted, LauncherTheme.Edge)
                            .VAlign(VerticalAlignment.Center)
                            .Cell(0, 0),
                        Column([
                            Text(entry.Project.Name)
                                .FontSize(14f).Foreground(LauncherTheme.Ink)
                                .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                                .Margin(new Thickness(0f, 0f, 28f, 0f)),
                            ProgressBar()
                                .Indeterminate(State.Percent is null)
                                .Value(State.Percent ?? 0)
                                .Thickness(4f)
                                .ToolTip(ProgressTooltip)
                                .Visible(State.Active && !State.CancelRequested),
                            Text(State.CancelRequested ? Resources.LaunchCanceling : entry.Task.Status)
                                .FontSize(11f).Foreground(State.Active ? LauncherTheme.Muted : LauncherTheme.Error)
                                .Wrap()
                                .Visible(!State.Active || State.CancelRequested),
                        ])
                            .Spacing(12f)
                            .VAlign(VerticalAlignment.Center)
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(14f), () => open(entry))
                .Style(RowStyle)
                .HAlign(HorizontalAlignment.Stretch)
                .OnGotFocus(_ => Focused = true).OnLostFocus(() => Focused = false)
                .Automation(new() { Name = entry.Project.Name, AutomationId = "content.download.open." + entry.Project.Id }),
            Opacity(LauncherControls.OutlineCommand(Row([LauncherIcons.Line("close", 16f)]), () => cancel(entry))
                    .Style(LauncherButtons.FlatIconButton)
                    .Width(28f).Height(28f)
                    .Enabled(!State.CancelRequested)
                    .OnGotFocus(_ => CancelFocused = true).OnLostFocus(() => CancelFocused = false)
                    .ToolTip(CancelText)
                    .Automation(new() { Name = CancelText, AutomationId = "content.download.cancel." + entry.Project.Id }))
                .Opacity(Hovered || Focused || CancelFocused ? 1f : 0f)
                .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Top)
                .Margin(new Thickness(0f, 10f, 10f, 0f)),
        ]),
        value => Hovered = value);
    private void OnChanged() => Revision++;
    protected override IDisposable? OnMounted() { entry.Task.Changed += OnChanged; return null; }
    protected override void OnDispose() { entry.Task.Changed -= OnChanged; base.OnDispose(); }
}

[View]
internal readonly partial record struct ContentDownloadLabel(string Label)
{
    /// <summary>Provides the icon and count inside the native download-list dropdown.</summary>
    public Ui Body => Row([LauncherIcons.Line("download", 20f), Text(Label)]).Spacing(10f);
}