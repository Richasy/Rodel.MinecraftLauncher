// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Instances;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Multiplayer;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Multiplayer;

/// <summary>Displays a compact room with centered text, a larger save thumbnail and separated edge actions.</summary>
internal sealed partial class MultiplayerMiniView(MultiplayerController controller, Action close, Action restore, Action members) : Control
{
    private partial int Revision { get; set; }
    private MultiplayerRoom? Room { get { _ = Revision; return controller.Room; } }
    private string Count => MultiplayerPresentation.OnlineCount(Room) is { } count ? string.Format(Resources.MultiplayerMemberCount, count) : Resources.MultiplayerUnknownCount;
    private string CopyLabel => Room?.Path == "fallback" ? Resources.MultiplayerCopyAddress : Resources.MultiplayerCopy;
    internal void Refresh() => Revision++;

    private Ui Body => Overlay([
        new LauncherMaterialSurface(LauncherMaterials.Acrylic),
        new InstanceIconView(controller.WorldIconData, 48f, 48f).HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Center).Margin(new Thickness(8f, 0f, 0f, 0f)),
        Grid([GridLength.Pixels(48f), GridLength.Star(), GridLength.Pixels(84f)], [GridLength.Pixels(20f), GridLength.Pixels(16f)], [
            Text(Room?.Name ?? string.Empty).FontSize(14f).FontWeight(600).Foreground(LauncherTheme.Ink)
                .Height(20f).VAlign(VerticalAlignment.Top).Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                .Automation(new() { Name = Room?.Name ?? string.Empty, AutomationId = "multiplayer.mini.name" }).Cell(1, 0),
            new MultiplayerRoomStatus(LauncherAppearance.Capture(),MultiplayerPresentation.Status(Room), MultiplayerPresentation.Ready(Room))
                .Height(16f).VAlign(VerticalAlignment.Top).Cell(1, 1),
        ]).ColumnSpacing(12f).RowSpacing(4f).VAlign(VerticalAlignment.Center).Margin(new Thickness(8f, 0f, 0f, 0f)),
        Row([
            new MultiplayerMiniAction(LauncherAppearance.Capture(),"copy", CopyLabel, () => Sprout.Host.Clipboard.SetText(Room?.Invitation ?? Room?.Address ?? string.Empty), MultiplayerPresentation.Ready(Room)),
            new MultiplayerMiniAction(LauncherAppearance.Capture(),"expand", string.Format(Resources.MultiplayerReturnRoom, Room?.Name ?? string.Empty), restore),
            new MultiplayerMiniAction(LauncherAppearance.Capture(),"close", Resources.MultiplayerMiniClose, close),
        ]).HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Top),
        LauncherControls.OutlineCommand(Row([
            LauncherIcons.Line("person", 14f).VAlign(VerticalAlignment.Top),
            Text(Count).FontSize(12f).Height(16f).VAlign(VerticalAlignment.Top),
            LauncherIcons.Line("chevron-down", 10f).VAlign(VerticalAlignment.Top),
        ]).Spacing(4f).HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Top), members)
            .Style(LauncherButtons.FlatIconButton.Padding(new Thickness(0f, 8f, 8f, 8f)).ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Top)).Width(84f)
            .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Bottom)
            .Enabled(MultiplayerPresentation.OnlineCount(Room) is not null)
            .Automation(new() { Name = Resources.MultiplayerMembers, AutomationId = "multiplayer.mini.members" }),
    ]).Width(320f).Height(64f);

}

/// <summary>Provides a compact edge hit target with a consistent visual inset.</summary>
/// <param name="Icon">The shared launcher glyph key.</param>
/// <param name="Name">The localized action name and icon-purpose tooltip.</param>
/// <param name="Click">The UI-only action.</param>
/// <param name="Enabled">Whether current room evidence permits the action.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct MultiplayerMiniAction(LauncherAppearance Theme, string Icon, string Name, Action Click, bool Enabled = true)
{
    /// <summary>Keeps the full edge hit area separate from its eight-DIP visual inset.</summary>
    public Ui Body => LauncherControls.OutlineCommand(Theme, Row([
        LauncherIcons.Line(Icon, 14f).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Top),
    ]).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Top).Margin(new Thickness(0f, 0f, 2f, 0f)), Click)
        .Style(Theme.Buttons.FlatIconButton.Padding(new Thickness(0f, 8f, 0f, 10f)).ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Top)).Width(28f).Enabled(Enabled).ToolTip(Name)
        .Automation(new() { Name = Name, AutomationId = "multiplayer.mini." + Icon });
}

/// <summary>Supplies a proportionate neutral icon for the waiting-members empty state.</summary>
/// <param name="Size">The artwork size selected for the available room height.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct MultiplayerWaitingArtwork(LauncherAppearance Theme, float Size = 40f)
{
    /// <summary>Uses the same line style as participant rows, without replacing the world thumbnail.</summary>
    public Ui Body => LauncherIcons.Line("multiplayer", Size, Theme.Quiet);
}

/// <summary>Maintains a keyed, bounded scrolling member flyout using the room's actual participant snapshots.</summary>
internal sealed partial class MultiplayerMiniMembers : Control
{
    private readonly MultiplayerController _controller;
    private readonly float _height;
    private partial int Revision { get; set; }
    private MultiplayerMember[] Members { get { _ = Revision; return _controller.Room?.Members ?? []; } }
    private (MultiplayerMember Member, bool Divider)[] MemberRows => [.. Members.Select((member, index) => (member, index > 0))];
    private string Count => MultiplayerPresentation.OnlineCount(_controller.Room) is { } count ? string.Format(Resources.MultiplayerMemberCount, count) : Resources.MultiplayerUnknownCount;

    internal MultiplayerMiniMembers(MultiplayerController controller, float height)
    { _controller = controller; _height = height; controller.Changed += OnChanged; }
    private void OnChanged() => Revision++;
    private Ui Body => Column([
        Row([
            Text(Resources.MultiplayerMembers).FontSize(14f).FontWeight(600).Foreground(LauncherTheme.Ink).Weight(1f),
            Text(Count).FontSize(12f).Foreground(LauncherTheme.Muted),
        ]).Padding(16f, 12f),
        Rectangle(LauncherTheme.Edge).Height(1f),
        ScrollView(ItemsRepeater(MemberRows, static row => row.Member.Id, static row => new MultiplayerMiniMember(LauncherAppearance.Capture(),row.Member, row.Divider), new StackLayout { EstimatedItemExtent = 52f }))
            .MaxHeight(_height - 45f).Automation(new() { Name = Resources.MultiplayerMembers, AutomationId = "multiplayer.mini.members.scroll" }),
    ]).Width(318f);
    protected override void OnDispose() { _controller.Changed -= OnChanged; base.OnDispose(); }
}

/// <summary>Aligns the neutral avatar, player name and optional host role at their common top edge.</summary>
/// <param name="Member">One network participant, without an inferred game skin.</param>
/// <param name="Divider">Whether another participant precedes this row.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct MultiplayerMiniMember(LauncherAppearance Theme, MultiplayerMember Member, bool Divider)
{
    /// <summary>Shows a stable participant identity in the room flyout.</summary>
    public Ui Body => Border(Grid([GridLength.Pixels(20f), GridLength.Star(), GridLength.Auto], [GridLength.Auto], [
        LauncherIcons.Line("person", 20f).VAlign(VerticalAlignment.Top).Cell(0, 0),
        Text(Member.Name).FontSize(13f).Height(20f).Foreground(Theme.Ink).VAlign(VerticalAlignment.Top)
            .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
            .Automation(new() { Name = Member.Name, AutomationId = "multiplayer.mini.member." + Member.Id }).Cell(1, 0),
        Text(Resources.MultiplayerHost).FontSize(11f).Height(20f).Foreground(Theme.Muted)
            .Visible(Member.Role is "host" or "local").VAlign(VerticalAlignment.Top).Cell(2, 0),
    ]).ColumnSpacing(12f)).Padding(16f, 16f).BorderBrush(Theme.Edge).BorderThickness(0f, Divider ? 1f : 0f, 0f, 0f);
}