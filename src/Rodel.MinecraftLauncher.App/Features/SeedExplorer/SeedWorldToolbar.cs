// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Selects input provenance without duplicating the map or its owned state.</summary>
internal sealed partial class SeedSourceChoice(SeedExplorerState state, string id = "seed.source") : Control
{
    private Ui Body => new SeedChoice(LauncherAppearance.Capture(), Resources.SeedMapSource, [Resources.SeedInputSource, Resources.SeedWorldSource], state.Current.WorldSource ? 1 : 0,
        value => _ = state.Controller.Worlds.SelectSourceAsync(value == 1), id);
}

/// <summary>Keeps world selection and map content in one aligned input row; map tools and provenance have their own locations.</summary>
internal sealed partial class SeedWorldToolbar(SeedExplorerState state) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private string[] Dimensions => Current.WorldSnapshot?.Dimensions ?? ["overworld"];
    private string[] DimensionNames => [.. Dimensions.Select(SeedText.Dimension)];
    private bool Selected => Current.WorldSnapshot is not null && !Current.WorldReading;
    private bool Narrow => Current.Width < 1000f;
    private bool ViewInHeader => Current.Width >= 1100f;
    private string ReadLabel => Current.WorldLoaded ? Resources.SeedRefreshWorld : Resources.SeedReadWorld;
    private Ui Body => Border(Grid(ViewInHeader
        ? [GridLength.Pixels(112f), GridLength.Pixels(Narrow ? 128f : 180f), GridLength.Star(),
            GridLength.Pixels(Narrow ? 92f : 104f), GridLength.Pixels(176f), GridLength.Auto, GridLength.Auto]
        : [GridLength.Pixels(112f), GridLength.Pixels(Narrow ? 128f : 180f), GridLength.Star(),
            GridLength.Pixels(Narrow ? 92f : 104f), GridLength.Pixels(176f), GridLength.Auto], [GridLength.Auto], [
        new SeedSourceChoice(state, "seed.world.source").VAlign(VerticalAlignment.Center).Cell(0, 0),
        new SeedChoice(LauncherAppearance.Capture(),Resources.SeedInstance, [.. Current.Instances.Select(instance => instance.Name ?? instance.Id)],
            Array.FindIndex(Current.Instances, instance => instance.Id == Current.WorldInstanceId),
            index => { if (index >= 0 && index < Current.Instances.Length) _ = state.Controller.Worlds.SelectInstanceAsync(Current.Instances[index].Id); },
            "seed.world.instance", Current.Instances.Length > 0).VAlign(VerticalAlignment.Center).Cell(1, 0),
        new SeedChoice(LauncherAppearance.Capture(),Resources.SeedWorld, [.. Current.Worlds.Select(world => world.Name)],
            Array.FindIndex(Current.Worlds, world => world.Folder == Current.WorldFolder),
            index => { if (index >= 0 && index < Current.Worlds.Length) _ = state.Controller.Worlds.SelectWorldAsync(Current.Worlds[index].Folder); },
            "seed.world.save", Current.Worlds.Length > 0).VAlign(VerticalAlignment.Center).Cell(2, 0),
        new SeedChoice(LauncherAppearance.Capture(),Resources.SeedDimension, DimensionNames, Array.IndexOf(Dimensions, Current.World.Dimension),
            index => { if (index >= 0 && index < Dimensions.Length) _ = state.Controller.Map.ChangeWorldAsync(Current.World with { Dimension = Dimensions[index] }, Current.SliceY); },
            "seed.world.dimension", Selected).VAlign(VerticalAlignment.Center).Cell(3, 0),
        new SeedSegments(LauncherAppearance.Capture(),[Resources.SeedSavedMap, Resources.SeedGeneratedPreview], Current.SavedMap ? 0 : 1,
            value => _ = SelectContentAsync(value == 0), "seed.world.content", 176f,
            [Selected, Selected && Current.WorldSnapshot?.Seed is not null], Height: SeedChrome.FieldHeight)
            .VAlign(VerticalAlignment.Center).Cell(4, 0),
        new SeedWorldViewControls(state, false).VAlign(VerticalAlignment.Center).Visible(ViewInHeader).Cell(5, 0),
        LauncherControls.Command(Row([LauncherIcons.Line(Current.WorldLoaded ? "refresh" : "seed-generate", 20f)]), () => _ = state.Controller.Worlds.OpenAsync())
            .Style(Current.WorldLoaded ? LauncherButtons.DialogSecondary : LauncherButtons.DialogPrimary)
            .Width(SeedChrome.FieldHeight).Height(SeedChrome.FieldHeight).VAlign(VerticalAlignment.Center).ToolTip(ReadLabel).Enabled(Selected)
            .Automation(new() { Name = ReadLabel, AutomationId = "seed.world.open" }).Cell(ViewInHeader ? 6 : 5, 0),
    ]).ColumnSpacing(8f)).Padding(12f, 10f).BorderBrush(LauncherTheme.Edge).BorderThickness(0, 0, 0, 1f);

    private async Task SelectContentAsync(bool saved)
    {
        if (Current.SavedMap == saved || (!saved && Current.WorldSnapshot?.Seed is null)) return;
        state.Current = Current with { SavedMap = saved };
        await state.Controller.Worlds.OpenAsync();
    }
}

/// <summary>Reuses one view/slice editor geometry in the input row or the narrow map's overlay.</summary>
internal sealed partial class SeedWorldViewControls(SeedExplorerState state, bool compact) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private bool Selected => Current.WorldSnapshot is not null && !Current.WorldReading;
    private float FieldHeight => compact ? 32f : SeedChrome.FieldHeight;
    private string ViewId => compact ? "seed.world.view.overlay" : "seed.world.view";
    private string HeightId => compact ? "seed.world.y.overlay" : "seed.world.y";
    private Ui Body => Grid([GridLength.Pixels(compact ? 104f : 124f), GridLength.Auto], [GridLength.Auto], [
        new SeedChoice(LauncherAppearance.Capture(),Resources.SeedView, [Resources.SeedSurface, Resources.SeedSlice], Current.SliceY is null ? 0 : 1,
            value => _ = state.Controller.Map.ChangeWorldAsync(Current.World, value == 0 ? null : Current.World.Dimension == "overworld" ? -32 : 64),
            ViewId, Selected, Height: FieldHeight).VAlign(VerticalAlignment.Center).Cell(0, 0),
        Border(new SeedNumber("Y", Current.SliceY ?? 64, -32768, 32767,
            value => _ = state.Controller.Map.ChangeWorldAsync(Current.World, value), HeightId, Selected, Height: FieldHeight))
            .Width(78f).VAlign(VerticalAlignment.Center).Visible(Current.SliceY is not null).Cell(1, 0),
    ]).ColumnSpacing(Current.SliceY is null ? 0f : 8f);
}

/// <summary>Places the narrow window's view controls beside the zoom tools without consuming another toolbar row.</summary>
internal sealed partial class SeedWorldMapView(SeedExplorerState state) : Control
{
    private Ui Body => Column([new SeedWorldViewControls(state, true)])
        .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Top).Margin(new Thickness(0f, 10f, 58f, 0f));
}