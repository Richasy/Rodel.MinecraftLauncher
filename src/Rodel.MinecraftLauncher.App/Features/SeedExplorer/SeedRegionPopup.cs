// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Edits an inclusive area and presents frozen native loot and material counts in a light-dismiss surface.</summary>
internal sealed partial class SeedRegionPopup(SeedExplorerState state) : Control
{
    private Ui Body => When(state.Current.RegionContainer is not null,
        new SeedRegionInventoryHost(state), new SeedRegionFormScrollHost(state));
}

/// <summary>Owns a fresh container frame for each mounted conditional branch.</summary>
/// <param name="State">The owning explorer state, whose frozen results outlive the branch.</param>
[View]
internal readonly partial record struct SeedRegionInventoryHost(SeedExplorerState State)
{
    /// <summary>Creates the frame owned and disposed by this mounted branch.</summary>
    public Ui Body => new SeedRegionInventory(State);
}

/// <summary>Owns the area form's viewport for each mounted conditional branch.</summary>
/// <param name="State">The owning explorer state.</param>
[View]
internal readonly partial record struct SeedRegionFormScrollHost(SeedExplorerState State)
{
    /// <summary>Creates a viewport without reusing an unmounted ScrollView instance.</summary>
    public Ui Body => new SeedRegionFormViewport(State);
}

internal sealed partial class SeedRegionFormViewport : Control
{
    private readonly Sprout.Controls.ScrollView _scroll;
    internal SeedRegionFormViewport(SeedExplorerState state) { State = state; _scroll = new(new SeedRegionFormViewportContent(this)); _scroll.Padding(12f); }
    internal SeedExplorerState State { get; private set; }
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) { if (source is SeedRegionFormViewport other) State = other.State; }
    private Ui Body => _scroll.Width(Math.Min(432f, State.Current.MapWidth - 16f)).MaxHeight(Math.Max(184f, State.Current.MapHeight - 16f));
}

internal sealed partial class SeedRegionFormViewportContent(SeedRegionFormViewport owner) : Control
{
    private Ui Body => new SeedRegionForm(owner.State);
}

internal sealed partial class SeedRegionForm(SeedExplorerState state) : Control
{
    private SeedExplorerState OwnerState => state;
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) { if (source is SeedRegionForm other) state = other.OwnerState; }

    private SeedExplorerSnapshot Current => state.Current;
    private SeedBounds Bounds => Current.RegionBounds ?? state.Controller.Regions.DefaultBounds();
    private SeedDimension? Dimension => Current.Catalog?.Dimensions.First(item => item.Id == Current.World.Dimension);
    private bool Ready => (Current.UseSavedMap || Current.Seed.Length > 0) && !Current.Opening && !Current.RegionBusy;
    private string Status => Current.RegionBusy ? SeedRegionText.Progress(Current.RegionProgress, Current.UseSavedMap)
        : Current.RegionData is { } data ? string.Format(Resources.SeedAreaSummaryFormat, data.Materials.Sum(item => item.Count), data.Containers.Length, data.ElapsedMilliseconds / 1000) : string.Empty;
    private Ui Body => Column([
        Row([Text(Resources.SeedAnalyzeArea).FontSize(16f).Weight(1f).VAlign(VerticalAlignment.Center),
            Button(Resources.SeedSelectArea, state.Controller.Regions.BeginSelection).Style(SeedChrome.Quiet).Height(32f).Enabled(Ready)
                .Automation(new() { Name = Resources.SeedSelectArea, AutomationId = "seed.area.select" }),
            new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedEditBounds, "edit", () => state.Current = Current with { RegionEditBounds = !Current.RegionEditBounds }, "seed.area.edit-bounds", Ready),
        ]).Spacing(6f),
        Text(SeedRegionText.Range(Bounds)).FontSize(11f).Foreground(LauncherTheme.Muted).Wrap().FlowDirection(FlowDirection.LeftToRight)
            .Automation(new() { Name = SeedRegionText.Range(Bounds), AutomationId = "seed.area.bounds" }),
        Column([
            Grid([GridLength.Star(), GridLength.Star()], [GridLength.Auto, GridLength.Auto], [
                new SeedAreaField(LauncherAppearance.Capture(),"X₁", Bounds.MinX, -30_000_000, 29_999_999, value => Set(Bounds with { MinX = value }), "min-x", Ready).Cell(0, 0),
                new SeedAreaField(LauncherAppearance.Capture(),"X₂", Bounds.MaxX, -30_000_000, 29_999_999, value => Set(Bounds with { MaxX = value }), "max-x", Ready).Cell(1, 0),
                new SeedAreaField(LauncherAppearance.Capture(),"Z₁", Bounds.MinZ, -30_000_000, 29_999_999, value => Set(Bounds with { MinZ = value }), "min-z", Ready).Cell(0, 1),
                new SeedAreaField(LauncherAppearance.Capture(),"Z₂", Bounds.MaxZ, -30_000_000, 29_999_999, value => Set(Bounds with { MaxZ = value }), "max-z", Ready).Cell(1, 1),
            ]).ColumnSpacing(10f).RowSpacing(6f),
            Button(Resources.SeedMapCenter, () => state.Controller.Regions.SelectBounds(state.Controller.Regions.DefaultBounds())).Style(SeedChrome.Quiet).Height(28f).Enabled(Ready)
                .Automation(new() { Name = Resources.SeedMapCenter, AutomationId = "seed.area.center" }),
        ]).Spacing(4f).Visible(Current.RegionEditBounds),
        Grid([GridLength.Auto, GridLength.Star(), GridLength.Auto, GridLength.Star()], [GridLength.Auto], [
            Text("Y").FontSize(11f).Foreground(LauncherTheme.Muted).VAlign(VerticalAlignment.Center).Cell(0, 0),
            new SeedNumber(Resources.SeedFromY, Bounds.MinY, Dimension?.MinY ?? -64, (Dimension?.MinY ?? -64) + (Dimension?.Height ?? 384) - 1,
                value => Set(Bounds with { MinY = value }), "seed.area.min-y", Ready).Cell(1, 0),
            Text("–").FontSize(11f).Foreground(LauncherTheme.Muted).VAlign(VerticalAlignment.Center).Cell(2, 0),
            new SeedNumber(Resources.SeedToY, Bounds.MaxY, Dimension?.MinY ?? -64, (Dimension?.MinY ?? -64) + (Dimension?.Height ?? 384) - 1,
                value => Set(Bounds with { MaxY = value }), "seed.area.max-y", Ready).Cell(3, 0),
        ]).ColumnSpacing(8f),
        Row([new SeedCommand(LauncherAppearance.Capture(),Resources.SeedAnalyze, () => _ = state.Controller.Regions.AnalyzeAsync(), "seed.area.analyze", Ready, true).Weight(1f),
            new SeedCommand(LauncherAppearance.Capture(),Resources.SeedStructureQueryStop, state.Controller.Regions.CancelAnalysis, "seed.area.stop").Visible(Current.RegionBusy)]).Spacing(8f),
        Text(Current.RegionError).FontSize(11f).Foreground(LauncherTheme.Error).Wrap().Visible(Current.RegionError.Length > 0).Automation(new() { Name = Current.RegionError, AutomationId = "seed.area.error" }),
        Text(Status).FontSize(11f).Foreground(LauncherTheme.Muted).Wrap().Visible(Current.RegionBusy || Current.RegionData is not null).Automation(new() { Name = Status, AutomationId = "seed.area.status" }),
        Text(Current.RegionData is { } result ? SeedRegionText.Range(result.Bounds) : string.Empty).FontSize(10f).Foreground(LauncherTheme.Muted).FlowDirection(FlowDirection.LeftToRight).Wrap(),
        Text(Resources.SeedAreaDirty).FontSize(11f).Foreground(LauncherTheme.Muted).Wrap().Visible(Current.RegionData is { } old && old.Bounds != Bounds),
        new SeedSegments(LauncherAppearance.Capture(),[Resources.SeedLoot, Resources.SeedMaterials], Current.RegionTab, index => state.Current = Current with { RegionTab = index }, "seed.area.tab", Math.Max(0f, Math.Min(408f, Current.MapWidth - 40f))),
        TextBox(Current.RegionFilter, value => state.Current = Current with { RegionFilter = value }).Style(LauncherStyles.Input).Placeholder(Resources.SeedFilter).MinHeight(34f)
            .Automation(new() { Name = Resources.SeedFilter, AutomationId = "seed.area.filter" }),
        new LauncherCheckBox(LauncherAppearance.Capture(),Resources.SeedOnlyOres, Current.RegionOnlyOres, () => state.Current = Current with { RegionOnlyOres = !Current.RegionOnlyOres }, true, "seed.area.ores", FontSize: 12f).Visible(Current.RegionTab == 1),
        new SeedRegionResults(state),
        Text(Resources.SeedAreaHelp).FontSize(10f).Foreground(LauncherTheme.Muted).Wrap(),
        Text(Resources.SeedLootContext).FontSize(10f).Foreground(LauncherTheme.Muted).Wrap().Visible(!Current.UseSavedMap),
        Text(SeedRegionText.Policy(Current, Current.RegionData)).FontSize(10f).Foreground(LauncherTheme.Muted).Wrap(),
    ]).Spacing(8f);

    private void Set(SeedBounds bounds) => state.Controller.Regions.SelectBounds(bounds);
}

[View]
internal readonly partial record struct SeedAreaField(LauncherAppearance Theme, string Label, int Value, int Minimum, int Maximum, Action<int> Change, string Id, bool Enabled)
{
    /// <summary>Edits a single inclusive region boundary.</summary>
    public Ui Body => Column([Text(Label).FontSize(10f).Foreground(Theme.Muted), new SeedNumber(Label, Value, Minimum, Maximum, Change, "seed.area." + Id, Enabled)]).Spacing(2f);
}

internal sealed partial class SeedRegionResults(SeedExplorerState state) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private SeedContainer[] Containers => Current.RegionData?.Containers.Where(container => SeedRegionText.Name(Current, container.Id).Contains(Current.RegionFilter, StringComparison.OrdinalIgnoreCase)
        || container.Items.Any(item => SeedRegionText.ItemMatches(Current, item, Current.RegionFilter))).ToArray() ?? [];
    private SeedBlockCount[] Materials => Current.RegionData?.Materials.Where(item => item.Id is not ("minecraft:air" or "minecraft:cave_air" or "minecraft:void_air")
        && (!Current.RegionOnlyOres || item.Id.EndsWith("_ore", StringComparison.Ordinal) || item.Id == "minecraft:ancient_debris")
        && (item.Id.Contains(Current.RegionFilter, StringComparison.OrdinalIgnoreCase) || SeedRegionText.Name(Current, item.Id).Contains(Current.RegionFilter, StringComparison.OrdinalIgnoreCase))).OrderByDescending(item => item.Count).ToArray() ?? [];
    private Ui Body => Overlay([Column([
        ItemsRepeater(Containers, container => Array.IndexOf(Current.RegionData!.Containers, container), container => new SeedContainerRow(Current, container, () => state.Controller.Regions.OpenRegionContainer(container), "area")).KeyboardNavigation(),
        Text(Resources.SeedNoResults).FontSize(12f).Foreground(LauncherTheme.Muted).Visible(Current.RegionData is not null && Containers.Length == 0),
    ]).Spacing(4f).Visible(Current.RegionTab == 0), Column([
        ItemsRepeater(Materials, item => Array.IndexOf(Current.RegionData!.Materials, item), item => new SeedMaterialRow(Current, item)),
        Text(Resources.SeedNoResults).FontSize(12f).Foreground(LauncherTheme.Muted).Visible(Current.RegionData is not null && Materials.Length == 0),
    ]).Spacing(4f).Visible(Current.RegionTab == 1)]);
}

[View]
internal readonly partial record struct SeedMaterialRow(SeedExplorerSnapshot State, SeedBlockCount Material)
{
    /// <summary>Shows a real inclusive-volume count with its original resource image when available.</summary>
    public Ui Body => Row([
        Image(State.ItemImages.GetValueOrDefault(Material.Id) ?? Features.Instances.InstanceIconView.Transparent).Width(28f).Height(28f).Stretch(ImageStretch.Uniform).Interpolation(ImageInterpolationMode.NearestNeighbor),
        Text(SeedRegionText.Name(State, Material.Id)).FontSize(12f).Wrap().Weight(1f)
            .Automation(new() { Name = SeedRegionText.Name(State, Material.Id) + ": " + Material.Count, AutomationId = "seed.area.material." + Material.Id }),
        Text(Material.Count.ToString("N0")).FontSize(12f).FlowDirection(FlowDirection.LeftToRight),
    ]).Spacing(10f).MinHeight(38f);
}