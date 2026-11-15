// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

internal sealed partial class SeedSearchPanel(SeedExplorerState state, SeedExplorerPopups popups) : Control
{
    private readonly Dictionary<string, int> _keys = [];
    private int _key;
    private SeedExplorerSnapshot Current => state.Current;
    private int Key(string id) { if (!_keys.TryGetValue(id, out var key)) _keys[id] = key = ++_key; return key; }
    private string Progress => string.Format(Resources.SeedSearchProgressFormat, Current.Search?.Result.Checked ?? 0,
        Current.Search?.Result.Hits.Length ?? 0, (Current.Search?.Result.ElapsedMilliseconds ?? 0) / 1000);
    private bool CanResume => Current.Search is { } search && search.Result.Hits.Length < search.Query.MaxResults && !Current.Searching && !Current.Opening;
    private bool HasResults => Current.Search?.Result.Hits.Length > 0;
    private Ui Body => Border(Grid([GridLength.Star()], [GridLength.Auto, GridLength.Star(), GridLength.Auto], [
        Grid([GridLength.Star(), GridLength.Auto], [GridLength.Auto], [
            new SeedSegments(LauncherAppearance.Capture(),[Resources.SeedConditions, string.Format(Resources.SeedResultsFormat, Current.Search?.Result.Hits.Length ?? 0)], Current.SearchTab,
                value => state.Current = Current with { SearchTab = value }, "seed.search.tab").Cell(0, 0),
            DropDownButton(new SeedDropDownIconContent("history"), popups.History).Style(SeedChrome.IconDropDown).Width(32f).Height(32f).ToolTip(Resources.SeedHistory)
                .Enabled(!Current.Searching).Automation(new() { Name = Resources.SeedHistory, AutomationId = "seed.history.open" }).Cell(1, 0),
        ]).ColumnSpacing(4f).Margin(new Thickness(12f, 10f, 12f, 10f)).Cell(0, 0),
        Overlay([
            ScrollView(new SeedConditionsList(state)).Visible(Current.SearchTab == 0 && (!Current.CatalogLoading || Current.Catalog is not null)).Automation(new() { AutomationId = "seed.conditions" }),
            new SeedConditionPlaceholder(LauncherAppearance.Capture(),Current.CatalogLoading && Current.SearchMode).Margin(12f).VAlign(VerticalAlignment.Top)
                .Visible(Current.SearchTab == 0 && Current.Catalog is null && Current.CatalogLoading),
            ScrollView(new SeedResultsList(state)).Visible(Current.SearchTab == 1 && (Current.Searching || HasResults))
                .Automation(new() { AutomationId = "seed.results" }),
            new SeedSearchEmpty(LauncherAppearance.Capture(),Current).Visible(Current.SearchTab == 1 && !Current.Searching && !HasResults),
        ]).Cell(0, 1),
        Border(Column([
            Grid([GridLength.Star(), GridLength.Star(), GridLength.Star()], [GridLength.Auto, GridLength.Auto], [
                Text(Resources.SeedCandidateBudget).FontSize(10f).Foreground(LauncherTheme.Muted).Cell(0, 0),
                Text(Resources.SeedResultBudget).FontSize(10f).Foreground(LauncherTheme.Muted).Cell(1, 0),
                Text(Resources.SeedTimeBudget).FontSize(10f).Foreground(LauncherTheme.Muted).Cell(2, 0),
                new SeedNumber(Resources.SeedCandidateBudget, Current.SearchSeedBudget, 1, 1_000_000, value => state.Current = Current with { SearchSeedBudget = value }, "seed.budget.seeds", !Current.Searching).Cell(0, 1),
                new SeedNumber(Resources.SeedResultBudget, Current.SearchResultBudget, 1, 100, value => state.Current = Current with { SearchResultBudget = value }, "seed.budget.results", !Current.Searching).Cell(1, 1),
                new SeedNumber(Resources.SeedTimeBudget, Current.SearchSeconds, 1, 3600, value => state.Current = Current with { SearchSeconds = value }, "seed.budget.seconds", !Current.Searching).Cell(2, 1),
            ]).ColumnSpacing(6f).RowSpacing(5f),
            Text(Progress).FontSize(10f).Foreground(LauncherTheme.Muted).Wrap().Visible(Current.Searching || Current.Search is not null)
                .Automation(new() { Name = Progress, AutomationId = "seed.search.progress" }),
            Grid([GridLength.Star(), GridLength.Auto], [GridLength.Auto], [
                new SeedCommand(LauncherAppearance.Capture(),Current.Searching ? Resources.SeedStop : Resources.SeedStart,
                    () => { if (Current.Searching) { _ = state.Controller.Search.StopAsync(); } else { state.Current = Current with { SearchTab = 1 }; _ = state.Controller.Search.StartAsync(); } },
                    "seed.search.start", Current.Searching || (!Current.Opening && Current.Catalog is not null && Current.Profile is not null && Current.Conditions.Length > 0), !Current.Searching).Cell(0, 0),
                new SeedCommand(LauncherAppearance.Capture(),Resources.SeedResume, () => _ = state.Controller.Search.ResumeAsync(Current.Search!), "seed.search.resume", CanResume).Visible(CanResume).Cell(1, 0),
            ]).ColumnSpacing(CanResume ? 8f : 0f),
        ]).Spacing(9f)).Padding(12f).BorderBrush(LauncherTheme.Edge).BorderThickness(0, 1f, 0, 0).Cell(0, 2),
    ])).Background(LauncherTheme.Background).BorderBrush(LauncherTheme.Edge).BorderThickness(0, 0, 1f, 0);
}

[View]
internal readonly partial record struct SeedConditionCard(SeedCondition Condition, SeedExplorerSnapshot State, SeedSearchController Controller)
{
    /// <summary>Hosts one retained row/editor with stable native control identity.</summary>
    public Ui Body => new SeedConditionCardControl(Condition, State, Controller);
}

internal sealed partial class SeedConditionCardControl : Control
{
    internal SeedConditionCardControl(SeedCondition condition, SeedExplorerSnapshot state, SeedSearchController controller) { Condition = condition; State = state; Controller = controller; }
    private partial SeedCondition Condition { get; set; }
    private partial SeedExplorerSnapshot State { get; set; }
    private partial SeedSearchController Controller { get; set; }
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) { if (source is SeedConditionCardControl other) { Condition = other.Condition; State = other.State; Controller = other.Controller; } }

    private SeedTarget[] Targets => SeedText.Targets(State, Condition.Kind, Condition.Targets);
    private SeedCondition[] References => [.. State.Conditions.TakeWhile(item => item.Id != Condition.Id)];
    private string[] ReferenceIds => State.World.Dimension == "overworld" && State.Catalog?.SeedBoundSpawn != false ? ["spawn", "origin", .. References.Select(item => item.Id)] : ["origin", .. References.Select(item => item.Id)];
    private string[] ReferenceNames => [.. ReferenceIds.Select(id => id == "spawn" ? Resources.SeedSpawn : id == "origin" ? Resources.SeedOrigin
        : SeedText.TargetsName(References.First(item => item.Id == id), State))];
    private int ReferenceIndex => Array.IndexOf(ReferenceIds, Condition.Reference);
    /// <summary>Edits positive native constraints with explicit count, radius, original targets and a shared prior-condition reference.</summary>
    public Ui Body => Border(Column([
        Row([
            new SeedVisual(Condition.Kind == "biome" ? SeedAppearance.Biome(Condition.Targets[0]) : SeedAppearance.Structure(Condition.Targets[0]), SeedAppearance.Symbols, 28f),
            Text(Condition.Kind == "biome" ? Resources.SeedBiome : Resources.SeedStructure).FontSize(12f).Weight(1f).VAlign(VerticalAlignment.Center),
            new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedRemove, "delete", () => Controller.Remove(Condition.Id), "seed.condition.remove." + Condition.Id, !State.Searching, Critical: true),
        ]).Spacing(8f),
        new SeedChoice(LauncherAppearance.Capture(),Resources.SeedTarget, [.. Targets.Select(item => item.Name)], Array.FindIndex(Targets, item => SeedText.SameTargets(item.Targets, Condition.Targets)),
            index => { if (index >= 0 && index < Targets.Length) Controller.Change(Condition with { Targets = Targets[index].Targets }); }, "seed.condition.target." + Condition.Id, !State.Searching),
        Grid([GridLength.Star(), GridLength.Star()], [GridLength.Auto, GridLength.Auto], [
            Text(Resources.SeedRadius).FontSize(10f).Foreground(LauncherTheme.Muted).Cell(0, 0),
            Text(Condition.Kind == "biome" ? Resources.SeedBiomeStep : Resources.SeedMinimum).FontSize(10f).Foreground(LauncherTheme.Muted).Cell(1, 0),
            new SeedNumber(Resources.SeedRadius, Condition.Radius, 0, 8192, value => Controller.Change(Condition with { Radius = value }), "seed.condition.radius." + Condition.Id, !State.Searching).Cell(0, 1),
            new SeedNumber(Resources.SeedMinimum, Condition.Minimum, 1, 64, value => Controller.Change(Condition with { Minimum = value }), "seed.condition.count." + Condition.Id, !State.Searching)
                .Visible(Condition.Kind == "structure").Cell(1, 1),
            new SeedNumber(Resources.SeedBiomeStep, State.BiomeStep, 4, 256, Controller.ChangeBiomeStep, "seed.condition.step." + Condition.Id, !State.Searching)
                .Visible(Condition.Kind == "biome").Cell(1, 1),
        ]).ColumnSpacing(8f).RowSpacing(4f),
        new SeedChoice(LauncherAppearance.Capture(),Resources.SeedReference, ReferenceNames, ReferenceIndex,
            index => { if (index >= 0 && index < ReferenceIds.Length) Controller.Change(Condition with { Reference = ReferenceIds[index] }); }, "seed.condition.reference." + Condition.Id, !State.Searching),
        Grid([GridLength.Star(), GridLength.Pixels(96)], [GridLength.Auto], [
            new SeedChoice(LauncherAppearance.Capture(),Resources.SeedView, [Resources.SeedSurface, Resources.SeedSlice], Condition.Y is null ? 0 : 1,
                index => Controller.Change(Condition with { Y = index == 0 ? null : SeedText.DefaultSliceY(State) }), "seed.condition.slice." + Condition.Id, !State.Searching).Cell(0, 0),
            new SeedNumber("Y", Condition.Y ?? 64, SeedText.MinimumY(State), SeedText.MaximumY(State),
                value => Controller.Change(Condition with { Y = value }), "seed.condition.y." + Condition.Id, !State.Searching && Condition.Y is not null).Cell(1, 0),
        ]).ColumnSpacing(8f).Visible(Condition.Kind == "biome"),
    ]).Spacing(8f)).Padding(10f).Background(LauncherTheme.ControlBackground).BorderBrush(LauncherTheme.Edge).BorderThickness(1f);
}

[View]
internal readonly partial record struct SeedSearchResultCard(SeedSearchHit Hit, SeedExplorerSnapshot State, Action Open, Action Copy)
{
    /// <summary>Hosts one retained row/editor with stable native control identity.</summary>
    public Ui Body => new SeedSearchResultCardControl(Hit, State, Open, Copy);
}

internal sealed partial class SeedSearchResultCardControl : Control
{
    internal SeedSearchResultCardControl(SeedSearchHit hit, SeedExplorerSnapshot state, Action open, Action copy) { Hit = hit; State = state; Open = open; Copy = copy; }
    private partial SeedSearchHit Hit { get; set; }
    private partial SeedExplorerSnapshot State { get; set; }
    private partial Action Open { get; set; }
    private partial Action Copy { get; set; }
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) { if (source is SeedSearchResultCardControl other) { Hit = other.Hit; State = other.State; Open = other.Open; Copy = other.Copy; } }

    private SeedResultPreview? Preview => State.Previews.FirstOrDefault(item => item.Seed == Hit.Seed);
    private string Summary => string.Join(" · ", Hit.Matches.Select(match => SeedText.Position(match.Anchor, State)));
    /// <summary>Opens the exact result seed at its original world spawn; thumbnails contain only actual native samples.</summary>
    public Ui Body => Border(Overlay([
        Button(Grid([GridLength.Pixels(64f), GridLength.Star()], [GridLength.Auto], [
            Overlay([
                Shimmer(new Size(64f, 54f)).CornerRadius(0f).Active(Preview is null && State.SearchMode && State.SearchTab == 1 && State.Error.Length == 0).Visible(Preview is null)
                    .Automation(new() { Name = Resources.SeedPreviewPending, AutomationId = "seed.result.preview.loading." + Hit.Seed }),
                Image(Preview?.Image ?? Features.Instances.InstanceIconView.Transparent).Stretch(ImageStretch.Fill)
                    .Interpolation(ImageInterpolationMode.NearestNeighbor).Visible(Preview is not null),
            ]).Width(64f).Height(54f).VAlign(VerticalAlignment.Center).Cell(0, 0),
            Column([
                Text(Hit.Seed).FontSize(13f).Wrap().MinContentWidth(0f),
                Text(Summary).FontSize(10f).Foreground(LauncherTheme.Muted).Wrap().MinContentWidth(0f)
                    .Automation(new() { Name = Summary, AutomationId = "seed.result.summary." + Hit.Seed }),
            ]).Spacing(5f).MinContentWidth(0f).VAlign(VerticalAlignment.Center).Cell(1, 0),
        ]).ColumnSpacing(10f), Open).Style(SeedChrome.ListRow.Padding(new Thickness(10f, 10f, 46f, 10f)))
            .Automation(new() { Name = Hit.Seed, AutomationId = "seed.result." + Hit.Seed }),
        new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedCopySeed, "copy", Copy, "seed.result.copy." + Hit.Seed)
            .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Top).Margin(6f),
    ])).Background(State.Seed == Hit.Seed ? LauncherTheme.SelectionBackground : LauncherTheme.ControlBackground).BorderBrush(LauncherTheme.Edge).BorderThickness(1f);
}

internal sealed partial class SeedConditionsList(SeedExplorerState state) : Control
{
    private readonly Dictionary<string, int> _keys = [];
    private int _key;
    private int Key(SeedCondition item) { if (!_keys.TryGetValue(item.Id, out var key)) _keys[item.Id] = key = ++_key; return key; }
    private SeedExplorerSnapshot Current => state.Current;
    private Ui Body => Column([
        Border(new EmptyPlaceholderView("sliders", Resources.SeedConditionsEmpty, Resources.SeedConditionsEmptyDescription,
            LauncherTheme.Ink, LauncherTheme.Muted, "seed.conditions.empty")).Padding(8f, 20f).Visible(Current.Conditions.Length == 0),
        ItemsRepeater(Current.Conditions, item => Key(item), condition => new SeedConditionCard(condition, Current, state.Controller.Search), new StackLayout { Spacing = 10f }).KeyboardNavigation(singleTabStop: false),
        Grid([GridLength.Star(), GridLength.Star()], [GridLength.Auto], [
            new SeedCommand(LauncherAppearance.Capture(),Resources.SeedAddStructure, () => _ = state.Controller.Search.AddAsync("structure"), "seed.condition.add.structure", !Current.CatalogLoading && !Current.Opening && !Current.Searching && Current.Conditions.Length < 12 && Current.Profile is not null).Cell(0, 0),
            new SeedCommand(LauncherAppearance.Capture(),Resources.SeedAddBiome, () => _ = state.Controller.Search.AddAsync("biome"), "seed.condition.add.biome", !Current.CatalogLoading && !Current.Opening && !Current.Searching && Current.Conditions.Length < 12 && Current.Profile is not null).Cell(1, 0),
        ]).ColumnSpacing(8f),
    ]).Spacing(10f).Margin(new Thickness(12f, 0, 12f, 12f));
}

internal sealed partial class SeedResultsList(SeedExplorerState state) : Control
{
    private readonly Dictionary<string, int> _keys = [];
    private int _key;
    private int Key(SeedSearchHit item) { if (!_keys.TryGetValue(item.Seed, out var key)) _keys[item.Seed] = key = ++_key; return key; }
    private SeedExplorerSnapshot Current => state.Current;
    private string Status => Current.Search is { } search ? SeedText.Status(search.Result.Status) : Resources.SeedNoResults;
    private bool Animate => Current.Searching && Current.SearchMode && Current.SearchTab == 1;
    private bool HasResults => Current.Search?.Result.Hits.Length > 0;
    private Ui Body => Column([
        Text(Status).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap().Visible(!Current.Searching && HasResults),
        ItemsRepeater(Current.Search?.Result.Hits ?? [], item => Key(item), hit => new SeedSearchResultCard(hit, Current,
            () => _ = state.Controller.Map.ShowResultAsync(hit.Seed), () => state.Controller.Copy(hit.Seed)), new StackLayout { Spacing = 10f }).KeyboardNavigation(),
        Column([
            new SeedSearchPlaceholder(LauncherAppearance.Capture(),Animate, "seed.search.loading"),
            new SeedSearchPlaceholder(LauncherAppearance.Capture(),Animate, "seed.search.loading.1").Visible(!HasResults),
            new SeedSearchPlaceholder(LauncherAppearance.Capture(),Animate, "seed.search.loading.2").Visible(!HasResults),
        ]).Spacing(10f).Visible(Current.Searching),
    ]).Spacing(10f).Margin(new Thickness(12f, 0, 12f, 12f));
}

/// <summary>Uses the shared centered placeholder for an idle or finished search with no matches.</summary>
/// <param name="State">The frozen search state used to choose the terminal or initial empty message.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct SeedSearchEmpty(LauncherAppearance Theme, SeedExplorerSnapshot State)
{
    internal string Title => State.Search is { } search ? SeedText.Status(search.Result.Status) : Resources.SeedSearchNotStarted;
    internal string Icon => State.Search?.Result.Status is "time_limit" or "budget_exhausted" ? "history" : "search-empty";

    /// <summary>Places the status in the results viewport without inheriting the scroll content's natural height.</summary>
    public Ui Body => Border(new EmptyPlaceholderView(Icon, Title, State.Search is null ? string.Empty : Resources.SeedNoResults,
        Theme.Ink, Theme.Muted, "seed.search.empty")).Margin(20f);
}

/// <summary>Uses a bounded condition-shaped skeleton instead of stretching a result thumbnail across the viewport.</summary>
[View]
internal readonly partial record struct SeedConditionPlaceholder(LauncherAppearance Theme, bool Active)
{
    /// <summary>Reserves only the compact header and field rows while the original registry choices are loading.</summary>
    public Ui Body => Border(Column([
        Row([
            Shimmer(new Size(24f, 24f)).Width(24f).Height(24f).Active(Active).CornerRadius(0f)
                .Automation(new() { Name = Resources.SeedOpening, AutomationId = "seed.conditions.loading" }),
            Shimmer(new Size(112f, 12f)).Width(112f).Height(12f).Active(Active).CornerRadius(0f).VAlign(VerticalAlignment.Center),
        ]).Spacing(10f),
        Shimmer(new Size(200f, 32f)).Height(32f).Active(Active).CornerRadius(0f),
        Row([
            Shimmer(new Size(96f, 32f)).Width(96f).Height(32f).Active(Active).CornerRadius(0f),
            Shimmer(new Size(96f, 32f)).Width(96f).Height(32f).Active(Active).CornerRadius(0f),
        ]).Spacing(8f),
    ]).Spacing(10f)).Padding(10f).Background(Theme.ControlBackground).BorderBrush(Theme.Edge).BorderThickness(1f);
}

/// <summary>Reserves the same thumbnail and text rhythm as a pending real seed result.</summary>
/// <param name="Active">Whether visible placeholder blocks should animate for the current attempt.</param>
/// <param name="Id">The unique loading marker, with the first row naming the current search activity.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct SeedSearchPlaceholder(LauncherAppearance Theme, bool Active, string Id)
{
    /// <summary>Animates native shimmer blocks only while the visible results tab is searching.</summary>
    public Ui Body => Border(Row([
        Shimmer(new Size(64f, 54f)).CornerRadius(0f).Active(Active)
            .Automation(new() { AutomationId = Id, Name = Id == "seed.search.loading" ? Resources.SeedSearching : string.Empty }),
        Column([
            Shimmer(new Size(120f, 14f)).CornerRadius(0f).Active(Active),
            Shimmer(new Size(164f, 10f)).CornerRadius(0f).Active(Active),
            Shimmer(new Size(90f, 10f)).CornerRadius(0f).Active(Active),
        ]).Spacing(7f).VAlign(VerticalAlignment.Center).Weight(1f),
    ]).Spacing(10f)).Padding(6f).Background(Theme.ControlBackground).BorderBrush(Theme.Edge).BorderThickness(1f);
}