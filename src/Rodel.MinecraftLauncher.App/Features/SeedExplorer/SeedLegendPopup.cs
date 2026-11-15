// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

internal sealed partial class SeedLegendPopup : Control
{
    private readonly SeedExplorerState state;
    private readonly bool layers;
    private readonly Action? showLegend;
    private readonly Sprout.Controls.ScrollView _list;
    internal SeedLegendPopup(SeedExplorerState state, bool layers, Action? showLegend = null) { this.state = state; this.layers = layers; this.showLegend = showLegend; Query = string.Empty; _list = new(new SeedLegendRows(this)); }
    private partial int Tab { get; set; }
    private partial string Query { get; set; }
    internal SeedExplorerSnapshot Current => state.Current;
    internal SeedTarget[] Items => Current.Catalog is not { } catalog ? [] : [.. (Tab == 0
        ? catalog.Structures.Where(item => !layers || item.Dimensions.Contains(Current.World.Dimension)).Select(item => new SeedTarget(item.Id, SeedText.Structure(item.Id, Current), [item.Id], SeedAppearance.Structure(item.Id)))
        : catalog.Biomes.Select(item => new SeedTarget(item.Id, SeedText.Biome(item.Id, Current), [item.Id], SeedAppearance.Biome(item.Id))))
        .Where(item => Query.Trim().Length == 0 || item.Name.Contains(Query.Trim(), StringComparison.CurrentCultureIgnoreCase) || item.Id.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderBy(item => item.Name, StringComparer.CurrentCulture)];
    internal int Key(SeedTarget item) => Array.FindIndex(Tab == 0 ? Current.Catalog!.Structures.Select(entry => entry.Id).ToArray() : [.. Current.Catalog!.Biomes.Select(entry => entry.Id)], id => id == item.Id);
    internal void Toggle(string id)
    {
        var selected = (Current.StructureLayers ?? [.. Current.Catalog!.Structures.Select(item => item.Id)]).ToHashSet(StringComparer.Ordinal);
        if (!selected.Remove(id)) selected.Add(id);
        SetLayers([.. selected]);
    }
    private void SetLayers(string[]? selected)
    {
        state.Current = Current with { StructureLayers = selected };
        if (!Current.UseSavedMap) state.Controller.Map.ResumeViewport();
    }
    internal bool CanToggle => layers && Tab == 0;
    private Ui Body => Column([
        Column([
        Text(layers ? Resources.SeedLayers : Resources.SeedLegend).FontSize(16f),
        new SeedSegments(LauncherAppearance.Capture(),[Resources.SeedStructures, Resources.SeedBiomes], Tab, value => Tab = value, layers ? "seed.layers.tab" : "seed.legend.tab", 312f),
        TextBox(Query, value => Query = value).Placeholder(Resources.SeedFilter).Style(LauncherStyles.Input).MinHeight(32f)
            .Automation(new() { Name = Resources.SeedFilter, AutomationId = layers ? "seed.layers.filter" : "seed.legend.filter" }),
        Column([
            new LauncherCheckBox(LauncherAppearance.Capture(),Resources.SeedTerrain, Current.Terrain || Current.Catalog is { Biomes.Length: 0 }, () => state.Current = Current with { Terrain = !Current.Terrain }, Current.Catalog is not { Biomes.Length: 0 }, "seed.layers.terrain", FontSize: 12f),
            new LauncherCheckBox(LauncherAppearance.Capture(),Resources.SeedOrigin, Current.ShowOrigin, () => state.Current = Current with { ShowOrigin = !Current.ShowOrigin }, true, "seed.layers.origin", FontSize: 12f),
            new LauncherCheckBox(LauncherAppearance.Capture(),Resources.SeedMatches, Current.ShowBiomeWitnesses, () => state.Current = Current with { ShowBiomeWitnesses = !Current.ShowBiomeWitnesses }, true, "seed.layers.matches", FontSize: 12f),
            Grid([GridLength.Star(), GridLength.Star()], [GridLength.Auto], [
                new SeedCommand(LauncherAppearance.Capture(),Resources.SeedAll, () => SetLayers(null), "seed.layers.all").Cell(0, 0),
                new SeedCommand(LauncherAppearance.Capture(),Resources.SeedNone, () => SetLayers([]), "seed.layers.none").Cell(1, 0),
            ]).ColumnSpacing(8f).Visible(Tab == 0),
        ]).Spacing(8f).Visible(layers),
        ]).Spacing(10f).Margin(new Thickness(12f, 12f, 12f, 10f)),
        _list.Padding(new Thickness(12f, 0f, 12f, 12f))
            .MaxHeight(Math.Max(100, Math.Min(350, Current.Height - (layers ? 340 : 230))) + 12f),
        Text(Resources.SeedSamplingNote).FontSize(10f).Foreground(LauncherTheme.Muted).Wrap().Visible(Tab == 1)
            .Margin(new Thickness(12f, 0f, 12f, 12f)),
        new SeedCommand(LauncherAppearance.Capture(),Resources.SeedLegend, () => showLegend?.Invoke(), "seed.legend.open").Visible(layers)
            .Margin(new Thickness(12f, 0f, 12f, 12f)),
    ]).Width(336f);
}

[View]
internal readonly partial record struct SeedLegendRow(SeedTarget Target, SeedExplorerSnapshot State, bool Layers, Action Toggle)
{
    /// <summary>Hosts one retained row/editor with stable native control identity.</summary>
    public Ui Body => new SeedLegendRowControl(Target, State, Layers, Toggle);
}

internal sealed partial class SeedLegendRowControl : Control
{
    internal SeedLegendRowControl(SeedTarget target, SeedExplorerSnapshot state, bool layers, Action toggle) { Target = target; State = state; Layers = layers; Toggle = toggle; }
    private partial SeedTarget Target { get; set; }
    private partial SeedExplorerSnapshot State { get; set; }
    private partial bool Layers { get; set; }
    private partial Action Toggle { get; set; }
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) { if (source is SeedLegendRowControl other) { Target = other.Target; State = other.State; Layers = other.Layers; Toggle = other.Toggle; } }

    /// <summary>Keeps the exact pin visual/color next to the original ID and optional structure visibility switch.</summary>
    public Ui Body => Row([
        new SeedVisual(Target.Appearance, SeedAppearance.Symbols, 28f),
        Column([Text(Target.Name).FontSize(12f).Wrap(), Text(Target.Id).FontSize(10f).Foreground(LauncherTheme.Muted)]).Spacing(2f).Weight(1f),
        new LauncherCheckBox(LauncherAppearance.Capture(),string.Empty, State.StructureLayers is null || State.StructureLayers.Contains(Target.Id), Toggle, true,
            "seed.layer." + Target.Id, AccessibleName: Target.Name, BoxSize: 16f, Spacing: 0f).Visible(Layers),
    ]).Spacing(10f).Margin(new Thickness(2f, 6f, 2f, 6f));
}

internal sealed partial class SeedLegendRows(SeedLegendPopup owner) : Control
{
    private Ui Body => Column([ItemsRepeater(owner.Items, keySelector: item => owner.Key(item), template: item => new SeedLegendRow(item, owner.Current, owner.CanToggle, () => owner.Toggle(item.Id))).KeyboardNavigation()]);
}