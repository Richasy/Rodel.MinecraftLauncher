// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Popups;
using Sprout.WindowsAppSDK;
using System.Diagnostics;

namespace Rodel.MinecraftLauncher.App.Features.Accounts;

/// <summary>Coordinates the approved external-account flow through Core; the view never owns tokens or protocol requests.</summary>
internal sealed partial class ExternalAccountPopup : Control
{
    internal readonly AccountsFeature Feature;
    internal readonly IdentityRecord? Target;
    private readonly Action _close;
    private bool _refresh;
    private readonly CancellationTokenSource _lifetime;
    private readonly PopupFocusRecovery _focus;
    private readonly ExternalAccountBody _body;
    private Action? _cancelOperation;
    private Task _running = Task.CompletedTask;
    private string? _attemptId;
    private bool _disposed;
    private Flyout? _details;
    internal partial string Page { get; private set; }
    internal partial string Address { get; set; }
    internal partial string Username { get; set; }
    internal partial string Password { get; set; }
    internal partial bool Busy { get; private set; }
    internal partial string ErrorCode { get; private set; }
    internal partial string ErrorTitle { get; private set; }
    internal partial string ErrorDetail { get; private set; }
    internal partial string Original { get; private set; }
    internal partial ExternalLoginResult? Attempt { get; private set; }
    internal partial string SelectedRole { get; set; }
    internal partial bool LittleSkinSource { get; private set; }
    internal partial bool Copied { get; private set; }
    internal partial bool CopyFailed { get; private set; }
    internal string Activity => _refresh ? Resources.ExternalVerifying : Resources.ExternalSigningIn;
    internal string Source => Attempt?.Provider.Name ?? Target?.AuthServerName ?? (LittleSkinSource
        ? "LittleSkin"
        : Address.Trim().Length > 0 ? Address.Trim() : Resources.ExternalOther);
    internal bool Waiting => Busy && Page != "device";
    internal bool DeviceEnded => ErrorCode is "AUTH_CODE_EXPIRED" or "AUTH_ACCESS_DENIED";
    internal bool DeviceRetry => !Busy && ErrorCode is "AUTH_NETWORK_ERROR" or "AUTH_TIMEOUT" or "AUTH_TLS_ERROR" or "AUTH_RESPONSE_INVALID" or "AUTH_REMOTE_ERROR" or "AUTH_RATE_LIMITED";
    internal string Title => Page switch
    {
        "device" => Resources.ExternalLittleSkinLogin,
        "password" when LittleSkinSource => Resources.ExternalLittleSkinLogin,
        "roles" => Resources.ExternalSelectRole,
        "empty" => Resources.ExternalNoRolesTitle,
        "waiting" => _refresh
            ? Resources.AccountVerify
            : LittleSkinSource
            ? Resources.ExternalLittleSkinLogin
            : Target is not null ? Resources.AccountSignInAgain : Resources.ExternalAdd,
        "refresh" => Resources.ExternalVerifyFailed,
        "deleted" => Resources.ExternalRoleRemoved,
        _ => Target is not null ? Resources.AccountSignInAgain : Resources.ExternalAdd
    };

    internal ExternalAccountPopup(
        AccountsFeature feature,
        IdentityRecord? target,
        bool refresh,
        Action close)
    {
        Feature = feature;
        Target = target;
        _close = close;
        _refresh = refresh;
        _focus = Own(new PopupFocusRecovery(feature.Window));
        _lifetime = Own(new CancellationTokenSource());
        Page = refresh
            ? "waiting"
            : target?.AuthMethod == "littleskin"
            ? "device"
            : target is not null ? "password" : "sources";
        Address = target?.AuthServer ?? string.Empty;
        Username = string.Empty;
        Password = string.Empty;
        SelectedRole = string.Empty;
        LittleSkinSource = target?.AuthServer == "https://littleskin.cn/api/yggdrasil/";
        ErrorCode = string.Empty;
        ErrorTitle = string.Empty;
        ErrorDetail = string.Empty;
        Original = string.Empty;
        _body = Own(new ExternalAccountBody(this));
    }

    private Ui Body => _body;

    internal void PasswordPage()
    {
        _refresh = false;
        LittleSkinSource = Target?.AuthServer == "https://littleskin.cn/api/yggdrasil/";
        Address = Target?.AuthServer ?? string.Empty;
        Username = string.Empty;
        Password = string.Empty;
        SelectedRole = string.Empty;
        Attempt = null;
        Page = "password";
        ClearError();
        _focus.Request(this);
    }

    private void ClearError()
    {
        ErrorCode = string.Empty;
        ErrorTitle = string.Empty;
        ErrorDetail = string.Empty;
        Original = string.Empty;
    }

    internal void Login() => _running = RunAsync(async ct =>
        {
            await DiscardAttemptAsync();
            Attempt = null;
            return await Feature.Client.Identities.LoginExternalAsync(new()
            {
                Address = Address,
                Username = Username,
                Password = Password,
                AccountId = Target?.Id
            }, ct);
        }, "waiting");

    internal void LittleSkin()
    {
        _refresh = false;
        LittleSkinSource = true;
        Address = Target?.AuthServer ?? "https://littleskin.cn/api/yggdrasil/";
        _running = RunAsync(async ct =>
            {
                await DiscardAttemptAsync();
                var result = await Feature.Client.Identities.BeginLittleSkinAsync(Target?.Id, ct: ct);
                _attemptId = result.AttemptId;
                ct.ThrowIfCancellationRequested();
                if (!_disposed)
                {
                    Attempt = result;
                    Page = "device";
                    CopyCode();
                    _focus.Request(this);
                }

                return await PollAsync(result, ct);
            }, "waiting");
    }

    private async Task<ExternalLoginResult> PollAsync(ExternalLoginResult result, CancellationToken ct)
    {
        do
        {
            result = await Feature.Client.Identities.PollLittleSkinAsync(result.AttemptId, ct);
            if (!_disposed && !ct.IsCancellationRequested)
                Attempt = result;
        }
        while (result.Stage == "device");
        return result;
    }

    internal void RetryDevice()
    {
        if (DeviceEnded || Attempt is null)
        {
            LittleSkin();
            return;
        }

        var attempt = Attempt;
        _running = RunAsync(ct => PollAsync(attempt, ct), "device");
    }

    internal void SelectRole()
    {
        if (Attempt is null || SelectedRole.Length == 0)
            return;
        var attempt = Attempt.AttemptId;
        var role = SelectedRole;
        _running = RunAsync(ct => Feature.Client.Identities.SelectExternalProfileAsync(attempt, role, ct), "waiting");
    }

    internal void RereadRoles()
    {
        if (Attempt?.UserCode is not null)
            RetryDevice();
        else
            Login();
    }

    internal void Refresh() => _running = RunAsync(async ct =>
        {
            await Feature.Client.Identities.RefreshExternalAsync(Target!.Id!, ct);
            return new()
            {
                AttemptId = string.Empty,
                Provider = new(Target.AuthServer!, Target.AuthServerName ?? string.Empty),
                Stage = "complete",
                Account = Feature.Client.Identities.Get(Target.Id!)
            };
        }, "waiting");

    private async Task RunAsync(Func<CancellationToken, Task<ExternalLoginResult>> operation, string waitingPage)
    {
        if (Busy || _disposed)
            return;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _cancelOperation = cancellation.Cancel;
        using var work = Feature.TrackWork();
        Busy = true;
        ClearError();
        Page = waitingPage;
        _focus.Request(this);
        try
        {
            var result = await operation(cancellation.Token);
            _attemptId = result.Stage == "complete" ? null : result.AttemptId;
            cancellation.Token.ThrowIfCancellationRequested();
            if (_disposed)
                return;
            Attempt = result;
            SelectedRole = string.Empty;
            if (result.Stage == "complete")
            {
                Password = string.Empty;
                Feature.NotifyChanged();
                Feature.Show("list");
            }
            else
            {
                Page = result.Stage == "no-roles" ? "empty" : "roles";
                _focus.Request(this);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed && !cancellation.IsCancellationRequested)
            {
                var problem = ex as LauncherOperationException;
                if (problem?.Code == "AUTH_CLIENT_UNAVAILABLE" && LittleSkinSource)
                {
                    await DiscardAttemptAsync();
                    if (_disposed || cancellation.IsCancellationRequested)
                        return;
                    _refresh = false;
                    Attempt = null;
                    SelectedRole = string.Empty;
                    Password = string.Empty;
                    Address = Target?.AuthServer ?? "https://littleskin.cn/api/yggdrasil/";
                    ClearError();
                    Page = "password";
                    _focus.Request(this);
                    return;
                }

                ErrorCode = problem?.Code ?? "AUTH_REMOTE_ERROR";
                ErrorTitle = ErrorCode switch
                {
                    "AUTH_PASSWORD_INVALID" => Resources.ExternalPasswordError,
                    "AUTH_TLS_ERROR" => Resources.ExternalTlsError,
                    "AUTH_RESPONSE_INVALID" => Resources.ExternalResponseError,
                    "AUTH_RATE_LIMITED" => Resources.ExternalRateError,
                    "AUTH_CODE_EXPIRED" => Resources.ExternalCodeExpired,
                    "AUTH_ACCESS_DENIED" => Resources.ExternalDenied,
                    "AUTH_CLIENT_UNAVAILABLE" => Resources.ExternalClientUnavailable,
                    "AUTH_PROFILE_REMOVED" => Resources.ExternalRoleRemoved,
                    "AUTH_LOGIN_REQUIRED" or "AUTH_CACHE_UNAVAILABLE" => Resources.AccountSignInRequired,
                    "AUTH_NETWORK_ERROR" or "AUTH_TIMEOUT" => Resources.ExternalNetworkError,
                    _ => Resources.ExternalRemoteError
                };
                ErrorDetail = ErrorCode switch
                {
                    "AUTH_PASSWORD_INVALID" => Resources.ExternalPasswordHint,
                    "AUTH_NETWORK_ERROR" or "AUTH_TIMEOUT" => Resources.ExternalNetworkHint,
                    "AUTH_TLS_ERROR" => Resources.ExternalTlsHint,
                    "AUTH_RESPONSE_INVALID" => Resources.ExternalResponseHint,
                    "AUTH_RATE_LIMITED" => Resources.ExternalRateHint,
                    "AUTH_CODE_EXPIRED" => Resources.ExternalCodeExpiredHint,
                    "AUTH_CLIENT_UNAVAILABLE" => Resources.ExternalClientHint,
                    _ => ex.Message
                };
                Original = problem?.Details?["original"]?.ToString() ?? ex.Message;
                Page = ErrorCode == "AUTH_PROFILE_REMOVED"
                    ? "deleted"
                    : _refresh && ErrorCode is "AUTH_LOGIN_REQUIRED" or "AUTH_CACHE_UNAVAILABLE"
                    ? "reauth"
                    : _refresh
                    ? "refresh"
                    : Attempt?.Stage == "device" ? "device" : "password";
                _focus.Request(this);
            }
        }
        finally
        {
            if (!_disposed)
                Busy = false;
            _cancelOperation = null;
            cancellation.Dispose();
        }
    }

    internal void CopyCode()
    {
        if (Attempt?.UserCode is not { } code || DeviceEnded)
            return;
        Copied = Sprout.Host.Clipboard.SetText(code);
        CopyFailed = !Copied;
    }

    internal void OpenAuthorization()
    {
        var address = Attempt?.VerificationUriComplete ?? Attempt?.VerificationUri;
        if (address is null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
        }
        catch (Exception)
        {
            ErrorTitle = Resources.ExternalBrowserError;
            ErrorDetail = Resources.ExternalBrowserHint;
            ErrorCode = "AUTH_BROWSER_FAILED";
        }
    }

    internal void OpenRoleManager()
    {
        var address = Attempt?.Provider.ApiRoot ?? Target?.AuthServer;
        if (address is null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(new Uri(address).GetLeftPart(UriPartial.Authority)) { UseShellExecute = true });
        }
        catch (Exception)
        {
            LauncherToast.ShowError(Feature.Window, Resources.ExternalBrowserError);
        }
    }

    internal void ShowDetails()
    {
        if (_details?.IsOpen == true)
        {
            _details.Hide();
            return;
        }

        _details = new Flyout(() => new AccountErrorDetails(Feature.Window, Original))
        {
            Placement = PopupPlacement.BottomEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Transient
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.Padding(0f).MaxWidth(360f)).Automation(new()
        {
            Name = Resources.ExternalErrorDetails,
            AutomationId = "accounts.external.error.flyout"
        });
        var before = Feature.Window.FocusedElement;
        _details.Closed += () =>
        {
            if (!_disposed)
                _focus.Request(this, before);
        };
        _details.ShowAt(Feature.Window);
    }

    internal void Back() => _ = NavigateAsync(Page == "sources" || Target is not null ? "list" : "sources");

    internal void Cancel() => _ = NavigateAsync("list");

    internal void Dismiss() => _ = NavigateAsync("close");

    internal void Reauthenticate()
    {
        if (Target?.AuthMethod == "littleskin")
            LittleSkin();
        else
            PasswordPage();
    }

    private async Task NavigateAsync(string page)
    {
        _cancelOperation?.Invoke();
        await _running;
        await DiscardAttemptAsync();
        if (_disposed)
            return;
        Password = string.Empty;
        Attempt = null;
        ClearError();
        Busy = false;
        if (page == "close")
        {
            _close();
        }
        else if (page == "list")
        {
            Feature.Show("list");
        }
        else
        {
            Address = string.Empty;
            Username = string.Empty;
            SelectedRole = string.Empty;
            LittleSkinSource = false;
            Page = page;
            _focus.Request(this);
        }
    }

    private async Task DiscardAttemptAsync()
    {
        if (_attemptId is not { } id)
            return;
        _attemptId = null;
        try
        {
            await Feature.Client.Identities.CancelExternalLoginAsync(id);
        }
        catch (Exception)
        { /* Encrypted attempts expire and cannot change a registered identity. */
        }
    }

    protected override IDisposable? OnMounted()
    {
        if (_refresh)
            Refresh();
        else if (Target?.AuthMethod == "littleskin")
            LittleSkin();
        var scope = Feature.Window.Commands.PushScope();
        scope.Bind(new KeyChord(Key.Escape), new Command("accounts.external.escape", () =>
                {
                    if (_details?.IsOpen == true)
                        _details.Hide();
                    else
                        Cancel();
                }), KeyScope.When(KeyContexts.Popup));
        scope.Bind(new KeyChord(Key.Enter), new Command("accounts.external.submit", Login, () => _details?.IsOpen != true && Page == "password" && !Busy && Username.Trim().Length > 0 && Password.Length > 0 && (Target is not null || Address.Trim().Length > 0)), KeyScope.When(KeyContexts.Popup));
        return scope;
    }

    protected override void OnDispose()
    {
        _disposed = true;
        _cancelOperation?.Invoke();
        _lifetime.Cancel();
        _details?.Hide();
        Password = string.Empty;
        _ = CleanUpAsync();
        base.OnDispose();
    }

    private async Task CleanUpAsync()
    {
        await _running;
        await DiscardAttemptAsync();
    }
}