// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Popups;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Owns anchored light-dismiss surfaces above the map without altering the underlying map geometry.</summary>
internal sealed class SeedExplorerPopups
{
    private readonly SeedExplorerState _state;
    private SeedPosition _position = new();
    private long _revision;
    internal Func<Rect> MapBounds { get; set; } = () => default;
    internal Flyout Bookmarks { get; }
    internal Flyout Layers { get; }
    internal Flyout Legend { get; }
    internal Flyout Locate { get; }
    internal Flyout History { get; }
    internal Flyout Structures { get; }
    internal Flyout Region { get; }
    internal Flyout WorldOptions { get; }
    internal Flyout Player { get; }
    private Flyout Place { get; }
    internal SeedExplorerPopups(SeedExplorerState state)
    {
        _state = state;
        Bookmarks = Create("seed.bookmarks", close => new SeedBookmarksPopup(state, close), 0f);
        Layers = Create("seed.layers", _ => new SeedLegendPopup(state, layers: true, showLegend: ShowLegend), 0f);
        Legend = Create("seed.legend", _ => new SeedLegendPopup(state, layers: false), 0f);
        Locate = Create("seed.locate", close => new SeedLocatePopup(state, this, close));
        History = Create("seed.history", close => new SeedHistoryPopup(state, close), 0f);
        Structures = Create("seed.structures", _ => new SeedStructuresPopup(state), 0f);
        Region = Create("seed.area", _ => new SeedRegionPopup(state), 0f);
        WorldOptions = Create("seed.world-options", _ => new SeedWorldOptionsPopup(state));
        Player = Create("seed.player.options", _ => new SeedPlayerPopup(state));
        WorldOptions.Opening += args => { if (state.Current.World.Preset == "single_biome") _ = state.Controller.Map.EnsureCatalogAsync(); };
        foreach (var flyout in new[] { Structures, Region, Layers, Locate }) flyout.Placement = PopupPlacement.BottomEdgeAlignedLeft;
        Place = Create("seed.place", close => new SeedPlacePopup(state, _position, close), 0f);
        Place.Closed += () => { if (!state.Controller.Disposed) state.Current = state.Current with { SelectedPlace = null, PickedPoint = null }; };
        state.Controller.DismissPopups = HideAll;
    }
    private Flyout Create(string id, Func<Action, Control> create, float padding = 12f)
    {
        var flyout = new Flyout(create)
        {
            Placement = PopupPlacement.BottomEdgeAlignedRight,
            ShouldConstrainToRootBounds = true,
            ShowMode = FlyoutShowMode.Standard,
            ChromeResolver = theme => LauncherMenus.ResolvePopupChrome(theme, padding),
        };
        flyout.Opening += _ =>
        {
            _revision++;
            if (!_state.Controller.Disposed) _state.Controller.Map.CancelPoint();
        };
        flyout.Automation(new() { AutomationId = id });
        return flyout;
    }
    internal void HideAll()
    {
        _revision++;
        if (!_state.Controller.Disposed) _state.Controller.Map.CancelPoint();
        foreach (var popup in new[] { Bookmarks, Layers, Legend, Locate, History, Structures, Region, WorldOptions, Player, Place }) popup.Hide();
    }
    internal void ShowRegion()
    {
        HideAll();
        var bounds = MapBounds();
        Region.ShowAt(_state.Controller.Window, new Point(bounds.Right - 12, bounds.Top + 4));
    }
    private void ShowLegend()
    {
        HideAll();
        var bounds = MapBounds();
        Legend.ShowAt(_state.Controller.Window, new Point(bounds.Right - 12, bounds.Top + 4));
    }
    internal void ShowPosition(SeedPosition position) => ShowPosition(position, null);
    internal void ShowPoint(SeedPosition position) => ShowPosition(position, null, true);
    internal void ShowPosition(SeedPosition position, int? teleportY, bool point = false)
    {
        _position = position;
        var dimension = _state.Current.Catalog?.Dimensions.First(item => item.Id == _state.Current.World.Dimension);
        var y = teleportY ?? (position.Kind == "biome" && _state.Current.SliceY is null ? position.Y + 1 : position.Y);
        _state.Current = _state.Current with { PendingTeleportY = Math.Clamp(y, dimension?.MinY ?? -64, (dimension?.MinY ?? -64) + (dimension?.Height ?? 384) - 1) };
        ShowAtPosition(position, point);
    }
    private void ShowAtPosition(SeedPosition position, bool point)
    {
        HideAll();
        var state = _state.Current;
        var x = ((position.X - state.CenterX) / state.BlocksPerPixel) + (state.MapWidth / 2);
        var z = ((position.Z - state.CenterZ) / state.BlocksPerPixel) + (state.MapHeight / 2);
        var right = x < state.MapWidth / 2;
        // Native popup constraints fit the surface while its anchor remains at the real place.
        Place.Placement = right ? PopupPlacement.Right : PopupPlacement.Left;
        _state.Current = _state.Current with
        {
            SelectedPlace = point ? null : position,
            FocusedPlace = point ? _state.Current.FocusedPlace : position,
            PickedPoint = point ? position : null,
        };
        var revision = _revision;
        _state.Controller.Dispatcher.TryEnqueue(() =>
        {
            if (_state.Controller.Disposed || revision != _revision) return;
            var bounds = MapBounds();
            Place.ShowAt(_state.Controller.Window, new Point(bounds.Left + (float)x, bounds.Top + (float)z));
        });
    }
}