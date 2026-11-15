// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.GameBridge;
using Rodel.MinecraftLauncher.Core.Instances;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using System.Globalization;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Instances;

/// <summary>Presents one instance's actual bridge state and bounded local edits through Core.</summary>
internal sealed partial class GameBridgeDialog : Control
{
    private readonly LauncherClient _client;
    private readonly string _instanceId;
    private readonly string _name;
    private readonly Window _window;
    private readonly Action _close;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _ct;
    private bool _reading;
    private string? _session;
    private string? _operationSession;
    private string? _operationId;
    internal partial bool Enabled { get; set; }
    internal partial bool Saving { get; set; }
    internal partial bool Editing { get; set; }
    private partial bool Unresolved { get; set; }
    internal partial string Status { get; set; }
    internal partial string Error { get; set; }
    internal partial string Receipt { get; set; }
    internal partial string Value { get; set; }
    internal partial int Kind { get; set; }
    internal partial GameSnapshot? Snapshot { get; set; }

    internal GameBridgeDialog(
        LauncherClient client,
        InstanceRecord instance,
        bool enabled,
        Window window,
        Action close)
    {
        _client = client;
        _instanceId = instance.Id;
        _window = window;
        _close = close;
        _ct = _lifetime.Token;
        _name = instance.Name;
        Enabled = enabled;
        Status = Resources.BridgeLoading;
        Error = string.Empty;
        Receipt = string.Empty;
        Value = "1000";
    }

    internal bool CanApply => !Editing && !Unresolved && Snapshot is { Ready: true, Mode: "local_owned" } && (Kind == 0 ? long.TryParse(Value, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && ticks >= 0 : float.TryParse(Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var health) && float.IsFinite(health) && health is >= GameBridgeLimits.MinimumHealth and <= GameBridgeLimits.MaximumHealth);
    internal string Observation => Snapshot is { Ready: true } value ? string.Format(
        Resources.BridgeObservation,
        value.Dimension,
        value.X,
        value.Y,
        value.Z,
        value.Health,
        value.DayTime) : string.Empty;

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Auto, GridLength.Auto],
        [
            Column([
                Text(Resources.BridgeTitle)
                    .FontSize(20f).FontWeight(700).Foreground(LauncherTheme.Ink),
                Text(_name)
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Wrap(),
            ])
                .Spacing(5f)
                .Padding(24f, 20f, 24f, 16f)
                .Cell(0, 0),
            ScrollView(new GameBridgeBody(
LauncherAppearance.Capture(),                    Self,
                    Enabled,
                    Saving,
                    Editing,
                    Status,
                    Observation,
                    Receipt,
                    Error,
                    Value,
                    Kind,
                    Snapshot,
                    CanApply))
                .MaxHeight(Math.Max(100f, _window.ClientSize.Height - 220f))
                .Cell(0, 1),
            Border(LauncherControls.OutlineCommand(Resources.CloseLaunchSettings, _close)
                    .Style(LauncherButtons.DialogSecondary)
                    .MinHeight(40f)
                    .Automation(new()
                    {
                        Name = Resources.CloseLaunchSettings,
                        AutomationId = "game.bridge.close"
                    }))
                .Padding(24f, 16f, 24f, 20f)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 1f, 0f, 0f)
                .Cell(0, 2),
        ])
        .Width(Math.Min(480f, _window.ClientSize.Width - 48f));

    internal async Task SaveEnabledAsync(bool enabled)
    {
        if (Saving)
            return;
        Saving = true;
        try
        {
            await _client.Settings.SetAsync("gameBridgeEnabled", JsonValue.Create(enabled), _client.Instances.Get(_instanceId), _ct);
            Enabled = enabled;
        }
        catch (OperationCanceledException) when (_ct.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Error = string.Format(Resources.BridgeFailure, error.Message);
        }
        finally
        {
            Saving = false;
        }
    }

    private async Task ObserveAsync()
    {
        try
        {
            while (!_ct.IsCancellationRequested)
            {
                if (!Editing)
                {
                    if (Unresolved && _operationSession is not null && _operationId is not null)
                    {
                        try
                        {
                            ShowReceipt(await _client.GameBridge.GetOperationAsync(_operationSession, _operationId, _ct));
                        }
                        catch (OperationCanceledException) when (_ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception)
                        {
                            Receipt = Resources.BridgeUnknown;
                        }
                    }

                    await RefreshAsync();
                }

                await Task.Delay(1000, _ct);
            }
        }
        catch (OperationCanceledException) when (_ct.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshAsync()
    {
        if (_reading)
            return;
        _reading = true;
        try
        {
            var sessions = await Task.Run(() => _client.Sessions.List().Where(value => value.InstanceId == _instanceId && value.Status == "running").ToArray(), _ct);
            if (sessions.Length != 1)
            {
                Snapshot = null;
                _session = null;
                Error = string.Empty;
                Status = sessions.Length == 0 ? Resources.BridgeNoGame : Resources.BridgeChooseSession;
                return;
            }

            _session = sessions[0].Id!;
            var bridge = await _client.GameBridge.GetStatusAsync(_session, _ct);
            if (bridge.State != "connected")
            {
                Snapshot = null;
                Error = string.Empty;
                Status = bridge.State == "disabled" ? Resources.BridgeDisabled : Resources.BridgeOffline;
                return;
            }

            Snapshot = await _client.GameBridge.SnapshotAsync(_session, _ct);
            Status = !Snapshot.Ready
                ? Snapshot.ErrorCode == "GAME_BINDING_UNSUPPORTED" ? Resources.BridgeUnsupported : Resources.BridgeWaitingWorld
                : Snapshot.Mode == "local_owned" ? Resources.BridgeLocal : Resources.BridgeRemote;
            Error = string.Empty;
        }
        catch (OperationCanceledException) when (_ct.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Snapshot = null;
            Status = Resources.BridgeOffline;
            Error = string.Format(Resources.BridgeFailure, error.Message);
        }
        finally
        {
            _reading = false;
        }
    }

    internal async Task ApplyAsync()
    {
        if (!CanApply || _session is null || Snapshot is not { } observed)
            return;
        Editing = true;
        Error = string.Empty;
        Receipt = Resources.BridgeQueued;
        var session = _session;
        var operation = Guid.NewGuid().ToString("N");
        _operationSession = session;
        _operationId = operation;
        var edit = new GameEditRequest
        {
            GameIncarnation = observed.GameIncarnation,
            WorldEpoch = observed.WorldEpoch!,
            Dimension = observed.Dimension!,
            OperationId = operation,
            Kind = Kind == 0 ? "world.setTime" : "player.setHealth",
            DayTime = Kind == 0 ? long.Parse(Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) : null,
            Health = Kind == 1 ? float.Parse(Value, CultureInfo.InvariantCulture) : null,
        };
        try
        {
            var result = await _client.GameBridge.EditAsync(session, edit, _ct);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (result.State is "queued" or "applying" && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(200, _ct);
                result = await _client.GameBridge.GetOperationAsync(session, operation, _ct);
            }

            ShowReceipt(result);
        }
        catch (OperationCanceledException) when (_ct.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Unresolved = true;
            Receipt = Resources.BridgeUnknown;
            Error = string.Format(Resources.BridgeFailure, error.Message);
        }
        finally
        {
            Editing = false;
        }
    }

    private void ShowReceipt(GameOperation result)
    {
        Unresolved = result.State is "queued" or "applying" or "unknown";
        Receipt = result.State switch
        {
            "applied" => string.Format(Resources.BridgeApplied, result.DayTime ?? result.Health?.ToString(CultureInfo.CurrentCulture)),
            "canceled" => Resources.BridgeCanceled,
            "rejected" => string.Format(Resources.BridgeRejected, result.ErrorCode),
            _ => Resources.BridgeUnknown,
        };
    }

    internal GameBridgeDialog Self => this;

    protected override IDisposable? OnMounted()
    {
        _ = ObserveAsync();
        return PopupKeyboardClose.Bind(_window, _close, static () => true);
    }

    protected override void OnDispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnDispose();
    }
}

/// <summary>Projects one immutable dialog state so live bridge changes participate in view reconciliation.</summary>
/// <param name = "Owner">The control receiving user callbacks; it is not the view's state identity.</param>
/// <param name = "Enabled">Whether the next launch should request the bridge.</param>
/// <param name = "Saving">Whether the instance setting is being committed.</param>
/// <param name = "Editing">Whether an edit is being submitted or its initial receipt observed.</param>
/// <param name = "Status">The localized current connectivity and ownership explanation.</param>
/// <param name = "Observation">The formatted latest live sample, empty when unavailable.</param>
/// <param name = "Receipt">The most recent edit outcome or unresolved-effect explanation.</param>
/// <param name = "Error">The current operation or observation failure, if any.</param>
/// <param name = "Value">The uncommitted edit input, retained for validation and correction.</param>
/// <param name = "Kind">The selected fixed operation: zero for time, one for health.</param>
/// <param name = "Snapshot">The actual sampled world controlling observation and form visibility.</param>
/// <param name = "CanApply">Whether a local edit is valid and no effect remains unresolved.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct GameBridgeBody(
    LauncherAppearance Theme,
    GameBridgeDialog Owner,
    bool Enabled,
    bool Saving,
    bool Editing,
    string Status,
    string Observation,
    string Receipt,
    string Error,
    string Value,
    int Kind,
    GameSnapshot? Snapshot,
    bool CanApply)
{
    internal void SetEnabled(bool value) => _ = Owner.SaveEnabledAsync(value);

    internal void SetKind(int value)
    {
        Owner.Kind = value;
        Owner.Value = value == 0 ? "1000" : "13";
    }

    internal void SetValue(string value) => Owner.Value = value;
    internal Action ApplyAction
    {
        get
        {
            var owner = Owner;
            return () => _ = owner.ApplyAsync();
        }
    }

    /// <summary>Renders one grouped status and the local world's fixed edit form.</summary>
    public Ui Body => Column([
        Grid(
            [GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                Text(Resources.BridgeEnableNextLaunch)
                    .FontSize(14f).Foreground(Theme.Ink)
                    .Wrap()
                    .VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                ToggleSwitch(Enabled, SetEnabled)
                    .NoStatusText()
                    .Palette(Theme.Styles.ContentToggle)
                    .OnSurfacePaint(LauncherToggleSurface.Paint)
                    .Enabled(!Saving)
                    .Automation(new()
                    {
                        Name = Resources.BridgeEnableNextLaunch,
                        AutomationId = "game.bridge.enable"
                    })
                    .Cell(1, 0),
            ])
            .ColumnSpacing(20f),
        Border(Column([
                Text(Status)
                    .FontSize(14f).Foreground(Theme.Ink)
                    .Wrap()
                    .Automation(new()
                    {
                        Name = Status,
                        AutomationId = "game.bridge.status",
                        LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                    }),
                Text(Observation)
                    .FontSize(12f).Foreground(Theme.Muted)
                    .Wrap()
                    .Visible(Snapshot is { Ready: true })
                    .Automation(new()
                    {
                        Name = Observation,
                        AutomationId = "game.bridge.snapshot"
                    }),
            ])
                .Spacing(12f))
            .Padding(16f)
            .BorderBrush(Theme.Edge)
            .BorderThickness(1f),
        Column([
            Text(Resources.BridgeLocalEdit)
                .FontSize(14f).Foreground(Theme.Ink),
            LauncherControls.Choice(Theme,[Resources.BridgeTime, Resources.BridgeHealth])
                .SelectedIndex(Kind)
                .OnSelectionChanged(SetKind)
                .MinHeight(40f)
                .Enabled(!Editing)
                .Automation(new()
                {
                    Name = Resources.BridgeLocalEdit,
                    AutomationId = "game.bridge.kind"
                }),
            TextBox(Value, SetValue)
                .FontSize(14f)
                .MinHeight(40f)
                .Padding(12f, 10f)
                .Enabled(!Editing)
                .Automation(new()
                {
                    Name = Kind == 0 ? Resources.BridgeTime : Resources.BridgeHealth,
                    AutomationId = "game.bridge.value"
                }),
            LauncherControls.Command(Theme,Resources.BridgeApply, ApplyAction)
                .Style(Theme.Buttons.DialogPrimary)
                .MinHeight(40f)
                .Enabled(CanApply)
                .Automation(new()
                {
                    Name = Resources.BridgeApply,
                    AutomationId = "game.bridge.apply"
                }),
        ])
            .Spacing(10f)
            .Visible(Snapshot is { Ready: true, Mode: "local_owned" }),
        Text(Receipt)
            .FontSize(12f).Foreground(Theme.Muted)
            .Wrap()
            .Visible(Receipt.Length > 0)
            .Automation(new()
            {
                Name = Receipt,
                AutomationId = "game.bridge.receipt",
                LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
            }),
        Text(Error)
            .FontSize(12f).Foreground(Theme.Error)
            .Wrap()
            .Visible(Error.Length > 0)
            .Automation(new()
            {
                Name = Error,
                AutomationId = "game.bridge.error",
                LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
            }),
    ])
        .Spacing(16f)
        .Padding(24f, 4f, 24f, 20f);
}