// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using Sprout.WindowsAppSDK;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Wallpapers;

internal sealed partial class WallpaperGrid : Control
{
    internal WallpaperGrid(
        IReadOnlyList<WallpaperItem> items,
        string candidate,
        float tileWidth,
        bool busy,
        Action<string> select,
        int columns,
        WallpaperAppearance appearance,
        Func<WallpaperItem, IFlyout> menu)
    {
        Items = items;
        Candidate = candidate;
        TileWidth = tileWidth;
        Busy = busy;
        Select = select;
        Columns = columns;
        Appearance = appearance;
        Menu = menu;
    }

    /// <summary>Palette and text shared by every retained tile.</summary>
    public partial WallpaperAppearance Appearance { get; set; }
    /// <summary>Built-in images followed by the optional imported image.</summary>
    public partial IReadOnlyList<WallpaperItem> Items { get; set; }
    /// <summary>Image identity selected in this theme's slot.</summary>
    public partial string Candidate { get; set; }
    /// <summary>Thumbnail and caption width at the current viewport size.</summary>
    public partial float TileWidth { get; set; }
    /// <summary>Disables all tile interactions while importing an image.</summary>
    public partial bool Busy { get; set; }
    /// <summary>Desired column count used to size the wrapping content area.</summary>
    public partial int Columns { get; set; }
    private readonly Action<string> Select;
    private readonly Func<WallpaperItem, IFlyout> Menu;
    private WallpaperTileView[] Tiles => [..Items.Select(item => new WallpaperTileView(
LauncherAppearance.Capture(),                item,
                Candidate,
                TileWidth,
                Busy,
                Select,
                Appearance,
                Menu))];

    private Ui Body => Border(ItemsRepeater(Tiles, static tile => tile.Item.Id, static tile => tile, new UniformGridLayout
    {
        ItemWidth = TileWidth,
        MinItemHeight = (TileWidth * .625f) + 36f,
        ColumnSpacing = 14f,
        RowSpacing = 14f,
    }).KeyboardNavigation())
        .Width((Columns * TileWidth) + ((Columns - 1) * 14f) + 40f)
        .Padding(new Thickness(20f, 0f, 20f, 0f));
}