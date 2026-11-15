// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Features.Multiplayer;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using System.ComponentModel;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

/// <summary>Owns the optional app entry, explicit active-room confirmation, and its subordinate compact-window preference.</summary>
internal sealed partial class MultiplayerSettingsGroup : Control
{
    private readonly AppSettings _settings;
    private readonly Window _window;
    private readonly MultiplayerController _controller;
    private readonly LauncherSettingsGroup _group;
    private ContentDialog? _confirmation;
    private partial int Revision { get; set; }
    internal partial string Error { get; private set; }
    internal partial bool Busy { get; private set; }
    internal bool Enabled { get { _ = Revision; return _settings.MultiplayerEnabled; } }
    internal bool Mini { get { _ = Revision; return _settings.MultiplayerMiniWindow; } }
    internal bool CanChange { get { _ = Revision; return !Busy && !_controller.Busy && (!_controller.HasOwnedWork || _controller.Room?.Active == true) && _controller.Room?.State is not ("ending" or "exit-pending"); } }

    internal MultiplayerSettingsGroup(AppSettings settings, Window window, MultiplayerController controller)
    {
        _settings = settings; _window = window; _controller = controller; Error = string.Empty;
        _group = new(
            Resources.MultiplayerHeading,
            Resources.MultiplayerSettingsDescription,
            string.Empty,
            new MultiplayerSettingsContent(this),
            null,
            commands: new MultiplayerFeatureToggle(this),
            window: window,
            automationId: "settings.multiplayer.group",
            localizedHeader: () => Resources.MultiplayerHeading,
            localizedDescription: () => Resources.MultiplayerSettingsDescription);
        settings.PropertyChanged += OnPreferenceChanged;
        controller.Changed += OnRoomChanged;
    }

    internal void Expand() => _group.Expand();
    private void OnRoomChanged() => Revision++;
    private void OnPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(AppSettings.MultiplayerEnabled) or nameof(AppSettings.MultiplayerMiniWindow)) Revision++; }

    internal async Task ChangeAsync(bool enabled)
    {
        if (!CanChange || enabled == Enabled) return;
        Busy = true; Error = string.Empty;
        try
        {
            if (!enabled)
            {
                var room = await _controller.ReadRoomAsync();
                if (room?.Active == true)
                {
                    var confirmed = false;
                    var content = new MultiplayerDisableContent(value => { confirmed = value; _confirmation?.Hide(); });
                    _confirmation = new ContentDialog
                    {
                        ContentComponent = () => content,
                        InitialFocus = () => content.CancelFocus,
                    }.Style(LauncherMenus.Confirmation.ContentPadding(0f).MinWidth(320f)).Automation(new() { Name = Resources.MultiplayerDisableTitle, AutomationId = "settings.multiplayer.confirmation", IsDialog = true });
                    await _confirmation.ShowAsync(_window);
                    if (!confirmed) return;
                    await _controller.StopForFeatureAsync(room.Id);
                }
            }

            _settings.MultiplayerEnabled = enabled;
        }
        catch (Exception error) { Error = error.Message; _group.Expand(); }
        finally { _confirmation = null; Busy = false; Revision++; }
    }

    internal void ChangeMini(bool value)
    {
        try { _settings.MultiplayerMiniWindow = value; Error = string.Empty; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        { Error = error.Message; }
    }

    private Ui Body => _group;
    protected override void OnDispose()
    {
        _confirmation?.Hide();
        _settings.PropertyChanged -= OnPreferenceChanged;
        _controller.Changed -= OnRoomChanged;
        base.OnDispose();
    }
}

internal sealed partial class MultiplayerFeatureToggle(MultiplayerSettingsGroup owner) : Control
{
    private Ui Body => ToggleSwitch(owner.Enabled, value => _ = owner.ChangeAsync(value)).NoStatusText().Enabled(owner.CanChange)
        .Palette(LauncherStyles.ContentToggle).OnSurfacePaint(LauncherToggleSurface.Paint)
        .Automation(new()
        {
            Name = Resources.MultiplayerEnabled,
            AutomationId = "settings.multiplayer.enabled",
            HelpText = Resources.MultiplayerSettingsDescription,
        });
}

internal sealed partial class MultiplayerSettingsContent(MultiplayerSettingsGroup owner) : Control
{
    private Ui Body => Column([
        Grid([GridLength.Star(), GridLength.Auto], [GridLength.Auto], [
            Text(Resources.MultiplayerMiniWindow).FontSize(14f).Foreground(LauncherTheme.Ink).VAlign(VerticalAlignment.Center).Cell(0, 0),
            ToggleSwitch(owner.Mini, owner.ChangeMini).NoStatusText().Enabled(owner.Enabled && !owner.Busy)
                .Palette(LauncherStyles.ContentToggle).OnSurfacePaint(LauncherToggleSurface.Paint)
                .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Center)
                .Automation(new() { Name = Resources.MultiplayerMiniWindow, AutomationId = "settings.multiplayer-mini" }).Cell(1, 0),
        ]).ColumnSpacing(24f),
        Text(owner.Error).FontSize(12f).Foreground(LauncherTheme.Error).Wrap().Visible(owner.Error.Length > 0),
    ]).Spacing(8f).Padding(16f);
}

/// <summary>Composes launcher command skins inside the native dialog's modal, focus and Escape lifecycle.</summary>
internal sealed partial class MultiplayerDisableContent(Action<bool> decide) : Control
{
    internal InteractionNode? CancelFocus => Semantics is { } semantics && Interaction is { } interactions
        ? SettingsSearchLocator.Find(semantics, interactions, "settings.multiplayer.cancel") : null;

    private Ui Body => Column([
        Column([
            Text(Resources.MultiplayerDisableTitle).FontSize(20f).FontWeight(600).Foreground(LauncherTheme.Ink).Wrap(),
            Text(Resources.MultiplayerDisableMessage).FontSize(13f).Foreground(LauncherTheme.Ink).Wrap(),
        ]).Spacing(12f).Padding(24f),
        Rectangle(LauncherTheme.Edge).Height(1f),
        Grid([GridLength.Star(), GridLength.Star()], [GridLength.Auto], [
            LauncherControls.OutlineCommand(Resources.MultiplayerDisableAction, () => decide(true)).Style(LauncherButtons.DialogSecondary)
                .Automation(new() { Name = Resources.MultiplayerDisableAction, AutomationId = "settings.multiplayer.confirm" }).Cell(0, 0),
            LauncherControls.Command(Resources.DialogCancel, () => decide(false)).Style(LauncherButtons.DialogPrimary).AsPrimary()
                .Automation(new() { Name = Resources.DialogCancel, AutomationId = "settings.multiplayer.cancel" }).Cell(1, 0),
        ]).ColumnSpacing(8f).Padding(new Thickness(24f, 18f, 24f, 24f)),
    ]);
}