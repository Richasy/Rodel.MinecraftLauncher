// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Accounts;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Servers;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using System.Text.Json;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Servers;

internal sealed partial class ServerDialog : Control
{
    private readonly LauncherClient _client;
    private readonly HomeController _home;
    private readonly Window _window;
    private readonly ServerAction _action;
    private readonly ServerFavorite? _favorite;
    private readonly ServerStatus? _status;
    private readonly Action _close;
    private bool _closeAfterCancel;
    private readonly Action _changed;
    private readonly Action _showLocal;
    private readonly Action<ServerConnectionDraft> _saveDraft;
    private readonly Action<string> _recover;
    private readonly Action<string> _clientSelected;
    private readonly Action<LaunchTask> _connect;
    internal string CurrentAddress => Address;
    private readonly string? _draftAccountId;
    private string? _configuredAccountId;
    private string _configuredPlayerName = "Player";
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PopupFocusRecovery _focus;
    private readonly DetailReportPopup _report;
    private long _selectionRevision;
    private IdentityRecord[] _accountsData = [];
    private string _rawError = string.Empty;

    internal ServerDialog(
        LauncherClient client,
        HomeController home,
        Window window,
        ServerAction action,
        ServerFavorite? favorite,
        ServerStatus? status,
        Action close,
        Action changed,
        Action showLocal,
        ServerConnectionDraft? draft,
        Action<ServerConnectionDraft> saveDraft,
        Action<string> recover,
        Action<string> clientSelected,
        Action<LaunchTask> connect)
    {
        _client = client;
        _home = home;
        _window = window;
        _action = action;
        _favorite = favorite;
        _status = status;
        _close = close;
        _changed = changed;
        _showLocal = showLocal;
        _saveDraft = saveDraft;
        _recover = recover;
        _clientSelected = clientSelected;
        _connect = connect;
        Name = favorite?.Name ?? string.Empty;
        Address = favorite?.Address ?? draft?.Address ?? string.Empty;
        InstanceId = draft?.InstanceId ?? favorite?.PreferredInstanceId ?? (favorite?.InstanceIds.Length == 1 ? favorite.InstanceIds[0] : string.Empty);
        _draftAccountId = draft?.AccountId;
        Remember = draft?.Remember == true;
        Associations = favorite?.InstanceIds ?? [];
        Preferred = favorite?.PreferredInstanceId ?? string.Empty;
        Instances = ClientChoices(home.State);
        AccountNames = [];
        Error = string.Empty;
        ConfiguredIdentity = Resources.ServerConfiguredIdentity;
        Launch = new("idle");
        if (Connecting && home.State.LaunchStates.GetValueOrDefault(InstanceId) is { } previous && previous.ServerAddress == Address)
        {
            JobId = previous.JobId;
            Launch = previous;
        }

        Viewport = window.ClientSize;
        _focus = new(window);
        _report = new(
            window,
            Resources.ServerDetails,
            string.Empty,
            close,
            ContentWidth,
            "servers.report",
            back: () =>
            {
                Details = false;
                _focus.Request(this);
            },
            canClose: () => CanClose,
            showCloseButton: false);
    }

    private partial string Name { get; set; }
    private partial string Address { get; set; }
    private partial string InstanceId { get; set; }
    private partial string Preferred { get; set; }
    private partial IReadOnlyList<string> Associations { get; set; }
    private partial InstanceSummary[] Instances { get; set; }
    private partial string[] AccountNames { get; set; }
    private partial int AccountIndex { get; set; }
    private partial string ConfiguredIdentity { get; set; }
    private partial bool Remember { get; set; }
    private partial bool Saving { get; set; }
    private partial bool Loading { get; set; }
    private partial bool IdentityLoading { get; set; }
    private partial bool IdentityReady { get; set; }
    private partial bool CancelRequested { get; set; }
    private partial string Error { get; set; }
    private partial string? JobId { get; set; }
    private partial HomeLaunchState Launch { get; set; }
    private partial bool Details { get; set; }
    private partial Size Viewport { get; set; }
    private bool Connecting => _action is ServerAction.Connect or ServerAction.Direct;
    private bool Active => Saving || Launch.TaskActive;
    internal bool CanClose => !Saving && (!Launch.TaskActive || !Launch.TaskOwned);
    private bool Started => Launch.Status == "running";
    private bool HasConnectionError => Error.Length > 0 || (Launch.Problem is { } problem && LaunchProblemText.OriginalError(problem) is not null);
    private float ContentWidth => Math.Min(480f, Viewport.Width - 48f);
    private int InstanceIndex => Array.FindIndex(Instances, instance => instance.Id == InstanceId) + 1;
    private string[] InstanceNames => [Resources.ServerChooseInstance, .. Instances.Select(instance => string.Format(Resources.ServerInstanceChoice, instance.DisplayName, instance.DisplayVersion))];
    private string[] IdentityNames => [ConfiguredIdentity, .. AccountNames];
    private string Title => _action switch
    {
        ServerAction.Add => Resources.ServerAdd,
        ServerAction.Edit => Resources.ServerEdit,
        ServerAction.Associate => Resources.ServerAssociate,
        ServerAction.Remove => Resources.ServerRemove,
        ServerAction.Connect => string.Format(Resources.ServerConnectHeading, _favorite!.Name),
        ServerAction.ClaimClient => Resources.ServerAddClient,
        _ => Resources.ServerDirect,
    };
    private string SubmitLabel => Connecting
        ? Launch.Status is "failed" or "canceled" or "exited" or "interrupted" ? Resources.ServerRetry : Resources.ServerConnect
        : _action == ServerAction.Remove
        ? Resources.ServerRemove
        : _action == ServerAction.ClaimClient ? Resources.ServerClaimClient : Resources.SaveLaunchSettings;
    private string DismissLabel => Launch.TaskActive && Launch.TaskOwned
        ? CancelRequested ? Resources.LaunchCanceling : Resources.CancelLaunch
        : Started || Launch.TaskActive ? Resources.CloseLaunchSettings : Resources.DialogCancel;
    private string AddressError
    {
        get
        {
            if (Address.Length == 0)
                return string.Empty;
            try
            {
                ServerService.ParseAddress(Address);
                return string.Empty;
            }
            catch (ArgumentException)
            {
                return Resources.ServerInvalidAddress;
            }
        }
    }

    private bool CanSubmit => !Loading && !Active && !Started && (_action switch
    {
        ServerAction.Add or ServerAction.Edit => Name.Trim().Length > 0 && Address.Trim().Length > 0 && AddressError.Length == 0,
        ServerAction.Connect or ServerAction.Direct => InstanceIndex > 0 && AccountIndex >= 0 && (AccountIndex > 0 || (IdentityReady && !IdentityLoading)) && Address.Trim().Length > 0 && AddressError.Length == 0,
        ServerAction.ClaimClient => InstanceIndex > 0,
        _ => true,
    });
    private ServerAssociationData[] AssociationRows => [..Instances.Select(instance => new ServerAssociationData(
                instance.Id,
                instance.DisplayName,
                Associations.Contains(instance.Id),
                true,
                value => SelectAssociation(instance.Id, value),
                !Active)).Concat(Associations.Where(id => Instances.All(instance => instance.Id != id)).Select(id => new ServerAssociationData(
                    id,
                    string.Format(Resources.ServerMissingInstanceDetail, id),
                    true,
                    false,
                    value => SelectAssociation(id, value),
                    !Active)))];
    private InstanceSummary[] PreferredChoices => [.. Instances.Where(instance => Associations.Contains(instance.Id))];
    private string[] PreferredNames => [Resources.ServerChooseEachTime, .. PreferredChoices.Select(instance => instance.DisplayName)];
    private int PreferredIndex => Array.FindIndex(PreferredChoices, instance => instance.Id == Preferred) + 1;
    private ServerFormState Form => new(
        _action,
        Name,
        Address,
        AddressError,
        InstanceNames,
        InstanceIndex,
        IdentityNames,
        AccountIndex,
        Remember,
        AssociationRows,
        PreferredNames,
        PreferredIndex,
        Loading,
        Active,
        Started,
        Error,
        _favorite?.Name ?? string.Empty,
        _status is { Online: false, Loading: false },
        Instances.Length == 0,
        System.Globalization.CultureInfo.CurrentUICulture.Name,
        LauncherTheme.Ink,
        LauncherTheme.Muted,
        LauncherTheme.Error,
        _window.FlowDirection == FlowDirection.RightToLeft);

    private Ui Body => Overlay([
        _report.Visible(Details),
        Grid(
            [GridLength.Star()],
            [GridLength.Auto, GridLength.Auto, GridLength.Auto],
            [
                Border(Grid(
                        [GridLength.Star(), GridLength.Auto],
                        [GridLength.Auto],
                        [
                            Text(Title)
                                .FontSize(19f).FontWeight(600).Foreground(LauncherTheme.Ink)
                                .Wrap()
                                .Cell(0, 0),
                            LauncherControls.OutlineCommand(Connecting ? Resources.ProblemInspect : Resources.ServerDetails, () =>
                                {
                                    if (Connecting)
                                        _recover(InstanceId);
                                    else
                                        _ = ShowDetailsAsync();
                                })
                                .Style(LauncherButtons.DialogHeaderButton)
                                .VAlign(VerticalAlignment.Center)
                                .Enabled(!Connecting || (CanClose && InstanceId.Length > 0))
                                .Visible(Connecting ? HasConnectionError : Error.Length > 0 || JobId is not null)
                                .Automation(new()
                                {
                                    Name = Connecting ? Resources.ProblemInspect : Resources.ServerDetails,
                                    AutomationId = "servers.details"
                                })
                                .Cell(1, 0),
                        ])
                        .ColumnSpacing(12f))
                    .Padding(new Thickness(24f, 20f, 24f, 16f))
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(0f, 0f, 0f, 1f)
                    .Cell(0, 0),
                ScrollView(new ServerDialogForm(LauncherAppearance.Capture(),this, Form))
                    .MaxHeight(Math.Max(80f, Viewport.Height - 220f))
                    .Automation(new()
                    {
                        Name = Title,
                        AutomationId = "servers.dialog.scroll"
                    })
                    .Cell(0, 1),
                Border(Grid(
                        [GridLength.Star(), GridLength.Star()],
                        [GridLength.Auto],
                        [
                            LauncherControls.Command(SubmitLabel, () => _ = SubmitAsync())
                                .Style(LauncherButtons.DialogPrimary)
                                .Enabled(CanSubmit)
                                .Visible(!Active && !Started)
                                .Automation(new()
                                {
                                    Name = SubmitLabel,
                                    AutomationId = "servers.submit"
                                })
                                .Cell(0, 0),
                            LauncherControls.OutlineCommand(DismissLabel, () => _ = DismissAsync())
                                .Style(LauncherButtons.DialogSecondary)
                                .Enabled(!Saving && (!Launch.TaskOwned || !Active || !CancelRequested))
                                .Automation(new()
                                {
                                    Name = DismissLabel,
                                    AutomationId = "servers.dismiss"
                                })
                                .Cell(1, 0),
                        ])
                        .ColumnSpacing(12f))
                    .Padding(new Thickness(24f, 16f, 24f, 20f))
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(0f, 1f, 0f, 0f)
                    .Cell(0, 2),
            ])
            .Width(ContentWidth)
            .Visible(!Details),
    ]);

    private async Task LoadAsync()
    {
        Loading = true;
        try
        {
            _accountsData = await Task.Run(() => AccountsFeature.ReadAccounts(_client.Identities.List()), _lifetime.Token);
            AccountNames = [.. _accountsData.Select(account => string.Format(Resources.AccountMenuEntry, account.Name, AccountsFeature.TypeLabel(account)))];
            if (_draftAccountId is not null)
            {
                var index = Array.FindIndex(_accountsData, account => account.Id == _draftAccountId);
                AccountIndex = index < 0 ? -1 : index + 1;
                if (index < 0)
                    Error = Resources.ServerIdentityUnavailable;
            }

            await ReadConfiguredIdentityAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
                Loading = false;
        }
    }

    internal void ChangeName(string value) => Name = value;

    internal void ChangeAddress(string value)
    {
        if (value != Address)
            ResetAttempt();
        Address = value;
    }

    internal void ToggleRemember() => Remember = !Remember;

    internal void ChooseAccount(int index)
    {
        if (index != AccountIndex)
            ResetAttempt();
        AccountIndex = index;
    }

    internal void ChoosePreferred(int index) => Preferred = index > 0 && index <= PreferredChoices.Length ? PreferredChoices[index - 1].Id : string.Empty;

    internal void ShowLocal() => _showLocal();

    internal void ChooseInstance(int index)
    {
        var id = index > 0 && index <= Instances.Length ? Instances[index - 1].Id : string.Empty;
        if (id != InstanceId)
            ResetAttempt();
        InstanceId = id;
        _home.ObserveProblemsFor(Connecting ? id : null, "server-dialog");
        _ = ReadConfiguredIdentityAsync();
    }

    private void ResetAttempt()
    {
        if (Active)
            return;
        JobId = null;
        Launch = new("idle");
        Error = string.Empty;
        _rawError = string.Empty;
        CancelRequested = false;
    }

    private async Task ReadConfiguredIdentityAsync()
    {
        var revision = ++_selectionRevision;
        IdentityReady = false;
        if (InstanceId.Length == 0)
        {
            ConfiguredIdentity = Resources.ServerConfiguredIdentity;
            IdentityLoading = false;
            return;
        }

        IdentityLoading = true;
        var id = InstanceId;
        try
        {
            var values = await Task.Run(() => _client.Settings.GetEffective(_client.Instances.Get(id)).Values, _lifetime.Token);
            if (_lifetime.IsCancellationRequested || revision != _selectionRevision)
                return;
            var account = _accountsData.FirstOrDefault(item => item.Id == values?["accountId"]?.ToString());
            if (account is null && values?["accountId"] is not null)
            {
                ConfiguredIdentity = Resources.ServerIdentityUnavailable;
                return;
            }

            var player = account?.Name ?? values?["playerName"]?.ToString() ?? "Player";
            _configuredAccountId = account?.Id;
            _configuredPlayerName = player;
            IdentityReady = true;
            ConfiguredIdentity = string.Format(Resources.ServerConfiguredIdentityName, player);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (revision == _selectionRevision)
                ReportError(exception);
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested && revision == _selectionRevision)
                IdentityLoading = false;
        }
    }

    private void SelectAssociation(string id, bool selected)
    {
        Associations = selected ? [.. Associations.Append(id).Distinct()] : [.. Associations.Where(value => value != id)];
        if (!selected && Preferred == id)
            Preferred = string.Empty;
    }

    private async Task SubmitAsync()
    {
        if (!CanSubmit)
            return;
        Saving = true;
        Error = string.Empty;
        _rawError = string.Empty;
        CancelRequested = false;
        try
        {
            if (_action is ServerAction.Add or ServerAction.Edit)
            {
                await _client.Servers.AddAsync(Name.Trim(), Address.Trim(), _favorite?.Id, _lifetime.Token);
            }
            else if (_action == ServerAction.Associate)
            {
                await _client.Servers.SetInstancesAsync(_favorite!.Id, Associations, Preferred.Length == 0 ? null : Preferred, _lifetime.Token);
            }
            else if (_action == ServerAction.Remove)
            {
                await _client.Servers.RemoveAsync(_favorite!.Id, _lifetime.Token);
            }
            else if (_action == ServerAction.ClaimClient)
            {
                await _client.Servers.SetClientOwnerAsync(InstanceId, _favorite!.Id, ct: _lifetime.Token);
            }
            else
            {
                var instanceId = InstanceId;
                var options = new LaunchOptions
                {
                    ServerAddress = Address.Trim(),
                    AccountId = AccountIndex > 0 && AccountIndex <= _accountsData.Length ? _accountsData[AccountIndex - 1].Id : _configuredAccountId,
                    PlayerName = AccountIndex == 0 && _configuredAccountId is null ? _configuredPlayerName : null,
                    InheritIdentity = false,
                };
                if (Remember && _favorite is not null)
                {
                    await _client.Servers.AssociateAsync(_favorite.Id, instanceId, preferred: true, ct: _lifetime.Token);
                    _changed();
                }

                var request = await _client.CreateServerLaunchAsync(Address.Trim(), instanceId, options, _lifetime.Token);
                _clientSelected(instanceId);
                Saving = false;
                _connect(request);
                return;
            }

            _changed();
            Saving = false;
            _close();
            if (_action == ServerAction.ClaimClient)
                await _home.RefreshAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
            {
                Saving = false;
                _focus.RequestIfLost(this);
            }
        }
    }

    internal void RequestClose() => _ = DismissAsync();

    private async Task DismissAsync()
    {
        if (Saving || (CancelRequested && Active))
            return;
        if (Launch.TaskActive && Launch.TaskOwned && JobId is { } job)
        {
            try
            {
                _closeAfterCancel = true;
                CancelRequested = true;
                await _client.Jobs.CancelAsync(job, _lifetime.Token);
            }
            catch (Exception exception)
            {
                _closeAfterCancel = false;
                CancelRequested = false;
                ReportError(exception);
            }
        }
        else
        {
            _close();
        }
    }

    private void OnHomeChanged(HomePageState state)
    {
        Instances = ClientChoices(state);
        if (JobId is null || state.LaunchStates.GetValueOrDefault(InstanceId) is not { } launch || launch.JobId != JobId)
            return;
        var wasActive = Launch.TaskActive;
        Launch = launch;
        if (_closeAfterCancel && !launch.TaskActive)
        {
            _closeAfterCancel = false;
            _close();
            return;
        }

        if (launch.Error is { Length: > 0 } error)
            Error = string.Format(Resources.ServerOperationFailed, error);
        if (wasActive && !launch.TaskActive)
            _focus.RequestIfLost(this);
    }

    private void ReportError(Exception exception)
    {
        if (_lifetime.IsCancellationRequested)
            return;
        Error = string.Format(Resources.ServerOperationFailed, exception.Message);
        _rawError = LauncherOperationException.ToResult(exception).ToJsonString();
    }

    private InstanceSummary[] ClientChoices(HomePageState state) => [.. state.Instances.Where(instance => !instance.Unreadable && (instance.ServerId is null || (_action != ServerAction.ClaimClient && instance.ServerId == _favorite?.Id)))];

    private async Task ShowDetailsAsync()
    {
        try
        {
            var raw = JobId is { } job ? JsonSerializer.Serialize(await Task.Run(() => _client.Jobs.Get(job), _lifetime.Token), ApplicationJsonContext.Job) : _rawError;
            if (_lifetime.IsCancellationRequested)
                return;
            _report.ViewHeight = Render.Bounds.Height;
            _report.SetReport(Error, raw);
            Details = true;
            _focus.Request(_report);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs args) => Viewport = args.Size;

    protected override IDisposable? OnMounted()
    {
        _home.ObserveProblemsFor(Connecting ? InstanceId : null, "server-dialog");
        _home.StateChanged += OnHomeChanged;
        _window.SizeChanged += OnSizeChanged;
        if (Connecting)
            _ = LoadAsync();
        return PopupKeyboardClose.Bind(_window, () => _ = DismissAsync(), () => !Saving);
    }

    protected override void OnDispose()
    {
        _home.ObserveProblemsFor(null, "server-dialog");
        if (Connecting)
        {
            var accountId = AccountIndex > 0 && AccountIndex <= _accountsData.Length
                ? _accountsData[AccountIndex - 1].Id
                : AccountIndex < 0 || Loading ? _draftAccountId : null;
            _saveDraft(new(Address, InstanceId, accountId, Remember));
        }

        _lifetime.Cancel();
        _home.StateChanged -= OnHomeChanged;
        _window.SizeChanged -= OnSizeChanged;
        _focus.Dispose();
        _lifetime.Dispose();
        base.OnDispose();
    }
}

internal sealed record ServerAssociationData(
    string Id,
    string Name,
    bool Selected,
    bool Available,
    Action<bool> Changed,
    bool CanChange);

[View]
internal readonly partial record struct ServerAssociationRow(LauncherAppearance Theme, ServerAssociationData Data)
{
    internal Action Toggle
    {
        get
        {
            var data = Data;
            return () => data.Changed(!data.Selected);
        }
    }

    public Ui Body => Column([
        new LauncherCheckBox(
Theme,            Data.Name,
            Data.Selected,
            Toggle,
            Data.CanChange && (Data.Available || Data.Selected),
            "servers.association." + Data.Id).Margin(0f, 5f),
    ]);
}