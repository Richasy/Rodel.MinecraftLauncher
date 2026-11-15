// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Instances;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Multiplayer;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Multiplayer;

/// <summary>Keeps room identity and its exit action outside both independently scrolling columns.</summary>
internal sealed partial class MultiplayerRoomHeader : Control
{
    private readonly MultiplayerController _controller;
    private readonly MultiplayerInvitationIntro _invitation;
    private readonly Action _create;
    private readonly Action _join;
    private partial int Revision { get; set; }
    private MultiplayerRoom? Room { get { _ = Revision; return _controller.Room; } }
    private InstanceSummary? Instance => _controller.Instances.FirstOrDefault(item => item.Id == Room?.InstanceId);
    private string WorldName => Room?.Role == "host" ? Room.World?.Name ?? string.Empty : string.Empty;
    private bool DistinctWorld => WorldName.Length > 0 && !string.Equals(WorldName, Room?.Name, StringComparison.Ordinal);

    internal MultiplayerRoomHeader(MultiplayerController controller, Action create, Action join)
    {
        _controller = controller;
        _create = create; _join = join;
        _invitation = new(controller);
        controller.Changed += OnChanged;
    }

    private void OnChanged() => Revision++;

    private bool ExitGame => Room is { Active: true } || Room?.State == "exit-pending";
    private bool MultipleEndActions => ExitGame && Room?.GameCloseRequestedAt is null;
    private bool Ended => MultiplayerPresentation.Ended(Room);
    private string EndLabel => Room?.State == "exit-pending" ? Resources.MultiplayerCheckExit : !ExitGame
        ? Room?.Role == "guest" ? Resources.MultiplayerLeaveOnly : Resources.MultiplayerStopOnly
        : Room?.Role == "guest" ? Resources.MultiplayerLeaveExit : Resources.MultiplayerEndExit;
    private Ui Body => Grid([GridLength.Star(), GridLength.Auto], [GridLength.Auto], [
        Row([
            new InstanceIconView(Room?.World is null ? Instance?.IconData : _controller.WorldIconData, 48f, 48f)
                .VAlign(VerticalAlignment.Center)
                .Automation(new() { Name = WorldName.Length > 0 ? WorldName : Room?.Name ?? string.Empty, AutomationId = "multiplayer.world.thumbnail" }),
            Column([
                Text(Room?.Name ?? string.Empty).FontSize(21f).FontWeight(600).Foreground(LauncherTheme.Ink).Wrap()
                    .Automation(new() { Name = Room?.Name ?? string.Empty, AutomationId = "multiplayer.room.name" }),
                Text(WorldName).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap().Visible(DistinctWorld)
                    .Automation(new() { Name = WorldName, AutomationId = "multiplayer.world.name" }),
                Text(Room?.Role == "guest" ? Resources.MultiplayerGuestRole : Resources.MultiplayerHostRole).FontSize(11f).Foreground(LauncherTheme.Muted),
            ]).Spacing(4f).Weight(1f).VAlign(VerticalAlignment.Center),
        ]).Spacing(16f).VAlign(VerticalAlignment.Center).Cell(0, 0),
        Column([
            Row([
                new MultiplayerRoomStatus(LauncherAppearance.Capture(),Room?.State == "retrying" ? string.Format(Resources.MultiplayerRetryCount, Room.RetryCount) : MultiplayerPresentation.Status(Room), MultiplayerPresentation.Ready(Room)).VAlign(VerticalAlignment.Center),
                Row([
                    SplitButton(new LauncherSplitButtonLabel(EndLabel, LauncherStyles.Text.FontSize(13f).Foreground(LauncherTheme.Ink)), () => _ = _controller.EndAsync(ExitGame), EndMenu)
                        .Style(LauncherMenus.SplitCommand).CornerRadius(CornerRadius.Zero)
                        .Padding(new Thickness(12f, 8f, 12f, 8f)).SurfacePainter(LauncherSplitButtonSurface.Paint)
                        .Enabled(!_controller.Busy && Room?.State != "ending").Visible(!Ended && MultipleEndActions).VAlign(VerticalAlignment.Center)
                        .Automation(new() { Name = EndLabel, AutomationId = "multiplayer.stop" }),
                    LauncherControls.OutlineCommand(EndLabel, () => _ = _controller.EndAsync(ExitGame))
                        .Enabled(!_controller.Busy && Room?.State != "ending").Visible(!Ended && !MultipleEndActions).VAlign(VerticalAlignment.Center)
                        .Automation(new() { Name = EndLabel, AutomationId = "multiplayer.stop" }),
                    LauncherControls.OutlineCommand(Resources.MultiplayerCreate, _create).Visible(Ended).Enabled(!_controller.HasOwnedWork)
                        .Automation(new() { AutomationId = "multiplayer.room.create" }),
                    LauncherControls.OutlineCommand(Resources.MultiplayerJoin, _join).Visible(Ended).Enabled(!_controller.HasOwnedWork)
                        .Automation(new() { AutomationId = "multiplayer.room.join" }),
                ]).Spacing(8f).VAlign(VerticalAlignment.Center),
            ]).Spacing(18f).HAlign(HorizontalAlignment.Right),
            _invitation.Visible(MultiplayerPresentation.Ready(Room)),
        ]).Spacing(8f).MinWidth(258f).VAlign(VerticalAlignment.Center).Cell(1, 0),
    ]).ColumnSpacing(18f).Margin(new Thickness(24f, 16f, 24f, 16f));

    private MenuFlyout EndMenu => new MenuFlyout(new MenuFlyoutItem(Room?.Role == "guest" ? Resources.MultiplayerLeaveOnly : Resources.MultiplayerStopOnly, () => _ = _controller.EndAsync(false))
    { IsEnabled = !_controller.Busy && Room?.State != "ending" && Room?.GameCloseRequestedAt is null, Automation = new() { AutomationId = "multiplayer.stop-only" } }).Style(LauncherMenus.CommandMenu);
    protected override void OnDispose() { _controller.Changed -= OnChanged; base.OnDispose(); }
}

/// <summary>Places the native invitation and its copy action together, without a redundant heading.</summary>
internal sealed partial class MultiplayerInvitationIntro(MultiplayerController controller) : Control
{
    private partial int Revision { get; set; }
    private MultiplayerRoom? Room { get { _ = Revision; return controller.Room; } }
    private string? Connection => !MultiplayerPresentation.Ready(Room) ? null : Room?.Role == "guest" || Room?.Path == "fallback" ? Room?.Address : Room?.Invitation;
    private string CopyLabel => Room?.Role == "guest" || Room?.Path == "fallback" ? Resources.MultiplayerCopyAddress : Resources.MultiplayerCopy;
    private bool HasConnection => Connection is not null;
    protected override IDisposable? OnMounted() { controller.Changed += OnChanged; return null; }
    private void OnChanged() => Revision++;
    private Ui Body => Overlay([
        TextBox(Connection ?? string.Empty, _ => { }).ReadOnly().FontSize(13f).Padding(new Thickness(12f, 8f, 44f, 8f))
            .Visible(HasConnection)
            .Automation(new() { Name = Room?.Path == "fallback" || Room?.Role == "guest" ? Resources.MultiplayerConnectionAddress : Resources.MultiplayerCode, AutomationId = "multiplayer.invitation" }),
        new MultiplayerIcon(LauncherAppearance.Capture(),"copy", CopyLabel, () => Sprout.Host.Clipboard.SetText(Connection ?? string.Empty))
            .Visible(HasConnection)
            .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Center).Margin(new Thickness(0f, 0f, 4f, 0f)),
    ]).VAlign(VerticalAlignment.Center);
    protected override void OnDispose() { controller.Changed -= OnChanged; base.OnDispose(); }
}

/// <summary>Centers the empty state in the visible member area while retaining an independent scroll viewport.</summary>
internal sealed partial class MultiplayerRoomColumn : Control
{
    private readonly MultiplayerController _controller;
    private readonly Window _window;
    internal partial float ViewportHeight { get; set; }
    private partial int Revision { get; set; }
    private partial bool Compact { get; set; }
    private MultiplayerRoom? Room { get { _ = Revision; return _controller.Room; } }
    private bool Ready => MultiplayerPresentation.Ready(Room);
    private bool Waiting => MultiplayerPresentation.WaitingForFriends(Room);
    private bool ShowEmpty => !Ready || Waiting || Room?.Members.Length == 0;
    private string EmptyLabel => Waiting ? Resources.MultiplayerNoMembers : Resources.MultiplayerNoMemberInfo;
    private string Count => MultiplayerPresentation.OnlineCount(Room) is { } count ? string.Format(Resources.MultiplayerMemberCount, count) : Resources.MultiplayerUnknownCount;
    private string CopyLabel => Room?.Path == "fallback" ? Resources.MultiplayerCopyAddress : Resources.MultiplayerCopy;
    private MultiplayerMember[] Members => Room?.Role == "host" ? [.. Room.Members.Select(member => member.Role == "local" ? member with { Role = "host" } : member)] : Room?.Members ?? [];
    private (MultiplayerMember Member, bool Divider)[] MemberRows => [.. Members.Select((member, index) => (member, index > 0))];
    internal MultiplayerRoomColumn(MultiplayerController controller, Window window)
    {
        _controller = controller; _window = window;
        Compact = window.ClientSize.Height < 650f;
        controller.Changed += OnChanged; window.SizeChanged += OnSizeChanged;
    }
    private void OnChanged() => Revision++;
    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs e) => Compact = e.Size.Height < 650f;
    private void Copy() => Sprout.Host.Clipboard.SetText(Room?.Invitation ?? Room?.Address ?? string.Empty);
    private Ui Body => Grid([GridLength.Star()], [GridLength.Auto, GridLength.Star()], [
        Row([
            Text(Resources.MultiplayerMembers).FontSize(15f).FontWeight(600).Foreground(LauncherTheme.Ink).Weight(1f).VAlign(VerticalAlignment.Center)
                .Automation(new() { Name = Resources.MultiplayerMembers, AutomationId = "multiplayer.members.heading" }),
            Text(Count).FontSize(12f).Foreground(LauncherTheme.Muted).VAlign(VerticalAlignment.Center),
        ]).Spacing(12f).Margin(new Thickness(24f, 16f, 24f, 12f)).Cell(0, 0),
        Overlay([
            ItemsRepeater(MemberRows, static row => row.Member.Id, static row => new MultiplayerMemberView(LauncherAppearance.Capture(),row.Member, row.Divider), new StackLayout { EstimatedItemExtent = 73f }).Visible(!ShowEmpty),
            Grid([GridLength.Star()], [GridLength.Star(), GridLength.Auto, GridLength.Star()], [
                Column([
                    new MultiplayerWaitingArtwork(LauncherAppearance.Capture(),Compact ? 32f : 48f).HAlign(HorizontalAlignment.Center),
                    Text(EmptyLabel).FontSize(Compact ? 14f : 16f).Foreground(LauncherTheme.Ink)
                        .Wrap().HAlign(HorizontalAlignment.Center).Automation(new() { Name = EmptyLabel, AutomationId = "multiplayer.room.empty" }),
                    LauncherControls.Command(CopyLabel, Copy).Style(LauncherButtons.Accent).HAlign(HorizontalAlignment.Center).Visible(Waiting)
                        .Automation(new() { AutomationId = "multiplayer.room.empty.action" }),
                ]).Spacing(12f).Cell(0, 1),
            ]).Margin(24f).Visible(ShowEmpty),
        ]).Cell(0, 1),
    ]).MinHeight(ViewportHeight);
    protected override void OnDispose() { _controller.Changed -= OnChanged; _window.SizeChanged -= OnSizeChanged; base.OnDispose(); }
}

/// <summary>Keeps instance actions together below an environment heading aligned with the member heading.</summary>
internal sealed partial class MultiplayerShareColumn : Control
{
    private readonly MultiplayerController _controller;
    private readonly Func<bool, Task> _export;
    private readonly Action<string> _content;
    private readonly Action<string> _showDialog;
    private partial int Revision { get; set; }
    private MultiplayerRoom? Room { get { _ = Revision; return _controller.Room; } }
    private InstanceSummary? Instance => _controller.Instances.FirstOrDefault(item => item.Id == Room?.InstanceId);
    private bool Ending => Room?.State is "ending" or "exit-pending";
    private bool NeedsRecovery => Room?.State == "disconnected" || MultiplayerText.Recovery(Room?.ErrorCode).Length > 0;
    internal MultiplayerShareColumn(MultiplayerController controller, Func<bool, Task> export, Action<string> content, Action<string> showDialog)
    {
        _controller = controller; _export = export; _content = content; _showDialog = showDialog;
        controller.Changed += OnChanged;
    }
    private void OnChanged() => Revision++;
    private Ui Body => Column([
        Text(Resources.MultiplayerEnvironment).FontSize(15f).FontWeight(600).Foreground(LauncherTheme.Ink).Margin(new Thickness(24f, 16f, 24f, 12f))
            .Automation(new() { Name = Resources.MultiplayerEnvironment, AutomationId = "multiplayer.environment.heading" }),
        Column([
            Grid([GridLength.Pixels(42f), GridLength.Star(), GridLength.Pixels(32f)], [GridLength.Auto], [
                new InstanceIconView(Instance?.IconData).VAlign(VerticalAlignment.Center).Cell(0, 0),
                Column([
                    Text(Instance?.DisplayName ?? string.Empty).FontSize(14f).Foreground(LauncherTheme.Ink).Wrap(),
                    Text(Instance?.Subtitle(Resources.VersionNotConfigured, Resources.Vanilla) ?? string.Empty).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap(),
                ]).Spacing(5f).VAlign(VerticalAlignment.Center).Cell(1, 0),
                new MultiplayerIcon(LauncherAppearance.Capture(),"content", Resources.MultiplayerOpenMods, () => { if (Room is { } room) _content(room.InstanceId); }).VAlign(VerticalAlignment.Center).Cell(2, 0),
            ]).ColumnSpacing(12f),
            Column([
                LauncherControls.OutlineCommand(_controller.GameActionLabel, () => _ = _controller.OpenGameAsync()).Enabled(_controller.CanOpenGame)
                    .Visible(!MultiplayerPresentation.Ended(Room) || _controller.CanOpenGame)
                    .Automation(new() { Name = _controller.GameActionLabel, AutomationId = "multiplayer.game" }),
                LauncherControls.OutlineCommand(Resources.MultiplayerShare, () => _ = _export.Invoke(false)).Visible(Room?.Role == "host"),
                LauncherControls.OutlineCommand(Row([
                    LauncherIcons.Line("download", 15f), Text(Resources.MultiplayerExport).FontSize(12f),
                ]).Spacing(6f), () => _ = _export.Invoke(true)).Style(LauncherButtons.AiTextAction).HAlign(HorizontalAlignment.Left)
                    .Automation(new() { Name = Resources.MultiplayerExport, AutomationId = "multiplayer.export" }),
            ]).Spacing(8f),
            Column([
                Text(MultiplayerText.Recovery(Room?.ErrorCode)).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap().Visible(MultiplayerText.Recovery(Room?.ErrorCode).Length > 0),
                LauncherControls.OutlineCommand(Resources.MultiplayerRetry, () => _ = _controller.ControlAsync("retry")).Enabled(!_controller.Busy).Visible(!Ending && Room?.State is "disconnected" or "manual"),
                LauncherControls.OutlineCommand(Resources.MultiplayerChangeCode, () => _showDialog("invitation")).Visible(!Ending && Room is { Role: "guest", State: "disconnected" }).Enabled(!_controller.Busy),
                LauncherControls.OutlineCommand(Resources.MultiplayerUseAddress, () => _showDialog("fallback")).Visible(!Ending && Room?.State is "disconnected" or "manual").Enabled(!_controller.Busy),
            ]).Spacing(8f).Visible(NeedsRecovery),
        ]).Spacing(16f).Margin(new Thickness(24f, 0f, 24f, 24f)),
    ]);
    protected override void OnDispose() { _controller.Changed -= OnChanged; base.OnDispose(); }
}