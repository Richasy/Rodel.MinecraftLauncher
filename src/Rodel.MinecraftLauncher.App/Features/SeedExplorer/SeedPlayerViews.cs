// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using System.Globalization;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Presents the observation's source and coordinate without confusing a saved snapshot with live data.</summary>
internal static class SeedPlayerText
{
    internal static string Status(SeedExplorerSnapshot state) => state.Player?.State switch
    {
        "live" => Resources.SeedLivePosition,
        "disconnected" => Resources.SeedDisconnectedPosition,
        "disabled" => Resources.SeedBridgeDisabled,
        "other_world" => Resources.SeedPlayerOtherWorld,
        _ => state.Player?.Player is { } player ? string.Format(Resources.SeedSavedPosition, player.SavedAt.ToLocalTime().ToString("g")) : Resources.SeedPlayerUnavailable,
    };
    internal static string Coordinates(SeedExplorerSnapshot state) => state.Player?.Player is not { } player ? string.Empty
        : string.Format(Resources.SeedPlayerCoordinatesFormat, player.X.ToString("0.##", CultureInfo.InvariantCulture), player.Y.ToString("0.##", CultureInfo.InvariantCulture), player.Z.ToString("0.##", CultureInfo.InvariantCulture));
}

internal sealed partial class SeedPlayerCard(SeedExplorerState state) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private Ui Body => Border(Overlay([
        new LauncherMaterialSurface(LauncherMaterials.Acrylic),
        Row([
            LauncherControls.Command(Row([
                LauncherIcons.Line("player", 22f),
                Column([
                    Text(SeedPlayerText.Status(Current)).FontSize(12f),
                    Text(Current.Player?.Player?.Dimension == Current.World.Dimension ? SeedPlayerText.Coordinates(Current)
                        : SeedText.Dimension(Current.Player?.Player?.Dimension ?? "overworld")).FontSize(10f).Foreground(LauncherTheme.Muted).FlowDirection(FlowDirection.LeftToRight),
                ]).Spacing(3f),
            ]).Spacing(10f), state.Controller.Worlds.LocatePlayer).Style(SeedChrome.Quiet.Padding(6f))
                .Automation(new() { Name = Resources.SeedPlayerLocation, AutomationId = "seed.player.card" }),
            new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedFollowPlayer, "player-follow", () => state.Controller.Worlds.Follow(!Current.FollowPlayer), "seed.player.follow",
                Current.Player?.State == "live", Surface: Current.FollowPlayer ? SeedChrome.Inventory(true) : SeedChrome.MapTool),
        ]).Spacing(8f).Padding(5f),
    ])).BorderBrush(LauncherTheme.Edge).BorderThickness(1f).HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Bottom).Margin(12f);
}

internal sealed partial class SeedPlayerPin(SeedExplorerState state) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private static Color Fill => LauncherTheme.HighContrast ? LauncherTheme.HC.SystemColorWindowColor : LauncherTheme.Light ? Colors.White : Colors.Black;
    private float Left => (float)((((Current.Player?.Player?.X ?? 0) - Current.CenterX) / Current.BlocksPerPixel) + (Current.MapWidth / 2)) - 16f;
    private float Top => (float)((((Current.Player?.Player?.Z ?? 0) - Current.CenterZ) / Current.BlocksPerPixel) + (Current.MapHeight / 2)) - 16f;
    private Ui Body => Canvas([
        Transform(Overlay([
            new LauncherLineIcon(LauncherIcons.WorldPlayerGeometry, 28f, true, Fill)
                .Automation(new() { Name = SeedPlayerText.Coordinates(Current), AutomationId = "seed.player.pin" }),
            new LauncherLineIcon(LauncherIcons.WorldPlayerGeometry, 28f, false, LauncherTheme.ProgressAccent),
        ]))
            .Rotation((Current.Player?.Player?.Yaw ?? 0) + 180f).Width(32f).Height(32f).At(Left, Top),
    ]).Width(Current.MapWidth).Height(Current.MapHeight);
}

internal sealed partial class SeedPlayerPopup(SeedExplorerState state) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private partial bool Saving { get; set; }
    private partial string? PendingInstance { get; set; }
    private bool Pending => PendingInstance == Current.WorldInstanceId;
    private Core.Worlds.Maps.SavedWorldPlayer[] Players => Current.WorldSnapshot?.Players ?? [];
    private Ui Body => Column([
        Text(Resources.SeedPlayerLocation).FontSize(16f),
        new SeedChoice(LauncherAppearance.Capture(),Resources.SeedPlayer, [Resources.SeedPlayer, .. Players.Select(player => player.Name ?? player.Id)],
            Array.FindIndex(Players, player => player.Id == Current.PlayerId) + 1,
            index => state.Controller.Worlds.SelectPlayer(index > 0 && index <= Players.Length ? Players[index - 1].Id : null),
            "seed.player.selection").Visible(Players.Length > 1),
        new LauncherCheckBox(LauncherAppearance.Capture(),Resources.SeedPlayerTracking, Current.TrackPlayer, () => state.Controller.Worlds.SetTracking(!Current.TrackPlayer), true, "seed.player.visible", FontSize: 12f),
        new LauncherCheckBox(LauncherAppearance.Capture(),Resources.SeedFollowPlayer, Current.FollowPlayer, () => state.Controller.Worlds.Follow(!Current.FollowPlayer), Current.Player?.State == "live", "seed.player.follow-choice", FontSize: 12f),
        Rectangle(LauncherTheme.Edge).Height(1f),
        Text(SeedPlayerText.Status(Current)).FontSize(12f).Wrap(),
        Text(SeedPlayerText.Coordinates(Current)).FontSize(12f).FlowDirection(FlowDirection.LeftToRight).Visible(Current.Player?.Player is not null),
        Text(SeedText.Dimension(Current.Player?.Player?.Dimension ?? "overworld")).FontSize(12f).Foreground(LauncherTheme.Muted).Visible(Current.Player?.Player is not null),
        new SeedCommand(LauncherAppearance.Capture(),Resources.SeedPlayerLocation, state.Controller.Worlds.LocatePlayer, "seed.player.locate", Current.Player?.Player is not null),
        Text(Pending ? Resources.SeedBridgePending : Resources.SeedEnableBridgeDescription).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap()
            .Visible(Current.Player?.State == "disabled" || Pending),
        new SeedCommand(LauncherAppearance.Capture(),Resources.SeedEnableBridge, () => _ = EnableBridgeAsync(), "seed.player.enable-bridge", !Saving && !Pending)
            .Visible(Current.Player?.State == "disabled"),
    ]).Spacing(12f).Width(280f);

    private async Task EnableBridgeAsync()
    {
        if (Current.WorldInstanceId is not { } instance || Saving) return;
        Saving = true;
        try
        {
            await state.Controller.Client.Settings.SetAsync("gameBridgeEnabled", System.Text.Json.Nodes.JsonValue.Create(true), state.Controller.Client.Instances.Get(instance), state.Controller.Lifetime.Token);
            PendingInstance = instance;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { state.Controller.Error(error); }
        finally { Saving = false; }
    }
}