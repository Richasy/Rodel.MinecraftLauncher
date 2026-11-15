// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Popups;

namespace Rodel.MinecraftLauncher.App.Features.Servers;

/// <summary>One connection/editor surface shared by the home shortcuts and instance management.</summary>
internal sealed class ServerConnectionPresenter(
    LauncherClient client,
    HomeController home,
    Window window,
    Action showLocal) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private Task<ServerFavorite[]>? _favorites;
    private long _favoritesRevision = -1;
    private bool _openingRecovery;
    internal ServerStatusMonitor Statuses { get; } = new(client);
    private readonly Dictionary<string, ServerConnectionDraft> _drafts = [with(StringComparer.Ordinal)];
    internal event Action? Changed;
    internal event Action<string, string>? ClientSelected;
    internal Action<Core.Application.AI.AiRepairCreate>? RepairRequested { get; set; }
    internal Action<IdentityRecord?>? ExternalSignIn { get; set; }

    internal Task<ServerFavorite[]> ReadFavoritesAsync(CancellationToken cancellationToken, bool force = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (force || _favoritesRevision != home.State.InstancesRevision || _favorites is null || _favorites.IsFaulted || _favorites.IsCanceled)
        {
            _favoritesRevision = home.State.InstancesRevision;
            var lifetime = _lifetime.Token;
            _favorites = Task.Run(async () =>
            {
                var records = await client.Servers.ListAsync(lifetime).ConfigureAwait(false);
                return records.Select(entry => entry.Favorite).OfType<Core.Servers.ServerFavorite>().Select(ServerFavorite.Read).ToArray();
            }, lifetime);
        }

        return _favorites.WaitAsync(cancellationToken);
    }

    internal void ShowRecovery(string instanceId, ServerFavorite? favorite = null, string? server = null) => _ = ShowRecoveryAsync(instanceId, favorite, server);

    private async Task ShowRecoveryAsync(string instanceId, ServerFavorite? favorite, string? server)
    {
        if (_openingRecovery || _lifetime.IsCancellationRequested || ModalPopup.IsOpen(window))
            return;
        _openingRecovery = true;
        var cancellation = _lifetime.Token;
        try
        {
            var address = server ?? favorite?.Address;
            var launch = home.State.LaunchStates.GetValueOrDefault(instanceId);
            if (address is not null && launch?.ServerAddress != address)
                launch = null;
            try
            {
                if (launch is not null && ((server ?? favorite?.Address) is null || launch.ServerAddress == (server ?? favorite?.Address)))
                {
                    var recovery = await Task.Run(() => client.GetExternalLaunchRecovery(instanceId, launch.SessionId, launch.JobId), cancellation);
                    if (cancellation.IsCancellationRequested || ModalPopup.IsOpen(window))
                        return;
                    if (recovery is not null)
                    {
                        var external = new Flyout(close => new Features.Accounts.ExternalLaunchPopup(
                                client,
                                home,
                                window,
                                recovery,
                                close,
                                ExternalSignIn))
                        {
                            Placement = PopupPlacement.Full,
                            IsModal = true,
                            IsLightDismissEnabled = false
                        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.MaxWidth(440f).Padding(0f)).Automation(new()
                        {
                            Name = Resources.ExternalVerifyFailed,
                            AutomationId = "external-launch.popup",
                            IsDialog = true
                        });
                        ModalPopup.TryShow(window, external);
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
            { /* The normal report retains unreadable recovery evidence. */
            }

            if (cancellation.IsCancellationRequested || ModalPopup.IsOpen(window))
                return;
            LaunchRecoveryDialog? content = null;
            var popup = new Flyout(close => content ??= new LaunchRecoveryDialog(
                    client,
                    home,
                    window,
                    instanceId,
                    address,
                    address is not null ? Statuses.Values.GetValueOrDefault(address)?.Value : null,
                    close,
                    RepairRequested,
                    launch,
                    address is null && home.State.SelectedId == instanceId ? home.State.ActionError : null))
            {
                Placement = PopupPlacement.Full,
                IsModal = true,
                IsLightDismissEnabled = false
            }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.MaxWidth(480f).Padding(0f)).Automation(new()
            {
                Name = Resources.ProblemOriginalHeading,
                AutomationId = "recovery.popup",
                IsDialog = true
            });
            popup.Closing += args => args.Cancel = content?.CanClose == false;
            ModalPopup.TryShow(window, popup);
        }
        finally
        {
            _openingRecovery = false;
        }
    }

    internal void Show(
        ServerAction action,
        ServerFavorite? favorite = null,
        ServerStatus? status = null,
        Action? closed = null,
        string? instanceId = null)
    {
        if (ModalPopup.IsOpen(window))
            return;
        ServerDialog? content = null;
        Action? afterClose = null;
        var key = favorite?.Id ?? string.Empty;
        var draft = _drafts.GetValueOrDefault(key);
        if (instanceId is not null)
            draft = new(favorite?.Address ?? draft?.Address ?? string.Empty, instanceId, draft?.AccountId, draft?.Remember == true);
        var popup = new Flyout(close => content ??= new ServerDialog(
                client,
                home,
                window,
                action,
                favorite,
                status,
                close,
                () =>
                {
                    if (action is not (ServerAction.Connect or ServerAction.Direct))
                        _drafts.Remove(key);
                    _favorites = null;
                    Changed?.Invoke();
                },
                () =>
                {
                    afterClose = showLocal;
                    close();
                },
                draft,
                saved => _drafts[key] = saved,
                targetId =>
                {
                    var address = content?.CurrentAddress;
                    afterClose = () => ShowRecovery(targetId, favorite, address);
                    close();
                },
                targetId =>
                {
                    if (favorite is not null)
                        ClientSelected?.Invoke(favorite.Id, targetId);
                },
                request =>
                {
                    afterClose = () => _ = ConnectAsync(request);
                    close();
                }))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.MaxWidth(480f).Padding(0f)).Automation(new()
        {
            Name = favorite?.Name ?? Resources.ServersTab,
            AutomationId = "servers.dialog",
            IsDialog = true
        });
        popup.Closing += args =>
        {
            args.Cancel = content?.CanClose == false;
            if (args.Cancel)
                content?.RequestClose();
        };
        popup.Closed += () =>
        {
            closed?.Invoke();
            if (afterClose is { } next)
                window.RequestAnimationFrame(_ => next());
        };
        ModalPopup.TryShow(window, popup);
    }

    private async Task ConnectAsync(LaunchTask request)
    {
        try
        {
            await home.StartLaunchAsync(request);
        }
        catch (Exception exception)
        {
            home.ReportError(request.InstanceId, exception.Message);
        }
    }

    /// <summary>Cancels pending recovery reads and stops shared server observation when the shell closes.</summary>
    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        Statuses.Dispose();
    }
}