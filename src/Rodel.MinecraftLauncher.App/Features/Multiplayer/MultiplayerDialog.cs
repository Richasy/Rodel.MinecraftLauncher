// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Controls.Versions;
using Rodel.MinecraftLauncher.App.Features.Packs;
using Rodel.MinecraftLauncher.App.Features.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Multiplayer;
using Rodel.MinecraftLauncher.Core.Packs;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using Sprout.WindowsAppSDK;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Multiplayer;

/// <summary>Keeps create, join and environment preparation in one modal surface that cancels owned preparation before closure.</summary>
internal sealed partial class MultiplayerDialog : Control
{
    private readonly MultiplayerForms _forms;
    private readonly MultiplayerDialogBody _body;
    private readonly MultiplayerController _controller;
    private readonly Window _window;
    private readonly Action _close;
    private readonly PopupPreparationClose _closing;
    private readonly string _action;

    private partial int Revision { get; set; }

    private partial Size Viewport { get; set; }

    private bool Active => _controller.Preparing;
    private bool Cancelable { get { _ = Revision; return _controller.CanCancelPreparation; } }

    private bool CancelRequested { get { _ = Revision; return _controller.PreparationCancelRequested; } }

    private bool CanConfirm => !Active && (_action == "invitation" ? RoomCode.Parse(_controller.Code) is not null : _action == "fallback" ? !string.IsNullOrWhiteSpace(_forms.Address) : Stage switch
    {
        "create" => _controller.Create.State.CanConfirm,
        "target" => _controller.Create.State.Version.Length > 0 && string.Equals(_controller.Create.State.Version, _controller.TargetVersion, StringComparison.OrdinalIgnoreCase),
        "pack" => _controller.Pack.State.CanImport,
        "review" => _controller.Review is not null,
        _ => _controller.CanConnect,
    });
    private string Stage { get { _ = Revision; return _controller.Stage; } }

    private string Title => _action == "fallback" ? Resources.MultiplayerUseAddress : _action == "invitation" ? Resources.MultiplayerChangeCode : Stage switch
    {
        "create" => Resources.CreateInstance,
        "target" => Resources.MultiplayerTarget,
        "pack" => _controller.PreparingPackUpdate ? Resources.MultiplayerPackPrepareUpdate : Resources.PackImport,
        "review" => Resources.MultiplayerChanges,
        _ => _controller.Role == "host" ? Resources.MultiplayerCreate : Resources.MultiplayerJoin,
    };
    private string Primary => _action is "fallback" or "invitation" ? Resources.MultiplayerContinue : Stage switch
    {
        "create" => Resources.MultiplayerPrepareInstance,
        "target" => Resources.MultiplayerContinue,
        "pack" => _controller.Pack.State.HasWorkflow ? Resources.PackResumePreparation : Resources.MultiplayerPackCreate,
        "review" => Resources.MultiplayerApply,
        _ => _controller.Role == "host" ? _controller.Choices.HasGame ? Resources.MultiplayerHostRunning : Resources.MultiplayerStartHost
            : _controller.Choices.HasGame ? Resources.MultiplayerRestartJoin : Resources.MultiplayerStartJoin,
    };

    internal MultiplayerDialog(MultiplayerController controller, Window window, Action close, string action)
    {
        _forms = new(controller, window, action); _body = new(controller, _forms); _controller = controller; _window = window; _close = close; _action = action;
        _closing = new(close, () => _controller.CanCancelPreparation, () => CanClose, () => _controller.PreparationCancellationFailed);
        Viewport = window.ClientSize;
        controller.Changed += OnChanged; window.SizeChanged += OnSizeChanged;
    }

    internal bool CanClose => !Active && !_forms.PickingPack;

    internal void RequestClose()
    {
        if (_closing.Request())
            _ = _controller.CancelPreparationAsync();
    }

    private void OnChanged()
    {
        if (_closing.IsClosing || _closing.Observe())
            return;
        Revision++;
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs e) => Viewport = e.Size;

    private Ui Body => Grid([GridLength.Star()], [GridLength.Auto, GridLength.Auto, GridLength.Auto], [
        Row([
            LauncherControls.OutlineCommand(Row([LauncherIcons.Line("back", 18f)]), () => _controller.ShowStage("room"))
                .Style(LauncherButtons.IconButton).Width(32f).Visible(_action == "room" && Stage != "room").Enabled(!Active)
                .Automation(new() { Name = Resources.MultiplayerBack, AutomationId = "multiplayer.dialog.back" }),
            Text(Title).FontSize(21f).FontWeight(600).Foreground(LauncherTheme.Ink).Wrap().Weight(1f),
            LauncherControls.OutlineCommand(Row([LauncherIcons.Line("close", 18f)]), RequestClose)
                .Style(LauncherButtons.IconButton).Width(32f)
                .Automation(new() { Name = Resources.CloseLaunchSettings, AutomationId = "multiplayer.dialog.close" }),
        ]).Spacing(12f).Margin(new Thickness(24f, 20f, 24f, 16f)).Cell(0, 0),
        ScrollView(_body).MaxHeight(Math.Max(120f, Viewport.Height - 220f)).Cell(0, 1),
        Border(Grid([GridLength.Star(), GridLength.Star()], [GridLength.Auto], [
            LauncherControls.Command(Primary, Confirm).Style(LauncherButtons.DialogPrimary).Enabled(CanConfirm)
                .Visible(!Cancelable).Automation(new() { Name = Primary, AutomationId = "multiplayer.dialog.confirm" }).Cell(1, 0),
            LauncherControls.OutlineCommand(CancelRequested ? Resources.LaunchCanceling : Resources.MultiplayerCancelPreparation, () => _ = _controller.CancelPreparationAsync())
                .Style(LauncherButtons.DialogSecondary).Enabled(!CancelRequested).Visible(Cancelable)
                .Automation(new() { Name = CancelRequested ? Resources.LaunchCanceling : Resources.MultiplayerCancelPreparation, AutomationId = "multiplayer.dialog.cancel" }).Cell(1, 0),
        ]).ColumnSpacing(12f)).Padding(24f, 16f).BorderBrush(LauncherTheme.Edge).BorderThickness(0f, 1f, 0f, 0f).Cell(0, 2),
    ]).Width(Math.Min(600f, Viewport.Width - 72f));

    private async void Confirm()
    {
        if (!CanConfirm) return;
        if (_action == "fallback") { await _controller.ControlAsync("fallback", _forms.Address); if (_controller.Error.Length == 0) _close(); return; }

        if (_action == "invitation") { await _controller.ControlAsync("invitation", _controller.Code); if (_controller.Error.Length == 0) _close(); return; }

        switch (Stage)
        {
            case "target":
                if (!string.IsNullOrWhiteSpace(_controller.TargetVersion)) _controller.ShowStage("create");
                break;
            case "create": _controller.Create.ValidateVersion(); await _controller.CreateInstanceAsync(); break;
            case "pack":
                if (_controller.Pack.State.Inspection is null) await _controller.Pack.InspectAsync();
                else await _controller.Pack.ImportAsync();
                break;
            case "review": await _controller.ApplyPackAsync(); break;
            default:
                // Identity selection belongs to the shared account flow, outside the current modal.
                _close(); await _controller.ConnectAsync(); break;
        }
    }

    protected override IDisposable? OnMounted() => PopupKeyboardClose.Bind(_window, RequestClose, () => !_forms.PickingPack);

    protected override void OnDispose()
    {
        _closing.Dispose();
        _controller.Changed -= OnChanged; _window.SizeChanged -= OnSizeChanged; base.OnDispose();
    }
}

internal sealed partial class MultiplayerForms : Control
{
    private readonly MultiplayerController _model;
    private readonly Window _window;
    private readonly string _action;
    internal bool PickingPack { get; private set; }

    private readonly MultiplayerPackSummary _summary;
    private partial int Revision { get; set; }

    internal partial string Address { get; private set; }

    private MultiplayerController Controller { get { _ = Revision; return _model; } }

    private string Stage => Controller.Stage;
    internal MultiplayerForms(MultiplayerController controller, Window window, string action)
    {
        _model = controller; _window = window; _action = action; Address = string.Empty;
        _summary = new(controller);
        controller.Changed += OnChanged;
    }

    private void OnChanged() => Revision++;
    private Ui Body => Overlay([
        Column([
            Text(Resources.ServerAddress).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap(),
            TextBox(Address, value => { Address = value; Controller.Notify(); }).MinHeight(40f).Automation(new() { Name = Resources.ServerAddress, AutomationId = "multiplayer.fallback.address" }),
        ]).Spacing(10f).Visible(_action == "fallback"),
        Column([
            Text(Resources.MultiplayerCode).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap(),
            TextBox(Controller.Code, value => { if (value != Controller.Code) { Controller.TargetVersion = string.Empty; Controller.Code = value; } }).MinHeight(40f)
                .Automation(new() { Name = Resources.MultiplayerCode, AutomationId = "multiplayer.code" }),
        ]).Spacing(10f).Visible(_action == "invitation"),
        Column([
            Column([
                Text(Resources.MultiplayerCode).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap(),
                TextBox(Controller.Code, value => { if (value != Controller.Code) { Controller.TargetVersion = string.Empty; Controller.Code = value; } }).MinHeight(40f)
                    .Automation(new() { Name = Resources.MultiplayerCode, AutomationId = "multiplayer.code" }),
            ]).Spacing(10f).Visible(Controller.Role == "guest"),
            Text(Resources.MultiplayerSelectInstance).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap(),
            Text(Resources.EmptyInstances).FontSize(14f).Foreground(LauncherTheme.Ink).Visible(Controller.Instances.Count == 0),
            LauncherControls.Choice(Controller.Instances.Select(item => item.DisplayName + " · " + item.DisplayVersion))
                .SelectedIndex(Controller.Instances.ToList().FindIndex(item => item.Id == Controller.InstanceId)).OnSelectionChanged(Controller.SelectInstance)
                .MinHeight(40f).Visible(Controller.Instances.Count > 0).Automation(new() { Name = Resources.MultiplayerSelectInstance, AutomationId = "multiplayer.instance" }),
            Row([
                LauncherControls.OutlineCommand(Row([
                    LauncherIcons.Line("plus", 16f),
                    Text(Resources.CreateInstance),
                ]).Spacing(8f), () => Controller.ShowStage(Controller.Role == "guest" ? "target" : "create"))
                    .Automation(new() { AutomationId = "multiplayer.prepare.create" }),
                LauncherControls.OutlineCommand(Row([
                    LauncherIcons.Line("folder", 16f),
                    Text(Resources.PackImport),
                ]).Spacing(8f), () => _ = PickPackAsync())
                    .Automation(new() { AutomationId = "multiplayer.prepare.pack" }),
            ]).Spacing(12f),
            new MultiplayerDraftFields(_model),
            Text(Resources.MultiplayerRoomName).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap().Visible(Controller.Role == "host"),
            TextBox(Controller.Name, value => Controller.Name = value).MinHeight(40f).Visible(Controller.Role == "host")
                .Automation(new() { Name = Resources.MultiplayerRoomName, AutomationId = "multiplayer.name" }),
        ]).Spacing(12f).Visible(_action == "room" && Stage == "room"),
        Column([
            GameVersionPicker.Create(Controller.Create.State.Catalog, Controller.TargetVersion,
                ChangeTargetVersion, Controller.Create.ValidateVersion,
                "multiplayer.target", !Controller.Preparing && !Controller.Create.State.Loading).MinHeight(40f)
                .Automation(new() { Name = Resources.MultiplayerTarget, AutomationId = "multiplayer.target" }),
            Text(Controller.Create.State.VersionHint).FontSize(12f).Foreground(LauncherTheme.Error).Wrap()
                .Visible(Controller.Create.State.VersionHint.Length > 0),
        ]).Spacing(12f).Visible(_action == "room" && Stage == "target"),
        Column([
            Text(Resources.ImportName).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap(),
            TextBox(Controller.Create.State.Name, Controller.Create.ChangeName).Enabled(!Controller.Create.State.Active).MinHeight(40f)
                .Automation(new() { Name = Resources.ImportName, AutomationId = "multiplayer.create.name" }),
            Text(Resources.CreateVersion).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap(),
            TextBox(Controller.TargetVersion, _ => { }).ReadOnly().MinHeight(40f).Visible(Controller.Role == "guest")
                .Automation(new() { Name = Resources.CreateVersion, AutomationId = "multiplayer.create.required-version" }),
            GameVersionPicker.Create(Controller.Create.State.Catalog, Controller.Create.State.VersionText,
                Controller.Create.ChangeVersionText, Controller.Create.ValidateVersion,
                "multiplayer.create.version", !Controller.Create.State.Active && !Controller.Create.State.Loading, Controller.Create.State.VersionInvalid)
                .MinHeight(40f).Visible(Controller.Role == "host")
                .Automation(new() { Name = Resources.CreateVersion, AutomationId = "multiplayer.create.version" }),
            Text(Controller.Create.State.VersionHint).FontSize(12f).Foreground(LauncherTheme.Error).Wrap()
                .Visible(Controller.Create.State.VersionHint.Length > 0),
        ]).Spacing(12f).Visible(_action == "room" && Stage == "create"),
        Column([
            _summary,
            Text(string.Format(Resources.MultiplayerPackTarget, Controller.Instances.FirstOrDefault(item => item.Id == Controller.InstanceId)?.DisplayName ?? Resources.EmptyInstances))
                .FontSize(12f).Foreground(LauncherTheme.Muted).Wrap().Visible(Controller.InstanceId.Length > 0),
            Grid([GridLength.Star(), GridLength.Star()], [GridLength.Auto], [
                LauncherControls.OutlineCommand(Resources.MultiplayerPackReselect, () => _ = PickPackAsync()).Enabled(!Controller.Preparing)
                    .Automation(new() { Name = Resources.MultiplayerPackReselect, AutomationId = "multiplayer.pack.reselect" }).Cell(0, 0),
                LauncherControls.OutlineCommand(Resources.MultiplayerPackPrepareUpdate, () => _ = PrepareInstanceUpdateAsync())
                    .Enabled(!Controller.Preparing && Controller.InstanceId.Length > 0 && Controller.Pack.State.Inspection is not null)
                    .Automation(new() { Name = Resources.MultiplayerPackPrepareUpdate, AutomationId = "multiplayer.pack.update" }).Cell(1, 0),
            ]).ColumnSpacing(12f),
            new LauncherSettingsGroup(Resources.MultiplayerMissing, string.Empty, string.Empty,
            new MultiplayerMissingFiles(Controller, _window), null, window: _window, automationId: "multiplayer.missing")
                .Visible(Controller.Pack.State.HasMissing),
        ]).Spacing(14f).Visible(_action == "room" && Stage == "pack"),
        new MultiplayerDifferences(Controller.Review).Visible(_action == "room" && Stage == "review"),
    ]);
    private void ChangeTargetVersion(string value)
    {
        Controller.TargetVersion = value;
        Controller.Create.ChangeVersionText(value);
    }

    private async Task PrepareInstanceUpdateAsync()
    {
        await Controller.ReviewPackAsync(Controller.Pack.State.Source);
        if (Controller.Review is not null) Controller.ShowStage("review");
    }

    private async Task PickPackAsync()
    {
        if (PickingPack || Controller.Preparing) return;
        PickingPack = true;
        try
        {
            var picker = _window.CreateFileOpenPicker(); picker.FileTypeFilter.Add(".mrpack"); picker.FileTypeFilter.Add(".zip");
            if (await picker.PickSingleFileAsync() is not { } file) return;
            Controller.ShowStage("pack");
            Controller.Pack.SetSource(file.Path); await Controller.Pack.InspectAsync();
        }
        catch (Exception error) { Controller.ReportError(error.Message); }
        finally { PickingPack = false; }
    }

    protected override void OnDispose() { _model.Changed -= OnChanged; base.OnDispose(); }
}

internal sealed partial class MultiplayerDialogBody : Control
{
    private readonly MultiplayerController _model;
    private readonly MultiplayerForms _forms;
    private partial int Revision { get; set; }

    private MultiplayerController Controller { get { _ = Revision; return _model; } }

    private string Stage => Controller.Stage;
    private string Error => Stage switch
    {
        "create" when Controller.Create.State.Error.Length > 0 => Controller.Create.State.Error,
        "pack" when Controller.Pack.State.Error.Length > 0 => Controller.Pack.State.Error,
        _ => Controller.Error,
    };
    internal MultiplayerDialogBody(MultiplayerController controller, MultiplayerForms forms)
    { _model = controller; _forms = forms; controller.Changed += OnChanged; }

    private void OnChanged() => Revision++;
    private Ui Body => Column([
        _forms,
        Text(Error).FontSize(12f).Foreground(LauncherTheme.Error).Wrap().Visible(Error.Length > 0),
        Text(Controller.Progress).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap().Visible(Controller.CanCancelPreparation && Controller.Progress.Length > 0)
            .Automation(new() { Name = Controller.Progress, AutomationId = "multiplayer.preparation.progress" }),
    ]).Spacing(14f).Margin(new Thickness(24f, 4f, 24f, 24f));
    protected override void OnDispose() { _model.Changed -= OnChanged; base.OnDispose(); }
}