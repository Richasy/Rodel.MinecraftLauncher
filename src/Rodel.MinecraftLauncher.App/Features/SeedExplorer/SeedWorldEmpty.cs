// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

internal sealed partial class SeedWorldEmpty(SeedExplorerState state) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private string Title => Current.Instances.Length == 0 ? Resources.SeedNoInstancesTitle
        : Current.Worlds.Length == 0 ? Resources.SeedNoWorldsTitle : Resources.SeedReadWorld;
    private string Description => Current.Instances.Length == 0 ? Resources.SeedNoInstancesDescription
        : Current.Worlds.Length == 0 ? Resources.SeedNoWorldsDescription : Current.WorldSnapshot?.Name ?? string.Empty;
    private Ui Body => Column([
        new EmptyPlaceholderView("world-origin", Title, Description, LauncherTheme.Ink, LauncherTheme.Muted, "seed.world.empty"),
        new SeedCommand(LauncherAppearance.Capture(),Resources.SeedReadWorld, () => _ = state.Controller.Worlds.OpenAsync(), "seed.world.empty.open", !Current.WorldReading, true)
            .Visible(Current.WorldSnapshot is not null),
    ]).Spacing(16f).MaxWidth(390f).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center).Margin(24f);
}