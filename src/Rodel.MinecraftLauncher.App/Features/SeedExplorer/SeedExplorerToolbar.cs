// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

internal sealed partial class SeedExplorerToolbar(SeedExplorerState state, SeedExplorerPopups popups) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private string[] Dimensions => Current.Catalog is { Dimensions.Length: > 0 } catalog ? [.. catalog.Dimensions.Select(item => item.Id)] : ["overworld", "the_nether", "the_end"];
    private static string[] ViewNames => [Resources.SeedSurface, Resources.SeedSlice];
    private string[] VersionNames => [.. Current.Versions.Select(item => item.MinecraftVersion + (item.Installed ? string.Empty : " · " + Resources.SeedVersionNotInstalled))];
    private string[] DimensionNames => [.. Dimensions.Select(SeedText.Dimension)];
    private bool Ready => !Current.Opening && Current.Profile is not null;
    private void Generate(string value)
    {
        if (!Ready || string.IsNullOrWhiteSpace(value)) return;
        state.Current = Current with { SeedInput = value };
        _ = state.Controller.Map.GenerateAsync();
    }
    private void Dimension(int index)
    {
        if (index < 0 || index >= Dimensions.Length || Dimensions[index] == Current.World.Dimension) return;
        _ = state.Controller.Map.ChangeWorldAsync(Current.World with { Dimension = Dimensions[index], Preset = "normal", Biome = null }, Dimensions[index] == "the_nether" ? 64 : null);
    }
    private Ui Body => Border(Grid([
        GridLength.Pixels(Current.SearchMode ? 0f : 112f),
        GridLength.Pixels(Current.Width < 1000 ? 100f : 120f), GridLength.Star(),
        GridLength.Pixels(Current.Width < 1000 ? 112f : 128f), GridLength.Pixels(148f), GridLength.Auto, GridLength.Auto,
    ], [GridLength.Auto], [
        Border(new SeedSourceChoice(state)).Margin(new Thickness(0f, 0f, 8f, 0f)).Visible(!Current.SearchMode).Cell(0, 0),
        Border(new SeedChoice(LauncherAppearance.Capture(),Resources.SeedVersion, VersionNames, Current.VersionIndex,
            index => _ = state.Controller.Map.SelectVersionAsync(index), "seed.version", !Current.Opening, [.. Current.Versions.Select(version => version.Installed)])).Margin(new Thickness(0f, 0f, 8f, 0f)).Cell(1, 0),
        TextBox(Current.SeedInput, value => state.Current = Current with { SeedInput = value }).Style(LauncherStyles.Input).FlowDirection(FlowDirection.LeftToRight)
            .Placeholder(Resources.SeedInput).SubmitOnEnter().OnSubmitted(Generate).MinHeight(SeedChrome.FieldHeight).MinContentWidth(0f).Margin(new Thickness(0f, 0f, 12f, 0f))
            .Automation(new() { Name = Resources.SeedInput, AutomationId = "seed.input" }).Cell(2, 0),
        Border(new SeedChoice(LauncherAppearance.Capture(),Resources.SeedDimension, DimensionNames, Array.IndexOf(Dimensions, Current.World.Dimension), Dimension, "seed.dimension", Ready))
            .Margin(new Thickness(0f, 0f, 8f, 0f)).Cell(3, 0),
        Border(new SeedChoice(LauncherAppearance.Capture(),Resources.SeedView, ViewNames, Current.SliceY is null ? 0 : 1,
            index => { if (index != (Current.SliceY is null ? 0 : 1)) _ = state.Controller.Map.ChangeWorldAsync(Current.World, index == 0 ? null : SeedText.DefaultSliceY(Current)); },
            "seed.slice.mode", Ready)).Margin(new Thickness(0f, 0f, 12f, 0f)).Cell(4, 0),
        Border(new SeedNumber("Y", Current.SliceY ?? 64, SeedText.MinimumY(Current),
            SeedText.MaximumY(Current), value => { if (value != Current.SliceY) _ = state.Controller.Map.ChangeWorldAsync(Current.World, value); },
            "seed.slice.y", Ready)).Width(82f).Margin(new Thickness(0f, 0f, 12f, 0f)).Visible(Current.SliceY is not null).Cell(5, 0),
        Row([
            DropDownButton(new SeedDropDownIconContent("world-options"), popups.WorldOptions).Style(SeedChrome.IconDropDown)
                .Width(SeedChrome.FieldHeight).Height(SeedChrome.FieldHeight).ToolTip(Resources.SeedPreset).Enabled(Ready)
                .Automation(new() { Name = Resources.SeedPreset, AutomationId = "seed.world-options.open" }),
            new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedRandom, "refresh", () => _ = state.Controller.Map.RandomAsync(), "seed.random", Ready).VAlign(VerticalAlignment.Center).Visible(Current.Width >= 1000),
            new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedCopySeed, "copy", () => state.Controller.Copy(Current.Seed), "seed.copy", Ready && Current.Seed.Length > 0).VAlign(VerticalAlignment.Center).Visible(Current.Width >= 1000),
            LauncherControls.Command(Row([LauncherIcons.Line("seed-generate", 20f)]), () => Generate(Current.SeedInput))
                .Style(LauncherButtons.DialogPrimary).Width(SeedChrome.FieldHeight).Height(SeedChrome.FieldHeight).ToolTip(Resources.SeedGenerate)
                .Enabled(Ready && Current.SeedInput.Trim().Length > 0).Automation(new() { Name = Resources.SeedGenerate, AutomationId = "seed.generate" }),
        ]).Spacing(4f).Cell(6, 0),
    ])).Padding(12f, 10f).BorderBrush(LauncherTheme.Edge).BorderThickness(0, 0, 0, 1f);
}
internal sealed partial class SeedExplorerTitleBar(SeedExplorerState state, SeedExplorerPopups popups, Window window) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private ImageSource AppIcon => window.Icon?.AsImage ?? ImageSource.FromFile(
        System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "launcher.png"));

    private Ui Body => Grid([GridLength.Auto, GridLength.Auto, GridLength.Star()], [GridLength.Star()], [
        new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedBackToMap, "back", state.Controller.Regions.CloseScene, "seed.scene.back", Size: 48f)
            .Visible(Current.SceneOpen).Cell(0, 0),
        Image(AppIcon).Width(24f).Height(24f).VAlign(VerticalAlignment.Center).Margin(new Thickness(Current.SceneOpen ? 4f : 20f, 0f, 8f, 0f))
            .Automation(new() { Name = Resources.SeedTitle, AutomationId = "seed.logo" }).Cell(1, 0),
        When(Current.SceneOpen, new SeedSceneTitleBarHost(state), new SeedMapTitleBarHost(state, popups)).Cell(2, 0),
    ]).MinHeight(48f);
}

internal sealed partial class SeedMapTitleBar(SeedExplorerState state, SeedExplorerPopups popups) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private Ui Body => Grid([GridLength.Auto, GridLength.Auto, GridLength.Star(), GridLength.Auto, GridLength.Auto], [GridLength.Star()], [
        Text(Resources.SeedTitle).FontSize(13f).VAlign(VerticalAlignment.Center).Margin(new Thickness(0, 0, 10f, 0)).Cell(0, 0),
        new SeedSegments(LauncherAppearance.Capture(),[Resources.SeedMap, Resources.SeedSearch], Current.SearchMode ? 1 : 0,
            index => state.Controller.SelectMode(index == 1), "seed.mode", Current.Width < 1000 ? 180f : 220f).VAlign(VerticalAlignment.Center).Visible(!Current.SceneOpen).Cell(1, 0),
        DropDownButton(new SeedDropDownIconContent("star"), popups.Bookmarks).Style(SeedChrome.IconDropDown).Width(32f).Height(32f).ToolTip(Resources.SeedBookmarks)
            .VAlign(VerticalAlignment.Center).Automation(new() { Name = Resources.SeedBookmarks, AutomationId = "seed.bookmarks.open" }).Visible(!Current.SceneOpen).Cell(3, 0),
        LauncherControls.Command(Resources.SeedExport, () => _ = state.Controller.Map.ExportAsync())
            .Style(LauncherButtons.DialogSecondary.Padding(12f, 4f).LabelStyle(LauncherStyles.Text.FontSize(12f)))
            .MinHeight(32f).VAlign(VerticalAlignment.Center).Enabled(!Current.SceneOpen && Current.Seed.Length > 0 && !Current.Opening && !Current.MapBusy && !Current.Exporting)
            .Automation(new() { Name = Resources.SeedExport, AutomationId = "seed.export" }).Visible(!Current.SceneOpen).Cell(4, 0),
    ]).MinHeight(32f).VAlign(VerticalAlignment.Center).ColumnSpacing(8f).Margin(new Thickness(4f, 8f, 8f, 8f));
}

/// <summary>Combines scene navigation and location with the native caption band.</summary>
internal sealed partial class SeedSceneTitleBar(SeedExplorerState state) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private string Title => Current.ScenePosition is not { } p ? Resources.SeedPreviewStructure
        : p.Kind == "container" ? SeedRegionText.Name(Current, p.Id) : SeedText.Position(p, Current);
    private string Context => Current.ScenePosition is { } p ? SeedText.Dimension(Current.World.Dimension) + " · " + SeedText.Coordinates(p) : string.Empty;
    private Ui Body => Grid([GridLength.Star(), GridLength.Auto], [GridLength.Star()], [
        Column([
            Text(Title).Style(LauncherStyles.Text.FontSize(15f).Trimming(TextTrimming.CharacterEllipsis).MaxLines(1)),
            Text(Context).FontSize(10f).Foreground(LauncherTheme.Muted).FlowDirection(FlowDirection.LeftToRight),
        ]).Spacing(2f).VAlign(VerticalAlignment.Center).Cell(0, 0),
        new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedCopyTeleport, "copy", () => { if (Current.ScenePosition is { } p) state.Controller.Copy(SeedCommands.Teleport(Current.World, p.X, p.Y, p.Z)); }, "seed.scene.copy")
            .VAlign(VerticalAlignment.Center).Cell(1, 0),
    ]).ColumnSpacing(10f).Margin(new Thickness(4f, 4f, 8f, 4f));
}

/// <summary>Creates scene caption content for the mounted scene branch.</summary>
/// <param name="State">The current explorer window state.</param>
[View]
internal readonly partial record struct SeedSceneTitleBarHost(SeedExplorerState State)
{
    /// <summary>Creates navigation and location content inside the native caption band.</summary>
    public Ui Body => new SeedSceneTitleBar(State);
}

/// <summary>Creates the map caption content when returning from a scene.</summary>
/// <param name="State">The current explorer window state.</param>
/// <param name="Popups">The window-owned map flyouts.</param>
[View]
internal readonly partial record struct SeedMapTitleBarHost(SeedExplorerState State, SeedExplorerPopups Popups)
{
    /// <summary>Creates the map modes, bookmarks and export controls.</summary>
    public Ui Body => new SeedMapTitleBar(State, Popups);
}