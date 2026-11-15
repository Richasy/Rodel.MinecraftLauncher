// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Screenshots;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using System.Globalization;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

/// <summary>Keeps the gallery and decoded tiles mounted while native item containers repaint selection.</summary>
internal sealed partial class ScreenshotGallery : Control
{
    private readonly SelectableItemFactory<ScreenshotTile, ScreenshotTile> _factory;
    private readonly ItemsView _list;
    private readonly ScreenshotSelectionController _selection;
    private readonly Dictionary<ScreenshotFile, int> _keys = [];
    private IReadOnlyList<ScreenshotFile> _files = [];
    private LauncherAppearance? _appearance;
    private int _nextKey;
    private partial bool Busy { get; set; }

    internal ScreenshotGallery(ScreenshotStore store, Action<ScreenshotFile> open, Func<ScreenshotFile, IFlyout?> menu)
    {
        _factory = new(
            [],
            tile => _keys[tile.File],
            static tile => tile,
            nameSelector: static tile => tile.File.FileName,
            automationSelector: static tile => new() { AutomationId = "screenshot.item." + tile.File.FileName },
            contextFlyoutSelector: tile => menu(tile.File));
        _list = new(_factory, new MasonryVirtualizingLayout
        {
            TargetColumnWidth = 220f,
            EstimatedItemExtent = 184f,
            ColumnSpacing = 12f,
            RowSpacing = 12f,
        });
        _list.SelectionVisualMode(ItemsViewSelectionVisualMode.BackgroundOnly)
            .ItemContentInteraction(ItemsViewItemContentInteraction.Interactive)
            .ItemsMargin(new Thickness(20f, 12f, 20f, 24f));
        _selection = new(store, _list, open);
    }

    internal ScreenshotGallery WithItems(LauncherAppearance appearance, IReadOnlyList<ScreenshotFile> files, bool busy)
    {
        Busy = busy;
        if (_appearance != appearance || !_files.SequenceEqual(files))
        {
            _appearance = appearance;
            _files = files;
            _selection.SetFiles(files);
            foreach (var file in files)
            {
                if (!_keys.ContainsKey(file)) _keys[file] = ++_nextKey;
            }

            _factory.UpdateItems([.. files.Select(file => new ScreenshotTile(appearance, file, _selection))]);
            foreach (var file in _keys.Keys.Where(file => !files.Contains(file)).ToArray()) _keys.Remove(file);

            var normal = new ButtonVisualState(Colors.Transparent, appearance.Ink, Colors.Transparent);
            var selected = normal with { Border = appearance.PrimaryBackground };
            _list.ItemChrome(new(
                new(normal, normal, normal, normal),
                new(selected, selected, selected, selected),
                Colors.Transparent,
                new CornerRadius(0f),
                3f));
        }

        _selection.Synchronize();
        return this;
    }

    internal IDisposable BindKeyboard(Window window) => _selection.BindKeyboard(window.Commands, () => window.FocusedElement);

    private Ui Body => _list
        .Enabled(!Busy)
        .Automation(new() { Name = Resources.ContentScreenshots, AutomationId = "screenshot.list" });
}

[View]
internal readonly partial record struct ScreenshotTile(
    LauncherAppearance Theme,
    ScreenshotFile File,
    ScreenshotSelectionController Selection)
{
    internal PointerEventResult HandlePointer(in PointerEvent packet) => Selection.HandlePointer(File, packet);

    /// <summary>Shows a fixed-aspect, center-cropped preview without changing the original image or its file operations.</summary>
    public Ui Body => PointerArea(Border(Column([
            AsyncImage(ImageSource.FromFile(File.Path, decodePixelWidth: 512, decodePixelHeight: 512))
                .Stretch(ImageStretch.UniformToFill)
                .SizeForWidth(static width => new Size(width, width * 9f / 16f))
                .Placeholder(new ScreenshotImagePlaceholder(Theme, false))
                .Failed(new ScreenshotImagePlaceholder(Theme, true))
                .AutomationStatus(static status => status == ImageStatus.Failed ? Resources.ScreenshotPreviewUnavailable : string.Empty)
                .Automation(new() { Name = File.FileName, AutomationId = "screenshot.image." + File.FileName }),
            Column([
                Flow(FlowDirection.LeftToRight,
                    Text(File.FileName)
                        .FontSize(13f).Foreground(Theme.Ink)
                        .NoWrap().MaxLines(1)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()),
                Flow(FlowDirection.LeftToRight,
                    Text(File.ModifiedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                        .FontSize(11f).Foreground(Theme.Muted)
                        .NoWrap().MaxLines(1)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()),
            ])
                .Spacing(4f)
                .Padding(10f, 8f),
        ]))
        .Background(Theme.ControlBackground)
        .BorderBrush(Theme.Edge)
        .BorderThickness(1f), HandlePointer);
}

[View]
internal readonly partial record struct ScreenshotImagePlaceholder(LauncherAppearance Theme, bool Failed)
{
    /// <summary>Shows an explicit failed-preview state while leaving the image's file operations available.</summary>
    public Ui Body => Grid([GridLength.Star()], [GridLength.Star()], [
        Column([
            LauncherIcons.Line("image", 28f, Theme.EmptyPlaceholder).HAlign(HorizontalAlignment.Center),
            Text(Failed ? Resources.ScreenshotPreviewUnavailable : Resources.ContentLoading)
                .FontSize(11f).Foreground(Theme.Muted)
                .Align(TextAlignment.Center).Wrap(),
        ])
            .Spacing(8f)
            .Padding(12f)
            .VAlign(VerticalAlignment.Center),
    ]);
}