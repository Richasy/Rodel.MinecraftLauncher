// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Images;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Popups;

namespace Rodel.MinecraftLauncher.App.Features.Accounts;

internal sealed class AccountsFeature(LauncherClient client, Window window, Action showSkinEditor, Action showSeedExplorer) : IDisposable
{
    private Flyout? _popup;
    private MenuFlyout? _menu;
    private bool _disposed;
    private bool _openingMenu;
    private int _selectionGeneration;
    private int _activeWork;
    private CancellationTokenSource? _menuImages;
    internal bool HasActiveWork => Volatile.Read(ref _activeWork) > 0;
    internal event Action? Changed;
    internal IdentityRecord? CurrentAccount { get; private set; }
    internal bool Loaded { get; private set; }
    internal string CurrentName { get => field.Length > 0 ? field : Resources.AccountNoSelection; private set; } = string.Empty;

    internal static IdentityRecord[] OrderAccounts(IEnumerable<IdentityRecord> accounts) => [..accounts.OrderBy(a => a.Type == "microsoft"
                ? 0
                : a.Type == "external" ? 1 : 2)];

    internal static string TypeLabel(IdentityRecord account) => account.Type == "microsoft"
        ? Resources.AccountMicrosoft
        : account.Type == "external" ? string.Format(Resources.ExternalSourceType, account.AuthServerName ?? Resources.AccountExternal) : Resources.AccountOffline;

    internal static IdentityRecord[] ReadAccounts(IEnumerable<IdentityEntry> entries) => OrderAccounts(entries.Select(entry => entry.Identity).OfType<IdentityRecord>());
    internal LauncherClient Client => client;
    internal Window Window => window;
    internal AccountSkinRefresh Skins { get; } = new(client.Identities.RefreshSkinAsync);
    internal AccountAvatarImages Avatars { get; } = new();

    internal void Start()
    {
        window.Activated += OnActivated;
        Skins.Updated += OnSkinUpdated;
        NotifyChanged();
    }

    private void OnActivated(object? sender, EventArgs args) => NotifyChanged();
    private void OnSkinUpdated(IdentityRecord account)
    {
        var current = AccountSkinRefresh.SameRole(CurrentAccount, account);
        if (current)
            CurrentAccount = account;
        Avatars.Refresh(account);
        if (current)
            Changed?.Invoke();
    }

    internal void RefreshSkins(IEnumerable<IdentityRecord> accounts)
    {
        foreach (var account in accounts)
            _ = Skins.RefreshAsync(account);
    }

    internal IDisposable TrackWork()
    {
        Interlocked.Increment(ref _activeWork);
        return new WorkLease(() => Interlocked.Decrement(ref _activeWork));
    }

    private sealed class WorkLease(Action end) : IDisposable
    {
        public void Dispose() => end();
    }

    internal void ShowMenu() => _ = ShowMenuAsync();

    private async Task ShowMenuAsync()
    {
        if (_disposed || _openingMenu || ModalPopup.IsOpen(window))
            return;
        _openingMenu = true;
        try
        {
            var selection = await Task.Run(() => client.Identities.GetSelection());
            if (_disposed || ModalPopup.IsOpen(window))
                return;
            var current = selection.AccountId;
            var accounts = ReadAccounts(selection.Accounts);
            _menuImages?.Cancel();
            _menuImages?.Dispose();
            _menuImages = new();
            var items = new List<MenuFlyoutItemBase>();
            string? previousType = null;
            foreach (var account in accounts)
            {
                var type = account.Type;
                if (previousType is not null && (type != previousType))
                    items.Add(new MenuFlyoutSeparator());
                previousType = type;
                var id = account.Id!;
                var name = account.Name ?? string.Empty;
                var label = string.Format(Resources.AccountMenuEntry, name, TypeLabel(account));
                items.Add(new AccountMenuItem(account, id == current, () => _ = SelectAsync(id), Skins, Avatars, _menuImages?.Token ?? CancellationToken.None)
                {
                    Automation = new()
                    {
                        Name = label,
                        AutomationId = "accounts.select." + id
                    }
                });
            }

            if (items.Count > 0)
                items.Add(new MenuFlyoutSeparator());
            if (accounts.FirstOrDefault(a => a.Id == current && a.Type == "offline") is { } local)
            {
                items.Add(new MenuFlyoutItem(Resources.ImageAvatarTitle, () => _ = ShowAvatarAsync(local, false))
                {
                    Icon = new SymbolIcon(Symbol.Edit),
                    Automation = new()
                    {
                        Name = Resources.ImageAvatarTitle,
                        AutomationId = "accounts.avatar"
                    }
                });
            }

            items.Add(new MenuFlyoutItem(Resources.AccountManage, () => Show("list"))
            {
                Icon = new SymbolIcon(Symbol.Setting),
                Automation = new() { AutomationId = "accounts.manage" }
            });
            items.Add(new MenuFlyoutItem(Resources.AccountAdd, () => Show("types"))
            {
                Icon = new SymbolIcon(Symbol.Add),
                Automation = new() { AutomationId = "accounts.add" }
            });
            items.Add(new MenuFlyoutSeparator());
            items.Add(new MenuFlyoutItem(Resources.SkinEditorTitle, showSkinEditor)
            {
                Icon = new SkinEditorMenuIcon(),
                Automation = new() { AutomationId = "accounts.skin-editor" }
            });
            items.Add(new MenuFlyoutItem(Resources.SeedTitle, showSeedExplorer)
            {
                Icon = new SeedExplorerMenuIcon(),
                Automation = new() { AutomationId = "accounts.seed-explorer" }
            });
            _menu = new MenuFlyout([.. items])
            {
                Placement = PopupPlacement.RightEdgeAlignedTop
            }.Style(LauncherMenus.CommandMenu).Width(Math.Min(360f, window.ClientSize.Width - 32f)).MaxHeight(Math.Max(120f, window.ClientSize.Height - 100f));
            var images = _menuImages!;
            _menu.Closed += () =>
            {
                if (!images.IsCancellationRequested)
                    images.Cancel();
            };
            _menu.ShowAt(window);
            RefreshSkins(accounts);
        }
        catch
        {
            if (!_disposed)
                LauncherToast.ShowError(window, Resources.AccountOperationFailed);
        }
        finally
        {
            _openingMenu = false;
        }
    }

    internal async Task ShowAvatarAsync(IdentityRecord account, bool returnToList)
    {
        // Let the invoking menu complete its own close before replacing it with a modal popup.
        await Task.Yield();
        if (_disposed || account.Type != "offline" || account.Id is null)
            return;
        _menu?.Hide();
        if (_popup is { IsOpen: true } old)
        {
            var closed = new TaskCompletionSource();
            void Closed() => closed.TrySetResult();
            old.Closed += Closed;
            old.Hide();
            try
            {
                await closed.Task;
            }
            finally
            {
                old.Closed -= Closed;
            }
        }

        if (_disposed || ModalPopup.IsOpen(window))
            return;
        AccountAvatarPreference preference;
        string? error = null;
        try
        {
            preference = await Task.Run(() => AccountAvatarPreferences.Store.Read(account.Id));
        }
        catch (Exception)
        {
            preference = new();
            error = Resources.ImageReadPreferenceError;
        }

        if (_disposed || ModalPopup.IsOpen(window))
            return;
        ImageChoicePopup? content = null;
        var popup = new Flyout(close => content = new ImageChoicePopup(
                window,
                account.Name ?? string.Empty,
                true,
                preference.Preset,
                AccountAvatarPreferences.Source(preference),
                async (choice, ct) =>
                {
                    // Recheck identity ownership after the file picker and asynchronous image encoding.
                    if (client.Identities.Get(account.Id).Type != "offline")
                        throw new InvalidOperationException();
                    if (choice.Preset is { } preset)
                        AccountAvatarPreferences.Store.SavePreset(account.Id, preset);
                    else
                        await AccountAvatarPreferences.Store.SaveCustomAsync(account.Id, choice.Png ?? throw new InvalidOperationException(), ct);
                    AccountAvatarPreferences.Notify(account.Id);
                    NotifyChanged();
                },
                close,
                error))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.MaxWidth(440f).Padding(0f)).Automation(new()
        {
            Name = Resources.ImageAvatarTitle,
            AutomationId = "accounts.avatar.popup",
            IsDialog = true
        });
        popup.Closing += e => e.Cancel = content?.CanClose == false;
        popup.Closed += () =>
        {
            if (ReferenceEquals(_popup, popup))
                _popup = null;
            if (!_disposed)
            {
                if (returnToList)
                    Show("list");
                else
                    ShowMenu();
            }
        };
        _popup = popup;
        ModalPopup.TryShow(window, popup);
    }

    internal async Task SelectAsync(string id)
    {
        using var work = TrackWork();
        try
        {
            await Task.Run(() => client.Identities.SelectAsync(id));
            await RefreshAsync();
        }
        catch
        {
            LauncherToast.ShowError(window, Resources.AccountOperationFailed);
        }
    }

    internal void NotifyChanged() => _ = RefreshAsync();

    internal async Task<string?> EnsureLaunchIdentityAsync(CancellationToken ct)
    {
        var selection = await Task.Run(() => client.Identities.GetSelection(), ct);
        ct.ThrowIfCancellationRequested();
        if (selection.Accounts.Any(entry => entry.Identity?.Id == selection.AccountId && selection.AccountId is not null))
            return selection.AccountId;
        if (_disposed || ModalPopup.IsOpen(window))
            return null;
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Selected()
        {
            if (CurrentAccount?.Id is { } id)
                completion.TrySetResult(id);
        }

        Changed += Selected;
        Show(selection.Accounts.Any(entry => entry.Identity is not null) ? "list" : "offline");
        var popup = _popup;
        if (popup is null || !popup.IsOpen)
        {
            Changed -= Selected;
            return null;
        }

        void Closed()
        {
            completion.TrySetResult(null);
            closed.TrySetResult();
        }

        popup.Closed += Closed;
        try
        {
            using var cancellation = ct.Register(() => completion.TrySetCanceled(ct));
            var accountId = await completion.Task;
            popup.Hide();
            await closed.Task.WaitAsync(ct);
            return accountId;
        }
        finally
        {
            Changed -= Selected;
            popup.Closed -= Closed;
            popup.Hide();
        }
    }

    private async Task RefreshAsync()
    {
        var generation = ++_selectionGeneration;
        try
        {
            var selection = await Task.Run(() => client.Identities.GetSelection());
            if (_disposed || generation != _selectionGeneration)
                return;
            CurrentAccount = selection.Accounts.Select(entry => entry.Identity).FirstOrDefault(a => a?.Id == selection.AccountId);
            Avatars.Retain(selection.Accounts.Select(entry => entry.Identity).OfType<IdentityRecord>());
            CurrentName = CurrentAccount?.Name ?? string.Empty;
            Loaded = true;
            Changed?.Invoke();
            _ = Skins.RefreshAsync(CurrentAccount);
        }
        catch
        {
            // Keep the last usable identity; an initial failure still ends the loading placeholder.
            if (!_disposed && generation == _selectionGeneration && !Loaded)
            {
                Loaded = true;
                Changed?.Invoke();
            }
        }
    }

    internal void Show(string page)
    {
        if (page == "external")
        {
            ShowExternal();
            return;
        }

        if (_disposed)
            return;
        if (_popup?.IsOpen == true)
            _popup.Hide();
        if (ModalPopup.IsOpen(window))
            return;
        AccountPopup? content = null;
        var popup = new Flyout(close => content = new AccountPopup(this, page, close))
        {
            Placement = PopupPlacement.Full,
            IsModal = false,
            IsLightDismissEnabled = true
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.MaxWidth(440f).Padding(0f));
        popup.Automation(new()
        {
            Name = page == "types" ? Resources.AccountAdd : Resources.AccountManage,
            AutomationId = "accounts.popup",
            IsDialog = true
        });
        popup.Closing += e => e.Cancel = content?.Busy == true;
        popup.Closed += () =>
        {
            if (ReferenceEquals(_popup, popup))
                _popup = null;
        };
        _popup = popup;
        ModalPopup.TryShow(window, popup);
    }

    internal void ShowExternal(IdentityRecord? account = null, bool refresh = false) => _ = ShowExternalAsync(account, refresh);

    private async Task ShowExternalAsync(IdentityRecord? account, bool refresh)
    {
        await Task.Yield();
        if (_disposed)
            return;
        _menu?.Hide();
        if (_popup is { IsOpen: true } previous)
        {
            var closed = new TaskCompletionSource();
            void Closed() => closed.TrySetResult();
            previous.Closed += Closed;
            previous.Hide();
            try
            {
                await closed.Task;
            }
            finally
            {
                previous.Closed -= Closed;
            }
        }

        if (_disposed || ModalPopup.IsOpen(window))
            return;
        var popup = new Flyout(close => new ExternalAccountPopup(this, account, refresh, close))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.MaxWidth(440f).Padding(0f)).Automation(new()
        {
            Name = Resources.AccountExternal,
            AutomationId = "accounts.external.popup",
            IsDialog = true
        });
        popup.Closed += () =>
        {
            if (ReferenceEquals(_popup, popup))
                _popup = null;
        };
        _popup = popup;
        ModalPopup.TryShow(window, popup);
    }

    /// <summary>Detaches window events and ends account popups and optional skin queries.</summary>
    public void Dispose()
    {
        _disposed = true;
        window.Activated -= OnActivated;
        Skins.Updated -= OnSkinUpdated;
        Skins.Dispose();
        _menuImages?.Cancel();
        _menuImages?.Dispose();
        _popup?.Hide();
        _menu?.Hide();
    }
}