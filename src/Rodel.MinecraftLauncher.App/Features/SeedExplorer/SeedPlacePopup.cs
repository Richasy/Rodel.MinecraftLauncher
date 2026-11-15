// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

internal sealed partial class SeedPlacePopup(SeedExplorerState state, SeedPosition position, Action close) : Control
{
    private readonly Sprout.Controls.ScrollView _scroll = new(new SeedPlaceContent(state, position, close));
    private Ui Body => Column([_scroll.Padding(12f).Width(Math.Min(304f, state.Current.MapWidth - 32f)).MaxHeight(Math.Max(164f, state.Current.MapHeight - 32f))]);
}

internal sealed partial class SeedPlaceContent(SeedExplorerState state, SeedPosition position, Action close) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private SeedDimension Dimension => Current.Catalog!.Dimensions.First(item => item.Id == Current.World.Dimension);
    private string Command => SeedCommands.Teleport(Current.World, position.X, Current.PendingTeleportY, position.Z);
    private string Source => state.Controller.Worlds.IsPreview(position) ? Resources.SeedWorldPreviewStatus : Resources.SeedSavedMap;
    private Ui Body => Column([
        Grid([GridLength.Auto, GridLength.Star(), GridLength.Auto], [GridLength.Auto], [
            new SeedVisual(SeedAppearance.For(position), SeedAppearance.Symbols || position.Kind is "origin" or "spawn", 34f).Cell(0, 0),
            Column([Text(SeedText.Position(position, Current)).FontSize(15f).Wrap(), Text(position.Id).FontSize(10f).Foreground(LauncherTheme.Muted).Wrap()
                .Visible(position.Kind is not ("origin" or "spawn"))]).Spacing(3f).Cell(1, 0),
            new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedClose, "close", close, "seed.place.close").Cell(2, 0),
        ]).ColumnSpacing(8f),
        Text(SeedText.Coordinates(position)).FontSize(13f).FlowDirection(FlowDirection.LeftToRight).Automation(new() { Name = SeedText.Coordinates(position), AutomationId = "seed.place.coordinates" }),
        Text(Source).FontSize(11f).Foreground(LauncherTheme.Muted).Wrap()
            .Visible(Current.UseSavedMap).Automation(new() { Name = Source, AutomationId = "seed.place.source" }),
        Row([Text(Resources.SeedTeleportY).FontSize(12f).Weight(1f).VAlign(VerticalAlignment.Center),
            new SeedNumber(Resources.SeedTeleportY, Current.PendingTeleportY, Dimension.MinY, Dimension.MinY + Dimension.Height - 1,
                value => state.Current = state.Current with { PendingTeleportY = value }, "seed.place.y").Width(110f)]).Spacing(8f),
        Text(Command).FontSize(11f).Foreground(LauncherTheme.Muted).Wrap().FlowDirection(FlowDirection.LeftToRight).Automation(new() { Name = Command, AutomationId = "seed.place.command" }),
        new SeedCommand(LauncherAppearance.Capture(),Resources.SeedCopyTeleport, () => state.Controller.Copy(Command), "seed.place.teleport", Primary: true),
        new SeedCommand(LauncherAppearance.Capture(),Resources.SeedPreviewStructure, () => { close(); _ = state.Controller.Regions.OpenSceneAsync(position); }, "seed.place.preview", !Current.Opening)
            .Visible(position.Kind is "structure" or "container"),
        Grid([GridLength.Star(), GridLength.Star()], [GridLength.Auto], [
            new SeedCommand(LauncherAppearance.Capture(),Resources.SeedCopyCoordinates, () => state.Controller.Copy(SeedText.Coordinates(position)), "seed.place.copy").Cell(0, 0),
            new SeedCommand(LauncherAppearance.Capture(),state.Controller.Bookmarks.Saved(position) ? Resources.SeedUnsavePlace : Resources.SeedSavePlace,
                () => _ = state.Controller.Bookmarks.ToggleAsync(position, Current.PendingTeleportY), "seed.place.save", !Current.BookmarkBusy).Cell(1, 0),
        ]).ColumnSpacing(8f),
        Text(Resources.SeedTeleportNote).FontSize(10f).Foreground(LauncherTheme.Muted).Wrap(),
    ]).Spacing(10f);
}

internal sealed partial class SeedLocatePopup : Control
{
    private readonly SeedExplorerState state;
    private readonly SeedExplorerPopups popups;
    private readonly Action close;
    internal SeedLocatePopup(SeedExplorerState state, SeedExplorerPopups popups, Action close)
    { this.state = state; this.popups = popups; this.close = close; X = (int)Math.Round(state.Current.CenterX); Z = (int)Math.Round(state.Current.CenterZ); }
    private partial int X { get; set; }
    private partial int Z { get; set; }
    private void Locate()
    {
        close(); state.Controller.Map.Pan(X, Z);
        _ = state.Controller.Map.QueryPointAsync(X, Z, popups.ShowPoint);
    }
    private Ui Body => Column([
        Text(Resources.SeedLocate).FontSize(16f),
        Grid([GridLength.Star(), GridLength.Star()], [GridLength.Auto, GridLength.Auto], [
            Text("X").FontSize(12f).Cell(0, 0), Text("Z").FontSize(12f).Cell(1, 0),
            new SeedNumber("X", X, -29_999_999, 29_999_999, value => X = value, "seed.locate.x").Cell(0, 1),
            new SeedNumber("Z", Z, -29_999_999, 29_999_999, value => Z = value, "seed.locate.z").Cell(1, 1),
        ]).ColumnSpacing(8f).RowSpacing(6f),
        new SeedCommand(LauncherAppearance.Capture(),Resources.SeedLocate, () => Locate(), "seed.locate.go", Primary: true),
    ]).Spacing(12f).Width(280f);
}