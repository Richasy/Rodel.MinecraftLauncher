// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reactive;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Composes one retained map, editable search pane and native popup surfaces without resizing the map on pin selection.</summary>
internal sealed partial class SeedExplorerView : Control
{
    private readonly SeedExplorerState _state;
    private readonly SeedExplorerMapView _map;
    private readonly SeedScenePage _scene;
    internal SeedExplorerView(SeedExplorerState state) { _state = state; _map = new(state); _scene = new(state); }
    internal SeedExplorerPopups Popups => _map.Popups;
    private Ui Body => Overlay([_map.Visible(!_state.Current.SceneOpen), _scene.Visible(_state.Current.SceneOpen)]);
}

internal sealed partial class SeedExplorerMapView : Control
{
    private readonly SeedExplorerState _state;
    private readonly SeedMapSurface _map;
    private readonly SeedSearchPanel _search;
    internal SeedExplorerPopups Popups { get; }
    private bool _layoutPending;
    internal SeedExplorerMapView(SeedExplorerState state)
    {
        _state = state; Popups = new(state); _map = new(state, Popups); _search = new(state, Popups);
        Popups.MapBounds = () => _map.Bounds;
    }
    private SeedExplorerSnapshot Current => _state.Current;
    private bool SavedSource => Current.WorldSource && !Current.SearchMode && Current.SavedMap;
    private string LoadingTitle => SavedSource ? Resources.SeedReadingWorld : Resources.SeedLoadingMap;
    private int ConfirmedStructures => SeedMapMarkers.Positions(Current).Count(position => position.Kind == "structure"
        && Math.Abs(position.X - Current.CenterX) <= Current.MapWidth * Current.BlocksPerPixel / 2
        && Math.Abs(position.Z - Current.CenterZ) <= Current.MapHeight * Current.BlocksPerPixel / 2);
    private string MarkerStatus => Current.WorldSource && !Current.SearchMode
        ? !Current.WorldLoaded ? Resources.SeedReadWorld : Current.WorldNeedsRefresh ? Resources.SeedWorldChanged
            : Current.SavedMap ? Current.WorldPreviewPreparing ? Resources.SeedWorldPreviewPreparing
                : Current.WorldPreviewUnavailable ? Resources.SeedWorldPreviewUnavailable
                : Current.Tiles.Any(tile => tile.Tile.Source == "mixed") ? Resources.SeedWorldHybridStatus
                : string.Format(Resources.SeedWorldSnapshotStatus, Current.WorldSnapshot?.SavedAt.ToLocalTime().ToString("g")) : Resources.SeedWorldPreviewStatus
        : SeedMapPlanning.Overview(SeedMapTiles.View(Current)) ? Resources.SeedMarkerOverview
        : Current.MarkerLimited ? string.Format(Resources.SeedMarkerPartialFormat, ConfirmedStructures, Current.MarkerRegionsChecked, Current.MarkerRegionCount)
        : Current.MarkerBusy || Current.MarkerRegionsChecked < Current.MarkerRegionCount
            ? string.Format(Resources.SeedMarkerLoadingFormat, ConfirmedStructures, Current.MarkerRegionsChecked, Current.MarkerRegionCount)
            : string.Format(Resources.SeedMarkerConfirmedFormat, ConfirmedStructures);
    private string Status => SavedSource && Current.WorldReading ? Resources.SeedReadingWorld : Current.Preparing ? Resources.SeedPreparing : Current.Opening ? Resources.SeedOpening : Current.MapBusy
        ? string.Format(Resources.SeedLoadingTilesFormat, Current.RemainingTiles) : Current.Phase switch
        {
            "copied" => Resources.SeedCopied,
            "copy_failed" => Resources.SeedCopyFailed,
            "exported" => Resources.SeedExported,
            _ => Resources.SeedReady,
        };
    private Ui Body => Border(Grid([GridLength.Star()], [GridLength.Auto, GridLength.Star()], [
        Column([
            new SeedWorldToolbar(_state).Visible(Current.WorldSource && !Current.SearchMode),
            new SeedExplorerToolbar(_state, Popups).Visible(!Current.WorldSource || Current.SearchMode),
        ]).Cell(0, 0),
        Grid([GridLength.Pixels(Current.SearchMode ? Current.Width < 1000 ? 284 : 320 : 0), GridLength.Star()], [GridLength.Star()], [
            _search.Visible(Current.SearchMode).Cell(0, 0),
            Grid([GridLength.Star()], [GridLength.Star(), GridLength.Auto], [
                Flow(FlowDirection.LeftToRight, Overlay([
                    _map,
                    new SeedMapScale(_state).OffThread().Visible(Current.HasMap).Automation(new() { AutomationId = "seed.scale" }),
                    new SeedMapCommands(_state, Popups).OffThread().Visible(Current.HasMap),
                    new SeedWorldMapView(_state).Visible(Current.HasMap && Current.WorldSource && !Current.SearchMode && Current.Width < 1100f),
                    Border(Row([
                        Text(Resources.SeedSelectionHint).FontSize(12f).Wrap(),
                        new SeedCommand(LauncherAppearance.Capture(),Resources.SeedCancelSelection, _state.Controller.Regions.CancelSelection, "seed.area.cancel-selection"),
                    ]).Spacing(12f)).Padding(12f).Background(LauncherMaterials.Acrylic).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Bottom).Margin(12f).Visible(Current.SelectingRegion),
                    Border(Column([
                        new EmptyPlaceholderView("world-origin", Resources.SeedWelcomeTitle, Resources.SeedWelcomeDescription,
                            LauncherTheme.Ink, LauncherTheme.Muted, "seed.welcome"),
                        new SeedCommand(LauncherAppearance.Capture(),Resources.SeedSearch, () => _state.Controller.SelectMode(true), "seed.welcome.search", !Current.Opening)
                            .HAlign(HorizontalAlignment.Center).Visible(!Current.SearchMode),
                    ]).Spacing(20f)).MaxWidth(390f).Margin(24f).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
                        .Visible(!Current.WorldSource && !Current.HasMap && Current.Profile is not null && !Current.LoadingMap),
                    new SeedLoadingView(LauncherAppearance.Capture(),LoadingTitle, SavedSource ? Resources.SeedWorldHybridPolicy : Current.Phase == "spawn" ? Resources.SeedFindingSpawn
                        : Current.Phase == "map" ? Current.SliceY is null ? Resources.SeedStageTerrain : Resources.SeedBiome : Resources.SeedOpening,
                        Current.LoadingMap, "seed.map.loading").HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center).Margin(16f).Visible(Current.LoadingMap),
                    ProgressBar().Indeterminate(false).Value(Current.TotalTiles > 0 ? Math.Clamp((Current.TotalTiles - Current.RemainingTiles) * 100d / Current.TotalTiles, 0, 100) : 0)
                        .Thickness(3f).Foreground(LauncherTheme.ProgressAccent).VAlign(VerticalAlignment.Top).Visible(Current.MapBusy && !Current.LoadingMap)
                        .Automation(new() { Name = string.Format(Resources.SeedStageFormat, LoadingTitle, Current.TotalTiles - Current.RemainingTiles, Current.TotalTiles), AutomationId = "seed.map.progress" }),
                    new SeedMapZoomCommands(_state).OffThread().Visible(Current.HasMap),
                    new SeedPlayerCard(_state).Visible(Current.HasMap && Current.WorldSource && !Current.SearchMode && Current.TrackPlayer && Current.Player?.Player is not null),
                    new SeedWorldEmpty(_state).Visible(Current.WorldSource && !Current.SearchMode && !Current.WorldLoaded && !Current.LoadingMap),
                    Border(Column([
                        Text(Current.Preparing ? Resources.SeedPreparing : Current.Opening ? Resources.SeedOpening : Resources.SeedNoRuntime).FontSize(15f).Align(TextAlignment.Center).Wrap(),
                        Text(Resources.SeedOriginalNote).FontSize(12f).Foreground(LauncherTheme.Muted).Align(TextAlignment.Center).Wrap(),
                        new SeedCommand(LauncherAppearance.Capture(),Resources.SeedWorldSource, () => _ = _state.Controller.Worlds.SelectSourceAsync(true), "seed.prepare", !Current.Preparing, true)
                            .Visible(!Current.Opening && !Current.HasMap),
                    ]).Spacing(12f)).Padding(24f).MaxWidth(390f).Background(LauncherMaterials.Acrylic).BorderBrush(LauncherTheme.Edge).BorderThickness(1f)
                        .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center).Visible(!Current.WorldSource && !Current.HasMap && Current.Profile is null),
                ])).Cell(0, 0),
                new SeedExplorerFooter(_state, Status, MarkerStatus).Visible(Current.HasMap || (Current.WorldSource && Current.WorldSnapshot is not null)).Cell(0, 1),
            ]).Cell(1, 0),
        ]).Cell(0, 1),
    ])).Background(LauncherTheme.Background).BorderBrush(LauncherTheme.Edge).BorderThickness(0, 1f, 0, 0);

    private void Layout()
    {
        if (_layoutPending || _state.Controller.Disposed || Current.SceneOpen) return;
        _layoutPending = true;
        _state.Controller.Dispatcher.TryEnqueue(() =>
        {
            _layoutPending = false;
            if (_state.Controller.Disposed || Current.SceneOpen) return;
            var bounds = _map.Bounds;
            _state.Controller.Map.Resize(bounds.Width, bounds.Height);
            if (Current.PendingPlace is { } position)
            {
                var y = Current.PendingTeleportY;
                _state.Current = Current with { PendingPlace = null };
                Popups.ShowPosition(position, y);
            }
        });
    }
    protected override IDisposable? OnMounted()
    {
        var controller = _state.Controller;
        controller.Window.LayoutUpdated += Layout;
        var scope = controller.Window.Commands.PushScope();
        var gate = KeyScope.Global.InertWhenTyping().InertWhenPopup();
        scope.Bind(new KeyChord(Key.Escape), new Command("seed.selection.escape", controller.Regions.CancelSelection, () => Current.SelectingRegion), gate);
        var language = new Effect(() => _ = controller.Map.RefreshLanguageAsync(LanguageSection.GameLanguage));
        return new SeedViewLifetime(() => { controller.Window.LayoutUpdated -= Layout; scope.Dispose(); language.Dispose(); Popups.HideAll(); });
    }
    private sealed class SeedViewLifetime(Action dispose) : IDisposable
    {
        /// <inheritdoc/>
        public void Dispose() => dispose();
    }
}