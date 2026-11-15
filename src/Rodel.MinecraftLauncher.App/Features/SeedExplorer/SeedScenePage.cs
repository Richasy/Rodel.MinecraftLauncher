// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Features.SeedExplorer.Rendering;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reactive;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Explores one actual original block volume with an independent viewport, layer controls and native container inventory.</summary>
internal sealed partial class SeedScenePage : Control
{
    private readonly SeedExplorerState _state;
    private readonly SeedSceneViewport _viewport;
    private readonly Sprout.Controls.ScrollView _inspector;
    private readonly Flyout _missing;
    private readonly Flyout _options;
    internal SeedScenePage(SeedExplorerState state)
    {
        _state = state; _viewport = new(state); _inspector = new(new SeedSceneInspector(state));
        _missing = new Flyout(_ => new SeedMissingModelsPopup(state)) { Placement = PopupPlacement.TopEdgeAlignedRight, ShouldConstrainToRootBounds = true };
        _options = new Flyout(close => new SeedSceneOptions(state, close))
        {
            Placement = PopupPlacement.BottomEdgeAlignedRight,
            ShouldConstrainToRootBounds = true,
            ChromeResolver = theme => LauncherMenus.ResolvePopupChrome(theme, 12f)
        };
        _missing.ChromeResolver = theme => LauncherMenus.ResolvePopupChrome(theme, 0f);
    }
    private SeedExplorerSnapshot Current => _state.Current;
    private bool ShowingProgressCard => Current.SceneBusy || (Current.SceneMesh is null && Current.SceneError.Length == 0);
    private string ClipboardStatus => Current.Phase == "copied" ? Resources.SeedCopied : Current.Phase == "copy_failed" ? Resources.SeedCopyFailed : string.Empty;
    private float InspectorWidth => Current.Width < 1000 ? 272f : 320f;
    private string ModelStatus => Current.SceneMeshing ? Resources.SeedStageMesh : Current.SceneBusy ? SeedRegionText.Progress(Current.SceneProgress, Current.UseSavedMap && !Current.ScenePreview)
        : Current.SceneMesh is { } mesh ? string.Format(Resources.SeedLayerRangeFormat, mesh.Blocks, mesh.MinY, mesh.MaxY) : Resources.SeedPreviewPending;
    private string Summary => Current.UseSavedMap && Current.ScenePreview ? string.Format(Resources.SeedMapStatusFormat, Resources.SeedWorldPreviewStatus, ModelStatus) : ModelStatus;

    private Ui Body => Grid([GridLength.Star()], [GridLength.Star(), GridLength.Auto], [
        Grid([GridLength.Star(), GridLength.Pixels(InspectorWidth)], [GridLength.Auto, GridLength.Star()], [
            Border(Grid([GridLength.Pixels(120f), GridLength.Auto, GridLength.Pixels(96f), GridLength.Auto, GridLength.Pixels(96f), GridLength.Star(), GridLength.Auto], [GridLength.Star()], [
                new SeedSceneViewMode(_state).VAlign(VerticalAlignment.Center).Cell(0, 0),
                Text("Y").FontSize(11f).Foreground(LauncherTheme.Muted).VAlign(VerticalAlignment.Center).Cell(1, 0),
                new SeedNumber(Resources.SeedFromY, Current.SceneMinY, Current.SceneData?.Bounds.MinY ?? -64, Current.SceneData?.Bounds.MaxY ?? 319,
                    value => _state.Controller.Regions.Slice(value, Current.SceneMaxY), "seed.scene.min-y", Current.Scene is not null).VAlign(VerticalAlignment.Center).Cell(2, 0),
                Text("–").FontSize(11f).Foreground(LauncherTheme.Muted).VAlign(VerticalAlignment.Center).Cell(3, 0),
                new SeedNumber(Resources.SeedToY, Current.SceneMaxY, Current.SceneData?.Bounds.MinY ?? -64, Current.SceneData?.Bounds.MaxY ?? 319,
                    value => _state.Controller.Regions.Slice(Current.SceneMinY, value), "seed.scene.max-y", Current.Scene is not null).VAlign(VerticalAlignment.Center).Cell(4, 0),
                DropDownButton(new SeedDropDownIconContent("more"), _options).Style(SeedChrome.IconDropDown).Width(32f).Height(32f).ToolTip(Resources.SeedView)
                    .Automation(new() { Name = Resources.SeedView, AutomationId = "seed.scene.options" }).VAlign(VerticalAlignment.Center).Cell(6, 0),
            ]).ColumnSpacing(6f).Margin(12f, 0f)).MinHeight(56f).BorderBrush(LauncherTheme.Edge).BorderThickness(0, 0, 0, 1f).Cell(0, 0),
            Border(Grid([GridLength.Auto, GridLength.Star()], [GridLength.Star()], [
                new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedBackToList, "back", _state.Controller.Regions.ReturnToContainerList, "seed.scene.inventory.back")
                    .Visible(Current.SceneContainer is not null).VAlign(VerticalAlignment.Center).Cell(0, 0),
                Text(Current.SceneContainer is { } container ? SeedRegionText.Name(Current, container.Id) : string.Format(Resources.SeedContainerCountFormat, Current.SceneData?.Containers.Length ?? 0))
                    .FontSize(12f).VAlign(VerticalAlignment.Center).Automation(new() { AutomationId = "seed.scene.inspector-title" }).Cell(1, 0),
            ]).ColumnSpacing(Current.SceneContainer is null ? 0f : 8f).Margin(12f, 0f)).MinHeight(56f).BorderBrush(LauncherTheme.Edge).BorderThickness(1f, 0, 0, 1f).Cell(1, 0),
            Grid([GridLength.Star()], [GridLength.Auto, GridLength.Star()], [
                Border(Row([
                    Text(Current.SceneError).FontSize(11f).Foreground(LauncherTheme.Error).Wrap().Weight(1f).Automation(new() { Name = Current.SceneError, AutomationId = "seed.scene.error" }),
                    new SeedCommand(LauncherAppearance.Capture(),Resources.AccountRetry, () => Retry(), "seed.scene.retry"),
                ]).Spacing(8f)).Padding(12f, 6f).Visible(Current.SceneError.Length > 0).Cell(0, 0),
                Overlay([
                    _viewport,
                    Column([
                        new SeedLoadingView(LauncherAppearance.Capture(),Resources.SeedLoadingStructure, ModelStatus, Current.SceneBusy, "seed.scene.status",
                            Current.SceneProgress is { Total: > 0 } progress && !Current.SceneMeshing ? progress.Completed * 100d / progress.Total : null),
                        new SeedCommand(LauncherAppearance.Capture(),Resources.SeedStructureQueryStop, _state.Controller.Regions.CloseScene, "seed.scene.stop"),
                    ]).Spacing(12f).Width(300f).MaxWidth(320f).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
                        .Visible(ShowingProgressCard),
                    Text(Resources.SeedPreviewEmpty).FontSize(13f).Foreground(LauncherTheme.Muted).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
                        .Visible(Current.SceneMesh is { Vertices.Length: 0 }),
                ]).Cell(0, 1),
            ]).Cell(0, 1),
            Border(_inspector).BorderBrush(LauncherTheme.Edge).BorderThickness(1f, 0, 0, 0).Background(LauncherTheme.Background).Cell(1, 1),
        ]).Cell(0, 0),
        Border(Grid([GridLength.Star(), GridLength.Auto, GridLength.Auto], [GridLength.Star()], [
            Text(Summary).Style(LauncherStyles.Text.FontSize(10f).MaxLines(1).Trimming(TextTrimming.CharacterEllipsis)).Foreground(LauncherTheme.Muted).VAlign(VerticalAlignment.Center).TooltipWhenTrimmed()
                .Automation(new() { Name = Summary, AutomationId = "seed.scene.summary" }).Visible(!ShowingProgressCard).Cell(0, 0),
            Text(ClipboardStatus).FontSize(10f).Foreground(LauncherTheme.Muted).Wrap().Visible(ClipboardStatus.Length > 0).VAlign(VerticalAlignment.Center)
                .Automation(new() { Name = ClipboardStatus, AutomationId = "seed.scene.feedback" }).Cell(1, 0),
            DropDownButton(string.Format(Resources.SeedMissingModelsFormat, Current.Scene?.UnavailableModels.Count ?? 0), _missing).Style(SeedChrome.DropDown).MinHeight(24f).VAlign(VerticalAlignment.Center)
                .Visible(Current.Scene?.UnavailableModels.Count > 0).Automation(new() { AutomationId = "seed.scene.missing" }).Cell(2, 0),
        ]).ColumnSpacing(8f).Margin(12f, 4f)).MinHeight(36f).BorderBrush(LauncherTheme.Edge).BorderThickness(0, 1f, 0, 0).Cell(0, 1),
    ]).Background(LauncherTheme.Background).BorderBrush(LauncherTheme.Edge).BorderThickness(0, 1f, 0, 0);

    private void Retry()
    {
        if (Current.Scene is null && Current.ScenePosition is { } position)
        {
            _ = _state.Controller.Regions.OpenSceneAsync(position);
        }
        else
        {
            var mesh = Current.SceneMesh;
            _state.Current = Current with
            {
                SceneError = string.Empty,
                SceneRenderVersion = Current.SceneRenderVersion + 1,
                SceneMinY = mesh?.MinY ?? Current.SceneMinY,
                SceneMaxY = mesh?.MaxY ?? Current.SceneMaxY
            };
            _ = _state.Controller.Regions.BuildMeshAsync();
        }
    }

    /// <inheritdoc/>
    protected override IDisposable? OnMounted() => new Effect(() =>
    {
        if (!Current.SceneOpen) { _options.Hide(); _missing.Hide(); }
    });
}

internal sealed partial class SeedSceneInspector(SeedExplorerState state) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private Ui Body => When(Current.SceneContainer is not null, new SeedInventoryHost(state, Current.SceneContainer ?? new(), false), new SeedSceneContainersHost(state));
}

internal sealed partial class SeedSceneContainers(SeedExplorerState state) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private Ui Body => Column([
        TextBox(Current.SceneFilter, value => state.Current = Current with { SceneFilter = value }).Style(LauncherStyles.Input).Placeholder(Resources.SeedFilter).MinHeight(34f)
            .Automation(new() { Name = Resources.SeedFilter, AutomationId = "seed.scene.filter" }),
        ItemsRepeater(Filtered, container => Array.IndexOf(Current.SceneData!.Containers, container), container =>
            new SeedSceneContainerItem(LauncherAppearance.Capture(),Current, container, () => state.Controller.Regions.SelectContainer(container), container != Filtered.LastOrDefault())).KeyboardNavigation(),
        Text(Resources.SeedNoResults).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap().Visible(Filtered.Length == 0 && !Current.SceneBusy),
    ]).Spacing(10f).Margin(new Thickness(12f, 8f, 12f, 12f));
    private SeedContainer[] Filtered => Current.SceneData?.Containers.Where(container => Current.SceneFilter.Length == 0 || SeedRegionText.Name(Current, container.Id).Contains(Current.SceneFilter, StringComparison.OrdinalIgnoreCase)
        || container.Items.Any(item => SeedRegionText.ItemMatches(Current, item, Current.SceneFilter))).ToArray() ?? [];
}

/// <summary>Frames one scene container result with optional separation from its following row.</summary>
/// <param name="State">The frozen explorer context used to name and draw this result.</param>
/// <param name="Container">The original container represented by this row.</param>
/// <param name="Open">Opens this container in the shared inspector.</param>
/// <param name="Divider">Whether another row follows and needs a separating bottom line.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct SeedSceneContainerItem(LauncherAppearance Theme, SeedExplorerSnapshot State, SeedContainer Container, Action Open, bool Divider)
{
    /// <summary>Separates adjacent scene results without leaving a trailing line after the final item.</summary>
    public Ui Body => Border(new SeedContainerRow(State, Container, Open, "scene").Margin(0f, 6f))
        .BorderBrush(Theme.Edge).BorderThickness(0f, 0f, 0f, Divider ? 1f : 0f);
}

[View]
internal readonly partial record struct SeedContainerRow(SeedExplorerSnapshot State, SeedContainer Container, Action Open, string Scope)
{
    /// <summary>Retains a native container result row.</summary>
    public Ui Body => new SeedContainerRowControl(State, Container, Open, Scope);
}

internal sealed partial class SeedContainerRowControl : Control
{
    internal SeedContainerRowControl(SeedExplorerSnapshot state, SeedContainer container, Action open, string scope) { State = state; Container = container; Open = open; ScopeName = scope; }
    private partial SeedExplorerSnapshot State { get; set; }
    private partial SeedContainer Container { get; set; }
    private partial Action Open { get; set; }
    private partial string ScopeName { get; set; }
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) { if (source is SeedContainerRowControl other) { State = other.State; Container = other.Container; Open = other.Open; ScopeName = other.ScopeName; } }
    private ImageSource? PreviewImage(int index) => Container.Items.Length > index ? State.ItemImages.GetValueOrDefault(Container.Items[index].Id) : null;
    /// <summary>Displays an actual native container with its coordinates and retained original items.</summary>
    private Ui Body => Button(Column([
        Text(SeedRegionText.Name(State, Container.Id)).FontSize(12f).Wrap(),
        Text(SeedRegionText.Position(Container)).FontSize(10f).Foreground(LauncherTheme.Muted).FlowDirection(FlowDirection.LeftToRight),
        Row([new SeedItemImage(PreviewImage(0), 20f), new SeedItemImage(PreviewImage(1), 20f), new SeedItemImage(PreviewImage(2), 20f), new SeedItemImage(PreviewImage(3), 20f)]).Spacing(4f).Visible(Container.Items.Length > 0),
        Text(Resources.SeedDynamicLoot).FontSize(10f).Foreground(LauncherTheme.Muted).Wrap().Visible(Container.State == "dynamic_reward"),
    ]).Spacing(5f), Open).Style(SeedChrome.ListRow).MinHeight(60f)
        .Automation(new() { Name = SeedRegionText.Name(State, Container.Id) + " · " + SeedRegionText.Position(Container), AutomationId = $"seed.{ScopeName}.container.{Container.X}.{Container.Y}.{Container.Z}" });
}

internal sealed partial class SeedMissingModelsPopup(SeedExplorerState state) : Control
{
    private Ui Body => ScrollView(new SeedMissingModelsList(state)).Padding(12f).Width(254f).MaxHeight(264f);
}
internal sealed partial class SeedMissingModelsList(SeedExplorerState state) : Control
{
    private string[] Ids => state.Current.Scene?.UnavailableModels.ToArray() ?? [];
    private Ui Body => Column([ItemsRepeater(Ids, id => Array.IndexOf(Ids, id), id => new SeedMissingModel(id))]).Spacing(6f);
}

[View]
internal readonly partial record struct SeedMissingModel(string Id)
{
    /// <summary>Identifies one original block model omitted from this preview.</summary>
    public Ui Body => Text(Id).FontSize(11f).Wrap();
}

[View]
internal readonly partial record struct SeedItemImage(ImageSource? Source, float Size)
{
    /// <summary>Displays an original item resource without substituting generated artwork.</summary>
    public Ui Body => Image(Source ?? Features.Instances.InstanceIconView.Transparent).Width(Size).Height(Size).Stretch(ImageStretch.Uniform).Interpolation(ImageInterpolationMode.NearestNeighbor);
}

[View]
internal readonly partial record struct SeedSceneContainersHost(SeedExplorerState State)
{
    /// <summary>Mounts the actual filtered container list.</summary>
    public Ui Body => new SeedSceneContainers(State);
}
[View]
internal readonly partial record struct SeedInventoryHost(SeedExplorerState State, SeedContainer Container, bool Region)
{
    /// <summary>Mounts a complete frozen container inventory only when selected.</summary>
    public Ui Body => new SeedInventoryView(State, Container, Region);
}

internal sealed partial class SeedSceneOptions(SeedExplorerState state, Action close) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private Ui Body => Column([
        new SeedCommand(LauncherAppearance.Capture(),Resources.SkinResetView, () => { state.Camera = new(); close(); }, "seed.scene.reset"),
        new SeedCommand(LauncherAppearance.Capture(),Resources.SeedAllHeights, () => { if (Current.SceneData is { } d) state.Controller.Regions.Slice(d.Bounds.MinY, d.Bounds.MaxY); close(); }, "seed.scene.layers.all", Current.SceneData is not null),
        Text(Resources.SeedPreviewHelp).FontSize(11f).Foreground(LauncherTheme.Muted).Wrap(),
        Text(SeedRegionText.Policy(Current, Current.SceneData)).FontSize(10f).Foreground(LauncherTheme.Muted).Wrap(),
        Text(Resources.SeedFluidsHidden).FontSize(10f).Foreground(LauncherTheme.Muted).Wrap(),
        Text(Resources.SeedSceneCrop).FontSize(10f).Foreground(LauncherTheme.Muted).Wrap().Visible(Current.SceneCropped),
    ]).Spacing(8f).Width(230f);
}

/// <summary>Scopes camera-mode updates to the view selector instead of rebuilding the scene inspector.</summary>
internal sealed partial class SeedSceneViewMode(SeedExplorerState state) : Control
{
    private Ui Body => new SeedSegments(LauncherAppearance.Capture(), [Resources.SeedView3D, Resources.SeedViewTop], state.Camera.Plan ? 1 : 0,
        index => state.Camera = state.Camera with { Plan = index == 1 }, "seed.scene.view", 120f);
}