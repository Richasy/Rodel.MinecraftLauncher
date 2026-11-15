// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Multiplayer;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using Sprout.WindowsAppSDK;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Multiplayer;

/// <summary>Shows a network room around its world, people and invitation, with independent column viewports.</summary>
internal sealed partial class MultiplayerPage : Control
{
    private readonly LauncherClient _client;
    private readonly MultiplayerController _controller;
    private readonly Window _window;
    private readonly Action<string> _content;
    private readonly Sprout.Controls.ScrollView _leftScroll;
    private readonly Sprout.Controls.ScrollView _rightScroll;
    private readonly Action _showInfo;
    private readonly Action _join;
    private readonly Action _create;
    private readonly MultiplayerRoomHeader _roomHeader;
    private readonly MultiplayerRoomColumn _membersColumn;
    private readonly MultiplayerAboutButton _aboutButton;
    private Flyout? _popup;
    private Flyout? _info;
    private partial int Revision { get; set; }
    private partial float ViewWidth { get; set; }
    internal Action? RoomChanged { get; set; }
    internal bool HasOwnedWork => _controller.HasOwnedWork;
    internal string? ActiveRoomName => _controller.Room?.Active == true ? _controller.Room.Name : null;
    private MultiplayerRoom? Room { get { _ = Revision; return _controller.Room; } }
    private bool HasRoom => Room is not null;

    internal MultiplayerPage(LauncherClient client, MultiplayerController controller, Window window, Action<string> content)
    {
        _client = client; _controller = controller; _window = window; _content = content;
        _membersColumn = new(controller, window);
        _leftScroll = new(_membersColumn);
        _rightScroll = new(new MultiplayerShareColumn(controller, ExportAsync, content, ShowDialog));
        _showInfo = ShowInfo;
        _aboutButton = new(_showInfo);
        _join = Join;
        _create = () => CreateFor();
        _roomHeader = new(controller, _create, _join);
        ViewWidth = window.ClientSize.Width;
        controller.Changed += OnChanged; window.SizeChanged += OnSizeChanged;
        window.ActualThemeChanged += OnThemeChanged;
        window.LayoutUpdated += OnRoomLayoutUpdated;
    }

    internal void Start() => _controller.Start();
    private void OnChanged() { Revision++; RoomChanged?.Invoke(); }
    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs e) => ViewWidth = e.Size.Width;
    private void OnRoomLayoutUpdated()
    {
        if (!HasRoom || _leftScroll.Interaction is null) return;
        var height = _leftScroll.ViewportSize.Height;
        if (Math.Abs(_membersColumn.ViewportHeight - height) > .5f) _membersColumn.ViewportHeight = height;
    }
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        _popup?.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup);
        _info?.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.Padding(20f));
    }

    internal void CreateFor(string? instanceId = null)
    {
        if (_controller.Room?.Active == true) return;
        _controller.Open("host", instanceId); ShowDialog();
    }

    private void Join()
    {
        _controller.Open("guest"); ShowDialog();
    }

    private void ShowDialog(string action = "room")
    {
        if (ModalPopup.IsOpen(_window)) return;
        MultiplayerDialog? content = null;
        _popup = new Flyout(close => content = new MultiplayerDialog(_controller, _window, close, action))
        { Placement = PopupPlacement.Full, IsLightDismissEnabled = false, IsModal = true };
        _popup.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup).Automation(new() { Name = Resources.MultiplayerHeading, AutomationId = "multiplayer.popup", IsDialog = true });
        _popup.Closing += args =>
        {
            args.Cancel = content?.CanClose == false;
            if (args.Cancel)
                content?.RequestClose();
        };
        _popup.Opened += () => Revision++;
        _popup.Closed += () => Revision++;
        ModalPopup.TryShow(_window, _popup);
    }

    private Ui Body => Grid([GridLength.Star()], [GridLength.Pixels(LauncherLayout.PageHeaderHeight), GridLength.Auto, GridLength.Star()], [
        Row([
            Text(Resources.MultiplayerHeading).FontSize(32f).FontWeight(700).Foreground(LauncherTheme.Ink).Weight(1f).VAlign(VerticalAlignment.Center)
                .Automation(new() { Name = Resources.MultiplayerHeading, AutomationId = "multiplayer.heading" }),
            Row([
                Text(Resources.MultiplayerProvider).FontSize(12f).Foreground(LauncherTheme.Muted).VAlign(VerticalAlignment.Center)
                    .Automation(new() { Name = Resources.MultiplayerProvider, AutomationId = "multiplayer.provider" }),
                _aboutButton.VAlign(VerticalAlignment.Center),
            ]).Spacing(12f).VAlign(VerticalAlignment.Center),
        ]).Spacing(12f).VAlign(VerticalAlignment.Bottom).Cell(0, 0),
        Text(_controller.Error).FontSize(12f).Foreground(LauncherTheme.Error).Wrap().Visible(_controller.Error.Length > 0 && _popup?.IsOpen != true)
            .Margin(new Thickness(0f, LauncherLayout.PageHeaderGap, 0f, 0f)).Cell(0, 1),
        Border(Grid([GridLength.Star(), GridLength.Pixels(1f), GridLength.Star()], [GridLength.Star()], [
            new MultiplayerEntry(LauncherAppearance.Capture(),true, _create, _controller.Role == "host" && (_controller.Preparing || _controller.Stage != "room")).Cell(0, 0),
            Rectangle(LauncherTheme.Edge).Cell(1, 0),
            new MultiplayerEntry(LauncherAppearance.Capture(),false, _join, _controller.Role == "guest" && (_controller.Preparing || _controller.Stage != "room")).Cell(2, 0),
        ])).Background(LauncherMaterials.Acrylic).BorderBrush(LauncherTheme.Edge).BorderThickness(1f)
            .Visible(!HasRoom).Margin(new Thickness(0f, LauncherLayout.PageHeaderGap, 0f, 0f)).Cell(0, 2),
        Border(Grid([GridLength.Star()], [GridLength.Auto, GridLength.Pixels(1f), GridLength.Star()], [
            _roomHeader.Cell(0, 0),
            Rectangle(LauncherTheme.Edge).Cell(0, 1),
            Grid([GridLength.Star(), GridLength.Pixels(1f), GridLength.Pixels(ViewWidth < 1050f ? 258f : 320f)], [GridLength.Star()], [
                _leftScroll.Automation(new() { Name = Resources.MultiplayerMembers, AutomationId = "multiplayer.room.scroll" }).Cell(0, 0),
                Rectangle(LauncherTheme.Edge).Cell(1, 0),
                _rightScroll.Automation(new() { Name = Resources.MultiplayerEnvironment, AutomationId = "multiplayer.share.scroll" }).Cell(2, 0),
            ]).Cell(0, 2),
        ])).Background(LauncherMaterials.Acrylic).BorderBrush(LauncherTheme.Edge).BorderThickness(1f).Visible(HasRoom)
            .Margin(new Thickness(0f, LauncherLayout.PageHeaderGap, 0f, 0f)).Cell(0, 2),
    ]);

    private void ShowInfo()
    {
        _info = new Flyout(() => new MultiplayerAbout()) { Placement = PopupPlacement.BottomEdgeAlignedRight };
        _info.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.Padding(20f));
        if (_aboutButton.Interaction is { } target) _info.ShowAt(_window, target);
        else _info.ShowAt(_window);
    }

    private async Task ExportAsync(bool log)
    {
        if (Room is not { } room) return;
        try
        {
            var picker = _window.CreateFileSavePicker();
            picker.SuggestedFileName = log ? "connection" : "multiplayer";
            picker.FileTypeChoices.Add(log ? Resources.MultiplayerExport : Resources.MultiplayerShare, [log ? ".txt" : ".mrpack"]);
            if (await picker.PickSaveFileAsync() is not { } file) return;
            if (log)
            {
                await _client.Multiplayer.ExportLogAsync(room.Id, file.Path);
            }
            else
            {
                var result = await _client.Packs.ExportAsync(room.InstanceId, file.Path, forMultiplayer: true);
                if (!result.Complete) _controller.ReportError(Resources.MultiplayerExportIncomplete);
            }
        }
        catch (Exception error) { _controller.ReportError(error.Message); }
    }

    protected override void OnDispose()
    {
        _controller.Changed -= OnChanged; _window.SizeChanged -= OnSizeChanged;
        _window.ActualThemeChanged -= OnThemeChanged;
        _window.LayoutUpdated -= OnRoomLayoutUpdated;
        _popup?.Hide(); _info?.Hide(); _controller.Dispose(); base.OnDispose();
    }
}