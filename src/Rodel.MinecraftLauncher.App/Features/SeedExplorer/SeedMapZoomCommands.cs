// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Keeps the map's native backdrop and zoom foreground in one bounded compositor subtree.</summary>
internal sealed partial class SeedMapZoomCommands(SeedExplorerState state) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private Ui Body => Border(Overlay([
        new LauncherMaterialSurface(LauncherMaterials.Acrylic),
        Column([
            new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedZoomIn, "zoom-in", () => state.Controller.Map.Zoom(.5), "seed.zoom.in", !Current.Opening && Current.HasMap, Surface: SeedChrome.MapTool),
            new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedZoomOut, "zoom-out", () => state.Controller.Map.Zoom(2), "seed.zoom.out", !Current.Opening && Current.HasMap, Surface: SeedChrome.MapTool),
            new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedPlayerLocation, "player-follow", state.Controller.Worlds.LocatePlayer, "seed.player.center", Current.Player?.Player is not null, Surface: SeedChrome.MapTool)
                .Visible(Current.WorldSource && !Current.SearchMode),
            new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedSpawn, "world-spawn", state.Controller.Map.ReturnToSpawn, "seed.spawn", !Current.Opening, Surface: SeedChrome.MapTool)
                .Visible(Current.Spawn is not null && Current.World.Dimension == "overworld"),
            new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedOrigin, "world-origin", state.Controller.Map.ReturnToOrigin, "seed.origin", Current.HasMap, Surface: SeedChrome.MapTool),
        ]).Spacing(4f).Padding(2f),
    ])).BorderBrush(LauncherTheme.Edge).BorderThickness(1f)
        .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Top).Margin(10f);
}