// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed partial class ContentSearchPopup : Control
{
    private readonly ContentPage _page;
    private readonly Action _close;
    private readonly ContentSearchField _field;
    private readonly PopupFocusRecovery _focus;
    private readonly ContentSearchResults _results;

    internal ContentSearchPopup(ContentPage page, Action close)
    {
        _page = page;
        _close = close;
        _field = new(page);
        _focus = new(page.Window);
        _results = new(page);
        Viewport = page.Window.ClientSize;
    }

    private partial Size Viewport { get; set; }
    private static string Title => Resources.ContentUnifiedSearch;
    private bool HasSearchContext => _page.QueryActive && !_page.Online.Loading && (_page.Online.Error.Length == 0 || _page.Online.Error == _page.Online.LocalError);
    private string VersionFilter => _page.Online.MinecraftVersion is { Length: > 0 } version ? string.Format(Resources.ContentSearchTargetVersion, version) : Resources.ContentSearchAllVersions;

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Auto, GridLength.Star()],
        [
            Border(Grid(
                    [GridLength.Star(), GridLength.Auto, GridLength.Auto],
                    [GridLength.Auto],
                    [
                        Text(Title)
                            .FontSize(21f).FontWeight(600).Foreground(LauncherTheme.Ink)
                            .VAlign(VerticalAlignment.Center)
                            .Cell(0, 0),
                        Border(Text(_page.Home.Selected?.Name ?? string.Empty)
                                .FontSize(11f).Foreground(LauncherTheme.Muted)
                                .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed())
                            .Background(LauncherTheme.ControlBackground)
                            .BorderBrush(LauncherTheme.Edge)
                            .BorderThickness(1f)
                            .Padding(8f, 4f)
                            .MaxWidth(Math.Min(180f, Viewport.Width * .28f))
                            .VAlign(VerticalAlignment.Center)
                            .Visible(_page.Home.Selected is not null)
                            .Cell(1, 0),
                        LauncherControls.OutlineCommand(
                            Row([
                                LauncherIcons.Line("close", 18f)
                            ]),
                            _close)
                            .Style(LauncherButtons.PopupCloseButton)
                            .Width(32f).Height(32f)
                            .ToolTip(Resources.ContentSearchClose)
                            .Automation(new()
                            {
                                Name = Resources.ContentSearchClose,
                                AutomationId = "content.search.close"
                            })
                            .Cell(2, 0),
                    ])
                    .ColumnSpacing(16f))
                .Padding(24f, 20f)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 0f, 0f, 1f)
                .Cell(0, 0),
            Border(Column([
                    Row([
                        Text(Resources.ContentSearchCommunity)
                            .FontSize(12f).Foreground(LauncherTheme.Muted),
                        Text(Resources.ContentSearchProvider)
                            .FontSize(13f).FontWeight(600).Foreground(LauncherTheme.Ink)
                            .Automation(new()
                            {
                                Name = Resources.ContentSearchProvider,
                                AutomationId = "content.search.community"
                            }),
                        Text(VersionFilter)
                            .FontSize(12f).Foreground(LauncherTheme.Muted)
                            .Visible(HasSearchContext)
                            .Automation(new()
                            {
                                Name = VersionFilter,
                                AutomationId = "content.search.version-filter"
                            }),
                    ])
                        .Spacing(12f),
                    _field,
                    new ContentKindSegments(_page, LauncherMaterials.SegmentedMaterial, Search: true),
                ])
                    .Spacing(10f))
                .Padding(24f, 16f)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 0f, 0f, 1f)
                .Cell(0, 1),
            _results.Cell(0, 2),
        ])
        .Width(Math.Min(760f, Viewport.Width - 48f)).Height(Math.Max(0f, Viewport.Height - 64f));

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs e) => Viewport = e.Size;

    internal void FocusSearch()
    {
        if (_field.Interaction is { } root && FocusTraversal.Next(root, current: null, forward: true) is { } target)
            _ = _page.Window.SetFocus(target, FocusState.Programmatic);
        _focus.Request(_field);
    }

    protected override IDisposable? OnMounted()
    {
        _page.Window.SizeChanged += OnSizeChanged;
        return PopupKeyboardClose.Bind(_page.Window, _close, () => true);
    }

    protected override void OnDispose()
    {
        _page.Window.SizeChanged -= OnSizeChanged;
        _focus.Dispose();
        base.OnDispose();
    }
}

internal sealed partial class ContentSearchField(ContentPage page) : Control
{
    private Ui Body => Column([
        TextBox()
            .Text(page.Query)
            .OnTextChanged(value => page.ChangeQuery(value))
            .Style(LauncherStyles.Input)
            .MinHeight(40f)
            .Placeholder(Resources.ContentUnifiedSearch)
            .Automation(new()
            {
                Name = Resources.ContentSearch,
                AutomationId = "content.search"
            })
    ]);
}