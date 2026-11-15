// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Accounts;

internal sealed partial class AccountPopup : Control
{
    internal readonly AccountsFeature _feature;
    private readonly Action _close;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PopupFocusRecovery _focus;
    private readonly AccountPopupBody _body;
    private CancellationTokenSource? _operation;
    private bool _disposed;
    private int _reloadGeneration;
    internal IdentityRecord? _target;
    private MenuFlyout? _menu;

    internal AccountPopup(AccountsFeature feature, string page, Action close)
    {
        _feature = feature;
        _close = close;
        Page = page;
        Name = string.Empty;
        Status = string.Empty;
        ErrorCode = string.Empty;
        Accounts = [];
        CurrentId = string.Empty;
        _focus = new(feature.Window);
        _body = new(this);
    }

    public partial string Page { get; private set; }
    public partial string Name { get; set; }
    public partial string Status { get; private set; }
    public partial string ErrorCode { get; private set; }
    public partial bool Busy { get; private set; }
    /// <summary>Gets whether the current operation needs progress beyond disabled in-place account actions.</summary>
    public partial bool ShowProgress { get; private set; }
    /// <summary>Gets whether the account inventory has been read successfully, so an unavailable inventory is not shown as empty.</summary>
    public partial bool AccountsLoaded { get; private set; }
    public partial IdentityRecord[] Accounts { get; private set; }
    public partial string CurrentId { get; private set; }
    internal float ContentWidth => Math.Min(440f, _feature.Window.ClientSize.Width - 48f);
    private string Title => Page switch
    {
        "types" => Resources.AccountAdd,
        "offline" => Resources.AccountOffline,
        "remove" => string.Format(Resources.AccountRemoveTitle, _target?.Name),
        "login" => Resources.AccountMicrosoft,
        _ => Resources.AccountManage
    };

    private async Task ReloadAsync()
    {
        var generation = ++_reloadGeneration;
        try
        {
            var cancellation = _lifetime.Token;
            var selection = await Task.Run(() => _feature.Client.Identities.GetSelection(), cancellation);
            if (_disposed || generation != _reloadGeneration)
                return;
            Accounts = AccountsFeature.ReadAccounts(selection.Accounts);
            CurrentId = selection.AccountId ?? string.Empty;
            AccountsLoaded = true;
            _feature.RefreshSkins(Accounts);
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        catch
        {
            if (!_disposed && generation == _reloadGeneration)
                Status = Resources.AccountOperationFailed;
        }
    }

    private Ui Body => Column([
        Border(Row([
                LauncherControls.Command(
                    Row([
                        LauncherIcons.Line("back", 16f)
                    ]),
                    Back)
                    .Style(LauncherButtons.IconButton)
                    .Width(28f).Height(28f)
                    .Visible(!Busy && Page is not ("list" or "types"))
                    .Automation(new()
                    {
                        Name = Resources.AccountManage,
                        AutomationId = "accounts.back"
                    }),
                Text(Title)
                    .FontSize(16f).FontWeight(600).Foreground(LauncherTheme.Ink)
                    .Wrap()
                    .Weight(1f)
                    .VAlign(VerticalAlignment.Center),
                LauncherControls.Command(Resources.AccountAdd, () => _feature.Show("types"))
                    .Style(LauncherButtons.DialogPrimary)
                    .Enabled(!Busy)
                    .Visible(Page == "list")
                    .Automation(new()
                    {
                        Name = Resources.AccountAdd,
                        AutomationId = "accounts.add"
                    }),
                LauncherControls.OutlineCommand(
                    Row([
                        LauncherIcons.Line("close", 16f)
                    ]),
                    _close)
                    .Style(LauncherButtons.PopupCloseButton)
                    .Width(28f).Height(28f)
                    .Visible(!Busy && Page is not ("list" or "types"))
                    .ToolTip(Resources.CloseLaunchSettings)
                    .Automation(new()
                    {
                        Name = Resources.CloseLaunchSettings,
                        AutomationId = "accounts.close"
                    }),
            ])
                .Spacing(8f))
            .Padding(new Thickness(16f, 12f, 16f, 12f))
            .BorderBrush(LauncherTheme.Edge)
            .BorderThickness(0f, 0f, 0f, 1f),
        ScrollView(_body)
            .MaxHeight(Math.Max(100f, _feature.Window.ClientSize.Height - 180f))
            .Automation(new()
            {
                Name = Title,
                AutomationId = "accounts.scroll"
            }),
    ])
        .Width(ContentWidth);

    internal void ShowActions(IdentityRecord account)
    {
        var id = account.Id!;
        var items = new List<MenuFlyoutItemBase>();
        if (account.Type == "microsoft")
        {
            items.Add(new MenuFlyoutItem(Resources.AccountVerify, () => _ = RefreshAsync(id))
            {
                Icon = new SymbolIcon(Symbol.Refresh),
                Automation = new() { AutomationId = "accounts.verify" }
            });
            items.Add(new MenuFlyoutItem(Resources.AccountSignInAgain, () => SignInAgain(account))
            {
                Icon = new SymbolIcon(Symbol.Account),
                Automation = new() { AutomationId = "accounts.login-again" }
            });
        }
        else if (account.Type == "external")
        {
            items.Add(new MenuFlyoutItem(Resources.AccountVerify, () => _feature.ShowExternal(account, refresh: true))
            {
                Icon = new SymbolIcon(Symbol.Refresh),
                Automation = new() { AutomationId = "accounts.verify" }
            });
            items.Add(new MenuFlyoutItem(Resources.AccountSignInAgain, () => _feature.ShowExternal(account))
            {
                Icon = new SymbolIcon(Symbol.Account),
                Automation = new() { AutomationId = "accounts.login-again" }
            });
        }
        else if (account.Type == "offline")
        {
            items.Add(new MenuFlyoutItem(Resources.ImageAvatarTitle, () => _ = _feature.ShowAvatarAsync(account, true))
            {
                Icon = new SymbolIcon(Symbol.Edit),
                Automation = new()
                {
                    Name = Resources.ImageAvatarTitle,
                    AutomationId = "accounts.avatar." + id
                }
            });
        }

        items.Add(new MenuFlyoutItem(Resources.AccountRemove, () =>
                {
                    _target = account;
                    Page = "remove";
                    Status = string.Empty;
                    ErrorCode = string.Empty;
                    _focus.Request(this);
                })
        {
            Icon = new SymbolIcon(Symbol.Delete) { Foreground = LauncherTheme.Error },
            Automation = new() { AutomationId = "accounts.remove" }
        });
        _menu = new MenuFlyout([.. items]).Style(LauncherMenus.CommandMenu);
        _menu.ShowAt(_feature.Window);
    }

    private async Task RunAsync(Func<CancellationToken, Task> action, bool showProgress = true)
    {
        if (Busy || _disposed)
            return;
        _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        using var work = _feature.TrackWork();
        ShowProgress = showProgress;
        Busy = true;
        Status = string.Empty;
        ErrorCode = string.Empty;
        _focus.Request(this);
        try
        {
            await action(_operation.Token);
            if (!_disposed)
            {
                await ReloadAsync();
                _feature.NotifyChanged();
            }
        }
        catch (OperationCanceledException)
        {
            if (!_disposed)
            {
                Status = string.Empty;
                ErrorCode = string.Empty;
                await ReloadAsync();
                if (!_disposed)
                    Page = "list";
            }
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                await ReloadAsync();
                if (_disposed)
                    return;
                ErrorCode = ex is HttpRequestException ? "AUTH_NETWORK_ERROR" : (ex as LauncherOperationException)?.Code ?? "ACCOUNT_FAILED";
                Status = ErrorCode switch
                {
                    "AUTH_NOT_CONFIGURED" => Resources.AccountNotConfigured,
                    "AUTH_GAME_ACCESS_UNAVAILABLE" or "AUTH_PROFILE_UNAVAILABLE" => Resources.AccountAccessUnavailable,
                    "AUTH_PROFILE_REQUIRED" => Resources.AccountProfileRequired,
                    "AUTH_LOGIN_REQUIRED" or "AUTH_CACHE_UNAVAILABLE" => Resources.AccountSignInRequired,
                    "AUTH_NETWORK_ERROR" or "AUTH_TIMEOUT" => Resources.AccountNetworkUnavailable,
                    "AUTH_IDENTITY_MISMATCH" => Resources.AccountWrongProfile,
                    _ => Page == "login" || ErrorCode.StartsWith("AUTH_", StringComparison.Ordinal) ? Resources.AccountAuthFailed : Resources.AccountOperationFailed
                };
                if (ErrorCode == "AUTH_NOT_CONFIGURED")
                {
                    var message = Status;
                    Controls.LauncherToast.ShowError(_feature.Window, message);
                }
            }
        }
        finally
        {
            if (!_disposed)
            {
                Busy = false;
                _focus.RequestIfLost(this);
            }

            _operation.Dispose();
            _operation = null;
        }
    }

    internal Task AddOfflineAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Status = Resources.AccountNameRequired;
            ErrorCode = "NAME_REQUIRED";
            return Task.CompletedTask;
        }

        return RunAsync(async ct =>
            {
                var account = await _feature.Client.Identities.AddOfflineAsync(Name.Trim(), ct);
                await _feature.Client.Identities.SelectAsync(account.Id!, ct);
                if (!_disposed)
                    Page = "list";
            });
    }

    internal Task SelectAsync(string id) => RunAsync(ct => _feature.Client.Identities.SelectAsync(id, ct), showProgress: false);

    internal Task RemoveAsync() => RunAsync(async ct =>
        {
            await _feature.Client.Identities.RemoveAsync(_target!.Id!, ct);
            if (!_disposed)
                Page = "list";
        });

    internal Task RefreshAsync(string id) => RunAsync(async ct => await _feature.Client.Identities.RefreshMicrosoftAsync(id, ct));

    private void SignInAgain(IdentityRecord account)
    {
        _target = account;
        Page = "login";
        _ = LoginAsync(MicrosoftLoginMethod.System);
    }

    internal Task LoginAsync(MicrosoftLoginMethod method) => RunAsync(async ct =>
        {
            await _feature.Client.Identities.LoginMicrosoftAsync(new()
            {
                Method = method,
                ParentWindow = _feature.Window.Handle,
                AccountId = _target?.Id
            }, ct);
            if (!_disposed)
            {
                Page = "list";
                Status = string.Empty;
            }
        });

    internal Task RetryAsync() => RunAsync(async ct =>
        {
            try
            {
                await _feature.Client.Identities.RetryMicrosoftLoginAsync(_target?.Id, ct);
            }
            catch (LauncherOperationException ex) when (ex.Code == "AUTH_LOGIN_REQUIRED")
            {
                await _feature.Client.Identities.LoginMicrosoftAsync(new()
                {
                    Method = MicrosoftLoginMethod.System,
                    ParentWindow = _feature.Window.Handle,
                    AccountId = _target?.Id
                }, ct);
            }

            if (!_disposed)
            {
                Page = "list";
                Status = string.Empty;
            }
        });

    internal void CancelLogin() => _operation?.Cancel();

    private void Back()
    {
        if (Busy)
        {
            CancelLogin();
        }
        else
        {
            Page = "list";
            ErrorCode = string.Empty;
            Status = string.Empty;
            _focus.Request(this);
        }
    }

    protected override IDisposable? OnMounted()
    {
        _feature.Skins.Updated += OnSkinUpdated;
        _ = ReloadAsync();
        if (Page == "login")
            _ = LoginAsync(MicrosoftLoginMethod.System);
        return PopupKeyboardClose.Bind(_feature.Window, () =>
            {
                if (Busy)
                    CancelLogin();
                else
                    _close();
            }, () => true);
    }

    protected override void OnDispose()
    {
        _disposed = true;
        _feature.Skins.Updated -= OnSkinUpdated;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _menu?.Hide();
        _focus.Dispose();
        base.OnDispose();
    }

    private void OnSkinUpdated(IdentityRecord account) => _ = ReloadAsync();

}