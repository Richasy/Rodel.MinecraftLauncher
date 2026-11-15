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

/// <summary>One selectable wallpaper tile retained by its image identity.</summary>
/// <param name = "Item">Image, display name and gallery category.</param>
/// <param name = "Candidate">Identity currently selected for this theme.</param>
/// <param name = "Width">Width shared by thumbnails and their caption rows.</param>
/// <param name = "Busy">Whether tile interactions are temporarily disabled.</param>
/// <param name = "Select">Commits the selected image identity.</param>
/// <param name = "Appearance">Current palette and localized caption strings.</param>
/// <param name = "Menu">Creates imported-image management commands on demand.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct WallpaperTileView(
    LauncherAppearance Theme,
    WallpaperItem Item,
    string Candidate,
    float Width,
    bool Busy,
    Action<string> Select,
    WallpaperAppearance Appearance,
    Func<WallpaperItem, IFlyout> Menu)
{
    internal Action Choose
    {
        get
        {
            var select = Select;
            var id = Item.Id;
            return () => select(id);
        }
    }

    internal Func<IFlyout> ContextMenu
    {
        get
        {
            var menu = Menu;
            var item = Item;
            return () => menu(item);
        }
    }

    internal string Dimension => Item.Custom
        ? Resources.WallpaperCustom
        : !Item.Landscape ? Resources.WallpaperPlayers : Item.Id switch
        {
            "dark-lava-canyon" or "dark-warped-valley" => Resources.WallpaperNether,
            "dark-end-islands" => Resources.WallpaperEndDimension,
            _ => Resources.WallpaperOverworld,
        };

    /// <summary>Renders the selectable thumbnail, caption and current selection state.</summary>
    public Ui Body => Overlay([
        LauncherControls.Command(
Theme,            Column([
                Image(Item.Thumbnail)
                    .Stretch(ImageStretch.UniformToFill)
                    .Alignment(Item.Alignment)
                    .Height(Width * .625f),
                Row([
                    Text(Item.Name)
                        .FontSize(13f)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                        .Foreground(Candidate == Item.Id ? Appearance.SelectedInk : Appearance.Ink)
                        .Weight(1f),
                    Text(Candidate == Item.Id ? Resources.WallpaperSelected : Dimension)
                        .FontSize(11f).Foreground(Candidate == Item.Id ? Appearance.SelectedInk : Appearance.Muted),
                ])
                    .Spacing(8f)
                    .Margin(10f),
            ]),
            Choose)
            .Style(Candidate == Item.Id ? Appearance.SelectedRow : Appearance.NormalRow)
            .AsSelected(Candidate == Item.Id)
            .Enabled(!Busy)
            .ContextFlyout(ContextMenu)
            .Automation(new()
            {
                Name = Item.Name,
                AutomationId = "wallpaper.tile." + Item.Id
            }),
    ]);
}