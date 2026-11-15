// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Instances;
using Rodel.MinecraftLauncher.App.Layout;
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

[View]
internal readonly partial record struct ContentSortHeader(
    LauncherAppearance Theme,
    ContentPage Page,
    ContentSortColumn Field,
    string Label,
    ButtonStyle Style,
    Color Ink,
    bool Active,
    bool Descending)
{
    internal Action Sort
    {
        get
        {
            var page = Page;
            var column = Field;
            return () => page.ChooseSort(column);
        }
    }

    internal string AccessibleName => string.Format(Active
            ? Descending ? Resources.ContentSortDescending : Resources.ContentSortAscending
            : Resources.ContentSort, Label);

    public Ui Body => Column([
        LauncherControls.Command(
Theme,            Row([
                Text(Label)
                    .FontSize(11f).Foreground(Ink),
                LauncherIcons.Line(Descending ? "chevron-down" : "chevron-up", 12f, Ink)
                    .Visible(Active),
            ])
                .Spacing(6f),
            Sort)
            .Style(Style)
            .Automation(new()
            {
                Name = AccessibleName,
                AutomationId = "content.header." + Field.ToString()
                    .ToLowerInvariant()
            }),
    ]);
}

internal sealed partial class ContentCollection : Control
{
    private readonly ContentPage _page;
    private readonly Func<ContentRowState, ContentListRow> _createRow;
    private readonly Func<ContentRowState, IFlyout?> _contextMenu;
    private readonly ContentEmptyTip _emptyTip;
    private readonly ScrollView _emptyScroll;
    private readonly WorldsPanel _worlds;
    private readonly ScreenshotPanel _screenshots;

    internal ContentCollection(ContentPage page)
    {
        _page = page;
        _createRow = row => new(LauncherAppearance.Capture(), row, page);
        _contextMenu = CreateContextMenu;
        _emptyTip = new(page);
        _worlds = page.Worlds;
        _screenshots = page.Screenshots;
        _emptyScroll = new(_emptyTip);
    }

    private void OnEmptyLayout()
    {
        if (_emptyScroll.Interaction is not { IsCollapsed: false })
            return;
        var height = _emptyScroll.ViewportSize.Height;
        if (Math.Abs(_emptyTip.ViewportHeight - height) > .1f)
            _emptyTip.ViewportHeight = height;
    }

    protected override IDisposable? OnMounted()
    {
        _page.Window.LayoutUpdated += OnEmptyLayout;
        return null;
    }

    protected override void OnDispose()
    {
        _page.Window.LayoutUpdated -= OnEmptyLayout;
        base.OnDispose();
    }

    private MenuFlyout? CreateContextMenu(ContentRowState row)
    {
        if (row.Local is not { } item)
            return null;
        var returnFocus = _page.Window.FocusedElement;
        MenuFlyout? menu = null;
        var entries = new List<MenuFlyoutItemBase>
        {
            new MenuFlyoutItem(Resources.ContentUpdatesCheckItem, () =>
                {
                    menu?.Hide();
                    _page.ShowUpdates([item], returnFocus);
                })
            {
                IsEnabled = _page.CanCheckUpdates,
                Automation = new()
                {
                    Name = Resources.ContentUpdatesCheckItem,
                    AutomationId = "content.update." + item.FileName
                }
            },
        };
        if (item.ProjectId is not null)
        {
            entries.Add(new MenuFlyoutItem(item.Pinned ? Resources.ContentUpdatesUnpin : Resources.ContentUpdatesPin, () =>
                    {
                        menu?.Hide();
                        _page.ConfigurePolicy(
                            row.InstanceId,
                            row.Kind,
                            item,
                            !item.Pinned,
                            null);
                    })
            {
                IsEnabled = row.CanChange,
                Automation = new()
                {
                    Name = item.Pinned ? Resources.ContentUpdatesUnpin : Resources.ContentUpdatesPin,
                    AutomationId = "content.pin." + item.FileName
                }
            });
            MenuFlyoutItem Channel(string value, string label) => new(item.Channel == value ? string.Format(Resources.ContentUpdatesCurrentChannel, label) : label, () =>
                {
                    menu?.Hide();
                    _page.ConfigurePolicy(
                        row.InstanceId,
                        row.Kind,
                        item,
                        null,
                        value);
                })
            {
                IsEnabled = row.CanChange && item.Channel != value,
                Automation = new()
                {
                    Name = item.Channel == value ? string.Format(Resources.ContentUpdatesCurrentChannel, label) : label,
                    AutomationId = "content.channel." + value + "." + item.FileName
                }
            };
            entries.Add(new MenuFlyoutSubItem(Resources.ContentUpdatesChannel, Channel("release", Resources.ContentUpdatesRelease), Channel("beta", Resources.ContentUpdatesBeta), Channel("alpha", Resources.ContentUpdatesAlpha)));
        }

        if (row.Kind == ContentKind.ResourcePacks && item.InUse == true)
        {
            entries.Add(new MenuFlyoutItem(Resources.ContentResourceRaise, () =>
                    {
                        menu?.Hide();
                        _page.MoveResourcePack(row, true);
                    })
            {
                IsEnabled = row.CanChange && item.Priority > 1,
                Automation = new() { AutomationId = "content.priority.up." + item.FileName }
            });
            entries.Add(new MenuFlyoutItem(Resources.ContentResourceLower, () =>
                    {
                        menu?.Hide();
                        _page.MoveResourcePack(row, false);
                    })
            {
                IsEnabled = row.CanChange && item.Priority < _page.State.Items.Count(item => item.InUse == true),
                Automation = new() { AutomationId = "content.priority.down." + item.FileName }
            });
        }

        entries.Add(new MenuFlyoutItem(Resources.ContentRemove, () =>
                {
                    menu?.Hide();
                    _page.ShowRemove(row.InstanceId, row.Kind, item);
                })
        { IsEnabled = row.CanChange });
        menu = new MenuFlyout([.. entries]).Style(LauncherMenus.CommandMenu);
        return menu;
    }

    internal void ShowContextMenu(ContentRowState row)
    {
        if (CreateContextMenu(row) is MenuFlyout menu)
            menu.ShowAt(_page.Window);
    }

    private bool Empty => _page.State.Items.Count == 0 && _page.State.ReadError.Length == 0;

    private Ui Body => DropTarget(
        Overlay([
            new Controls.LauncherMaterialSurface(LauncherMaterials.Acrylic).Margin(1f),
            Border(Overlay([
                Grid(
                    [GridLength.Star()],
                    [GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Star(), GridLength.Auto],
                    [
                        Border(Grid(
                                [GridLength.Auto, GridLength.Auto, GridLength.Star(), GridLength.Auto],
                                [GridLength.Auto],
                                [
                                    new InstanceIconView(_page.Home.Selected?.IconData, 36f, 40f).VAlign(VerticalAlignment.Center)
                                        .Cell(0, 0),
                                    Row([
                                        Text(Resources.CurrentInstance)
                                            .FontSize(11f).Foreground(LauncherTheme.Muted)
                                            .Wrap()
                                            .Width(_page.Compact ? 60f : 70f)
                                            .VAlign(VerticalAlignment.Center),
                                        LauncherControls.Choice([.._page.Instances.Select(item => item.Name)])
                                            .SelectedIndex(Array.FindIndex(_page.Instances, item => item.Id == _page.Home.SelectedId))
                                            .OnSelectionChanged(_page.ChooseInstance)
                                            .Enabled(!_page.State.Active)
                                            .FaceplateStyle(LauncherMenus.ChoiceFace.LabelStyle(LauncherStyles.Text.FontSize(15f).FontWeight(600)))
                                            .Width(_page.Compact ? 200f : 280f).MinHeight(42f)
                                            .ToolTip(_page.Home.Runtime)
                                            .Automation(new()
                                            {
                                                Name = Resources.ContentSelectInstance,
                                                AutomationId = "content.instance",
                                                HelpText = _page.Home.Runtime
                                            }),
                                    ])
                                        .Spacing(12f)
                                        .Cell(1, 0),
                                    Text(_page.Home.Runtime)
                                        .FontSize(11f).Foreground(LauncherTheme.Muted)
                                        .Wrap()
                                        .Visible(!_page.Compact)
                                        .VAlign(VerticalAlignment.Center)
                                        .Cell(2, 0),
                                    DropDownButton(Resources.ContentBundledMods, _page.BundledModsFlyout.LauncherStyle(popupAppearance=>popupAppearance.Menus.ErrorPopup.Background(popupAppearance.Materials.Acrylic)
                                                .Border(popupAppearance.Edge)
                                                .Padding(0f, 16f, 0f, 16f)))
                                        .Style(default(DropDownButtonStyle).Button(LauncherButtons.OutlineButton)
                                            .Padding(12f, 8f)
                                            .CornerRadius(0f))
                                        .MinHeight(40f)
                                        .Enabled(_page.CanChange)
                                        .Visible(_page.InstalledMode && _page.Kind == ContentKind.Mods)
                                        .VAlign(VerticalAlignment.Center)
                                        .Automation(new()
                                        {
                                            Name = Resources.ContentBundledMods,
                                            AutomationId = "content.bundled.open"
                                        })
                                        .Cell(3, 0),
                                ])
                                .ColumnSpacing(12f))
                            .Padding(20f, 12f)
                            .BorderBrush(LauncherTheme.Edge)
                            .BorderThickness(0f, 0f, 0f, 1f)
                            .Cell(0, 0),
                        Column([
                            new ContentKindSegments(_page, LauncherMaterials.SegmentedMaterial, Search: false),
                        ])
                            .Padding(20f, _page.Compact ? 8f : 12f)
                            .Cell(0, 1),
                        Border(Column([
                                InfoBar(message: _page.ChangeBlocked)
                                    .Severity(_page.Home.LaunchReadError.Length > 0 ? InfoBarSeverity.Error : InfoBarSeverity.Warning)
                                    .Closable(false)
                                    .Style(default(InfoBarStyle).CornerRadius(0f).MessageFontSize(13f))
                                    .Open(_page.ChangeBlocked.Length > 0)
                                    .Automation(new()
                                    {
                                        Name = _page.ChangeBlocked,
                                        AutomationId = "content.change-blocked"
                                    }),
                                Text(string.Format(Resources.ContentListFailed, _page.State.ReadError))
                                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                                    .Wrap()
                                    .Visible(_page.InstalledMode && _page.State.ReadError.Length > 0)
                                    .Automation(new()
                                    {
                                        Name = string.Format(Resources.ContentListFailed, _page.State.ReadError),
                                        AutomationId = "content.read-error"
                                    }),
                                Text(_page.SelectionNotice)
                                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                                    .Wrap()
                                    .Visible(_page.InstalledMode && _page.SelectionNotice.Length > 0)
                                    .Automation(new()
                                    {
                                        Name = _page.SelectionNotice,
                                        AutomationId = "content.selection-notice"
                                    }),
                                LauncherControls.Choice(_page.State.ShaderSelection?.AvailableEngines.ToArray() ?? [])
                                    .SelectedIndex(_page.State.ShaderSelection is { } shaders ? Array.IndexOf([..shaders.AvailableEngines], shaders.Engine) : -1)
                                    .OnSelectionChanged(_page.ChooseShaderEngine)
                                    .Enabled(!_page.State.Active)
                                    .ItemHeight(36f)
                                    .MinHeight(36f).Width(200f)
                                    .Visible(_page.InstalledMode && _page.Kind == ContentKind.Shaders && _page.State.ShaderSelection?.AvailableEngines.Count > 1)
                                    .Automation(new()
                                    {
                                        Name = Resources.ContentShaderEngine,
                                        AutomationId = "content.shader-engine"
                                    }),
                            ])
                                .Spacing(6f))
                            .Padding(new Thickness(20f, 0f, 20f, 10f))
                            .Visible(_page.ChangeBlocked.Length > 0 || (_page.InstalledMode && (_page.State.ReadError.Length > 0 || _page.SelectionNotice.Length > 0 || (_page.Kind == ContentKind.Shaders && _page.State.ShaderSelection?.AvailableEngines.Count > 1))))
                            .Cell(0, 2),
                        Border(Grid(
                                ContentListLayout.Columns(_page.Compact, _page.Window.TextScaleFactor),
                                [GridLength.Auto],
                                [
                                    new ContentSortHeader(
LauncherAppearance.Capture(),                                        _page,
                                        ContentSortColumn.Name,
                                        Resources.InstanceNameColumn,
                                        LauncherButtons.InstanceTitle,
                                        LauncherTheme.Muted,
                                        _page.SortColumn == ContentSortColumn.Name,
                                        _page.SortDescending).Cell(1, 0),
                                    new ContentSortHeader(
LauncherAppearance.Capture(),                                        _page,
                                        ContentSortColumn.Version,
                                        Resources.ContentVersion,
                                        LauncherButtons.InstanceTitle,
                                        LauncherTheme.Muted,
                                        _page.SortColumn == ContentSortColumn.Version,
                                        _page.SortDescending).Visible(!_page.Compact)
                                        .Cell(2, 0),
                                    new ContentSortHeader(
LauncherAppearance.Capture(),                                        _page,
                                        ContentSortColumn.Status,
                                        Resources.InstanceStatusColumn,
                                        LauncherButtons.InstanceTitle,
                                        LauncherTheme.Muted,
                                        _page.SortColumn == ContentSortColumn.Status,
                                        _page.SortDescending).Cell(_page.Compact ? 2 : 3, 0),
                                ])
                                .ColumnSpacing(ContentListLayout.ColumnSpacing)
                                .Padding(ContentListLayout.HorizontalPadding, 8f))
                            .BorderBrush(LauncherTheme.Edge)
                            .BorderThickness(0f, 0f, 0f, 1f)
                            .Visible(_page.InstalledMode && _page.State.Items.Count > 0)
                            .Cell(0, 3),
                        Overlay([
                            ItemsView(
                                _page.Rows,
                                static row => row.Key,
                                _createRow,
                                new StackLayout { EstimatedItemExtent = 82f },
                                accessibleName: static row => row.Local?.Name ?? row.Remote?.Name ?? row.Heading ?? row.Message ?? Resources.ContentMore,
                                itemAutomation: static row => new() { AutomationId = "content.row." + (row.Local?.FileName ?? row.Key) },
                                itemContextFlyout: _contextMenu)
                                .SelectionMode(SelectionMode.None)
                                .ItemChrome(LauncherAppearance.Capture().ButtonPalette.ListItems)
                                .ItemContentInteraction(ItemsViewItemContentInteraction.Interactive)
                                .Automation(new()
                                {
                                    Name = _page.Kind.Title(),
                                    AutomationId = "content.list"
                                })
                                .Visible(_page.InstalledMode),
                            _emptyScroll.Automation(new() { AutomationId = "content.empty.viewport" })
                                .Visible(_page.InstalledMode && Empty),
                            _worlds.Visible(_page.WorldMode),
                            _screenshots.Visible(_page.ScreenshotMode),
                        ])
                            .Cell(0, 4),
                        Border(Text(string.Format(Resources.ContentCountByKind, _page.State.Items.Count, _page.Kind.Title()))
                                .FontSize(12f).Foreground(LauncherTheme.Muted)
                                .Automation(new()
                                {
                                    Name = string.Format(Resources.ContentCountByKind, _page.State.Items.Count, _page.Kind.Title()),
                                    AutomationId = "content.count",
                                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                                }))
                            .Padding(20f, 12f)
                            .BorderBrush(LauncherTheme.Edge)
                            .BorderThickness(0f, 1f, 0f, 0f)
                            .Visible(_page.InstalledMode && (_page.State.Items.Count > 0 || !_page.State.Loading) && _page.State.ReadError.Length == 0)
                            .Cell(0, 5),
                    ]),
                new ContentDropOverlay(
                    string.Format(Resources.ContentDropTitle, _page.Home.Selected?.Name ?? string.Empty),
                    string.Format(Resources.ContentDropScope, _page.WorldMode ? Resources.ContentWorlds : _page.Kind.Title(), _page.WorldMode ? "saves" : _page.Kind.Directory()),
                    LauncherTheme.ControlBackground,
                    LauncherTheme.Focus,
                    LauncherTheme.Ink,
                    LauncherTheme.Muted).Visible(_page.Dropping),
            ]))
            .BorderBrush(LauncherTheme.Edge)
            .BorderThickness(1f),
        ]),
        _page.DropTarget);
}