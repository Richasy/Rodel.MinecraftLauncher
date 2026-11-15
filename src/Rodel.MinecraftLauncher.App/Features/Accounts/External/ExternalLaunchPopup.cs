// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Styling;
using Sprout.WindowsAppSDK;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Accounts;

/// <summary>Presents recovery for the frozen external launch; Core owns account, destination and cache eligibility.</summary>
internal sealed partial class ExternalLaunchPopup : Control
{
    private readonly LauncherClient _client;
    private readonly HomeController _home;
    private readonly Window _window;
    private readonly ExternalLaunchRecovery _recovery;
    private readonly Action _close;
    private readonly Action<IdentityRecord?>? _signIn;
    private readonly CancellationTokenSource _lifetime;
    private readonly PopupFocusRecovery _focus;
    private Action? _cancelOperation;
    private Flyout? _details;
    private partial bool Busy { get; set; }
    private partial string Error { get; set; }
    private partial string Original { get; set; }
    private bool _disposed;
    private bool Component => _recovery.Code == "AUTH_COMPONENT_FAILED";
    private bool Removed => _recovery.Code is "AUTH_PROFILE_REMOVED" or "AUTH_SOURCE_CHANGED";
    private bool Expired => _recovery.Code is "AUTH_LOGIN_REQUIRED" or "AUTH_CACHE_UNAVAILABLE";
    private string Title => _recovery.Code switch
    {
        "AUTH_COMPONENT_FAILED" => Resources.ExternalComponentFailed,
        "AUTH_PROFILE_REMOVED" => Resources.ExternalRoleRemoved,
        "AUTH_LOGIN_REQUIRED" or "AUTH_CACHE_UNAVAILABLE" => Resources.AccountSignInRequired,
        "AUTH_TLS_ERROR" => Resources.ExternalTlsError,
        "AUTH_RESPONSE_INVALID" => Resources.ExternalResponseError,
        "AUTH_RATE_LIMITED" => Resources.ExternalRateError,
        "AUTH_NETWORK_ERROR" or "AUTH_TIMEOUT" => Resources.ExternalNetworkError,
        _ => Resources.ExternalRemoteError,
    };
    private string Primary => Component
        ? Resources.AccountRetry
        : Removed
        ? Resources.ExternalAddOtherRole
        : Expired ? Resources.AccountSignInAgain : Resources.AccountRetry;

    internal ExternalLaunchPopup(
        LauncherClient client,
        HomeController home,
        Window window,
        ExternalLaunchRecovery recovery,
        Action close,
        Action<IdentityRecord?>? signIn)
    {
        _client = client;
        _home = home;
        _window = window;
        _recovery = recovery;
        _close = close;
        _signIn = signIn;
        _focus = Own(new PopupFocusRecovery(window));
        _lifetime = Own(new CancellationTokenSource());
        Error = string.Empty;
        Original = recovery.Details?["original"]?.ToString() ?? recovery.Message;
    }

    private Ui Body => Column([
        Border(Text(Title)
                .FontSize(16f).FontWeight(600).Foreground(LauncherTheme.Ink)
                .Wrap())
            .Padding(16f, 12f)
            .BorderBrush(LauncherTheme.Edge)
            .BorderThickness(0f, 0f, 0f, 1f),
        Column([
            Border(Grid(
                    [GridLength.Pixels(20f), GridLength.Star()],
                    [GridLength.Auto],
                    [
                        LauncherIcons.Line("warning", 20f, LauncherTheme.Error)
                            .Cell(0, 0),
                        Column([
                            Grid(
                                [GridLength.Star(), GridLength.Pixels(24f)],
                                [GridLength.Auto],
                                [
                                    Text(Title)
                                        .FontSize(14f).FontWeight(600).Foreground(LauncherTheme.Ink)
                                        .Wrap()
                                        .Cell(0, 0),
                                    LauncherControls.Command(
                                        Row([
                                            LauncherIcons.Line("info", 16f)
                                        ]),
                                        ShowDetails)
                                        .Style(LauncherButtons.IconButton.Padding(4f))
                                        .Width(24f).Height(24f)
                                        .ToolTip(Resources.ExternalErrorDetails)
                                        .VAlign(VerticalAlignment.Center)
                                        .Automation(new()
                                        {
                                            Name = Resources.ExternalErrorDetails,
                                            AutomationId = "external-launch.error.info"
                                        })
                                        .Cell(1, 0),
                                ])
                                .ColumnSpacing(8f),
                            Text(Error.Length > 0
                                    ? Error
                                    : Component
                                    ? Resources.ExternalComponentHint
                                    : Expired
                                    ? Resources.AccountSignInRequired
                                    : _recovery.Code is "AUTH_NETWORK_ERROR" or "AUTH_TIMEOUT" ? Resources.ExternalNetworkHint : _recovery.Message)
                                .FontSize(13f).Foreground(LauncherTheme.Muted)
                                .Wrap(),
                        ])
                            .Spacing(4f)
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(12f))
                .Padding(12f)
                .Background(LauncherTheme.HighContrast ? LauncherTheme.ControlBackground : LauncherTheme.Error.MultiplyAlpha(.07f))
                .BorderBrush(LauncherTheme.Error.MultiplyAlpha(.32f))
                .BorderThickness(1f)
                .Visible(!Busy),
            Text(string.Format(Resources.ExternalAccountState, _recovery.Account.Name, _recovery.Account.AuthServerName))
                .FontSize(12f).Foreground(LauncherTheme.Muted)
                .Wrap()
                .Visible(!Busy),
            Row([
                ProgressRing(new Size(20f, 20f))
                    .Style(default(ProgressRingStyle).Foreground(LauncherTheme.ProgressAccent)
                        .Thickness(2f))
                    .VAlign(VerticalAlignment.Center),
                Column([
                    Text(Component ? Resources.ExternalComponentPreparing : Resources.ExternalVerifying)
                        .FontSize(14f).Foreground(LauncherTheme.Ink),
                    Text(Component ? "authlib-injector.jar" : _recovery.Account.AuthServerName ?? string.Empty)
                        .FontSize(12f).Foreground(LauncherTheme.Muted),
                ])
                    .Spacing(0f)
                    .Weight(1f)
                    .VAlign(VerticalAlignment.Center),
                LauncherControls.Command(Resources.DialogCancel, () => Cancel())
                    .Style(LauncherButtons.IconButton)
                    .VAlign(VerticalAlignment.Center)
                    .Automation(new()
                    {
                        Name = Resources.DialogCancel,
                        AutomationId = "external-launch.cancel-work"
                    }),
            ])
                .Spacing(12f)
                .Visible(Busy),
            LauncherControls.OutlineCommand(Resources.ExternalChooseComponent, () => _ = ChooseComponentAsync())
                .Style(LauncherButtons.OutlineButton)
                .Visible(Component && !Busy)
                .Automation(new()
                {
                    Name = Resources.ExternalChooseComponent,
                    AutomationId = "external-launch.choose-component"
                }),
            LauncherControls.OutlineCommand(Resources.ExternalTryCache, () => _ = RetryAsync(true))
                .Style(LauncherButtons.OutlineButton)
                .Visible(_recovery.CanUseCache && !Busy)
                .Automation(new()
                {
                    Name = Resources.ExternalTryCache,
                    AutomationId = "external-launch.use-cache"
                }),
            Text(Resources.ExternalCacheHint)
                .FontSize(12f).Foreground(LauncherTheme.Muted)
                .Wrap()
                .Visible(_recovery.CanUseCache && !Busy),
        ])
            .Spacing(16f)
            .Padding(16f),
        Grid(
            [GridLength.Star(), GridLength.Star()],
            [GridLength.Auto],
            [
                LauncherControls.Command(Primary, () =>
                    {
                        if (Expired || Removed)
                            SignIn();
                        else
                            _ = RetryAsync(false);
                    })
                    .Style(LauncherButtons.DialogPrimary)
                    .Enabled(!(Expired || Removed) || _signIn is not null)
                    .Automation(new()
                    {
                        Name = Primary,
                        AutomationId = "external-launch.retry"
                    })
                    .Cell(0, 0),
                LauncherControls.OutlineCommand(Resources.DialogCancel, _close)
                    .Style(LauncherButtons.DialogSecondary)
                    .Automation(new()
                    {
                        Name = Resources.DialogCancel,
                        AutomationId = "external-launch.cancel"
                    })
                    .Cell(1, 0),
            ])
            .ColumnSpacing(12f)
            .Padding(16f, 0f, 16f, 16f)
            .Visible(!Busy),
    ])
        .Width(Math.Min(440f, _window.ClientSize.Width - 48f));

    private async Task RetryAsync(bool cached, string? file = null)
    {
        if (Busy || _disposed)
            return;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _cancelOperation = operation.Cancel;
        Busy = true;
        Error = string.Empty;
        _focus.Request(this);
        try
        {
            if (Component)
                await _client.Identities.PrepareExternalLoginComponentAsync(file, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            await _home.StartLaunchAsync(_recovery.Request with { Options = _recovery.Request.Options with { UseCachedExternalSession = cached } }, applyDefaults: false);
            if (!_disposed)
                _close();
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                Error = exception.Message;
                Original = (exception as LauncherOperationException)?.Details?["original"]?.ToString() ?? exception.Message;
            }
        }
        finally
        {
            _cancelOperation = null;
            if (!_disposed)
            {
                Busy = false;
                _focus.Request(this);
            }
        }
    }

    private async Task ChooseComponentAsync()
    {
        var before = _window.FocusedElement;
        try
        {
            var picker = _window.CreateFileOpenPicker();
            picker.FileTypeFilter.Add(".jar");
            if (await picker.PickSingleFileAsync() is { } file && !_disposed)
                await RetryAsync(false, file.Path);
        }
        catch (Exception exception)
        {
            if (!_disposed)
                Error = exception.Message;
        }
        finally
        {
            if (!_disposed)
                _focus.Request(this, before);
        }
    }

    private void ShowDetails()
    {
        var before = _window.FocusedElement;
        _details = new Flyout(() => new AccountErrorDetails(_window, Original))
        {
            Placement = PopupPlacement.BottomEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Transient
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.Padding(0f).MaxWidth(360f)).Automation(new()
        {
            Name = Resources.ExternalErrorDetails,
            AutomationId = "external-launch.error.flyout"
        });
        _details.Closed += () =>
        {
            if (!_disposed)
                _focus.Request(this, before);
        };
        _details.ShowAt(_window);
    }

    private void Cancel() => _cancelOperation?.Invoke();

    private void SignIn()
    {
        _close();
        _window.RequestAnimationFrame(_ => _signIn?.Invoke(Removed ? null : _recovery.Account));
    }

    protected override IDisposable? OnMounted()
    {
        var scope = _window.Commands.PushScope();
        scope.Bind(new KeyChord(Key.Escape), new Command("external-launch.escape", () =>
                {
                    if (_details?.IsOpen == true)
                        _details.Hide();
                    else if (Busy)
                        Cancel();
                    else
                        _close();
                }), KeyScope.When(KeyContexts.Popup));
        return scope;
    }

    protected override void OnDispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _details?.Hide();
        base.OnDispose();
    }
}