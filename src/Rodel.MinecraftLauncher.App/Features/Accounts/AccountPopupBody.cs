// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Accounts;

internal sealed partial class AccountPopupBody : Control
{
    private readonly AccountPopup _owner;
    private readonly AccountRows _rows;

    internal AccountPopupBody(AccountPopup owner)
    {
        _owner = owner;
        _rows = new(owner);
    }

    public Ui Body => Column([
        Column([
            new AccountTypeChoice(LauncherAppearance.Capture(),true, _owner.ContentWidth - 116f, () => _owner._feature.Show("login")),
            new AccountTypeChoice(LauncherAppearance.Capture(),false, _owner.ContentWidth - 116f, () => _owner._feature.Show("offline")),
            LauncherControls.Command(
                Row([
                    LauncherIcons.Line("globe", 28f)
                        .VAlign(VerticalAlignment.Center),
                    Column([
                        Text(Resources.AccountExternal)
                            .FontSize(14f).FontWeight(600),
                        Text(Resources.ExternalTypeDescription)
                            .FontSize(12f).Foreground(LauncherTheme.Muted)
                            .Wrap()
                    ])
                        .Spacing(6f)
                        .Weight(1f),
                ])
                    .Spacing(16f),
                () => _owner._feature.Show("external"))
                .Style(LauncherButtons.Button.Padding(16f)
                    .ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center))
                .Automation(new()
                {
                    Name = Resources.AccountExternal,
                    AutomationId = "accounts.type.external"
                }),
        ])
            .Spacing(12f)
            .Visible(_owner.Page == "types"),
        Column([
            Text(Resources.AccountName)
                .FontSize(13f).Foreground(LauncherTheme.Ink),
            TextBox(_owner.Name, value => _owner.Name = value)
                .Placeholder(Resources.AccountNamePlaceholder)
                .Style(LauncherStyles.Input)
                .SubmitOnEnter()
                .OnSubmitted(value => _ = _owner.AddOfflineAsync())
                .Automation(new()
                {
                    Name = Resources.AccountName,
                    AutomationId = "accounts.name"
                }),
            Text(Resources.AccountOfflineNote)
                .FontSize(12f).Foreground(LauncherTheme.Muted)
                .Wrap(),
            LauncherControls.Command(Resources.AccountAddUse, () => _ = _owner.AddOfflineAsync())
                .Style(LauncherButtons.DialogPrimary)
                .Enabled(!_owner.Busy)
                .Automation(new()
                {
                    Name = Resources.AccountAddUse,
                    AutomationId = "accounts.save-offline"
                }),
        ])
            .Spacing(8f)
            .Visible(_owner.Page == "offline"),
        Column([
            Text(Resources.AccountRemoveDescription)
                .FontSize(14f).Foreground(LauncherTheme.Ink)
                .Wrap(),
            LauncherControls.Command(Resources.AccountRemove, () => _ = _owner.RemoveAsync())
                .Style(LauncherButtons.DialogPrimary)
                .Enabled(!_owner.Busy)
                .Automation(new()
                {
                    Name = Resources.AccountRemove,
                    AutomationId = "accounts.confirm-remove"
                }),
        ])
            .Spacing(20f)
            .Visible(_owner.Page == "remove"),
        Column([
            Row([
                ProgressRing(new Size(20f, 20f))
                    .Style(default(ProgressRingStyle).Foreground(LauncherTheme.ProgressAccent)
                        .Thickness(2f))
                    .VAlign(VerticalAlignment.Center),
                Column([
                    Text(Resources.AccountWaiting)
                        .FontSize(14f).Foreground(LauncherTheme.Ink),
                    Text(Resources.AccountMicrosoft)
                        .FontSize(12f).Foreground(LauncherTheme.Muted),
                ])
                    .Spacing(0f)
                    .Weight(1f)
                    .VAlign(VerticalAlignment.Center),
                LauncherControls.Command(Resources.DialogCancel, _owner.CancelLogin)
                    .Style(LauncherButtons.IconButton)
                    .VAlign(VerticalAlignment.Center)
                    .Automation(new()
                    {
                        Name = Resources.AccountCancelLogin,
                        AutomationId = "accounts.cancel-login"
                    }),
            ])
                .Spacing(12f)
                .Visible(_owner.Busy),
            Text(_owner.Status)
                .FontSize(13f).Foreground(LauncherTheme.Error)
                .Wrap()
                .Visible(_owner.Status.Length > 0)
                .Automation(new()
                {
                    Name = _owner.Status,
                    AutomationId = "accounts.status"
                }),
            LauncherControls.Command(Resources.AccountRetry, () => _ = _owner.RetryAsync())
                .Style(LauncherButtons.DialogPrimary)
                .Visible(!_owner.Busy && _owner.ErrorCode != "AUTH_NOT_CONFIGURED")
                .Automation(new()
                {
                    Name = Resources.AccountRetry,
                    AutomationId = "accounts.retry"
                }),
            LauncherControls.Command(Resources.AccountBrowser, () => _ = _owner.LoginAsync(MicrosoftLoginMethod.Browser))
                .Style(LauncherButtons.DialogSecondary)
                .Visible(!_owner.Busy && _owner.ErrorCode == "AUTH_BROKER_UNAVAILABLE")
                .Automation(new()
                {
                    Name = Resources.AccountBrowser,
                    AutomationId = "accounts.browser"
                }),
        ])
            .Spacing(12f)
            .Visible(_owner.Page == "login"),
        Column([
            Text(Resources.AccountEmpty)
                .FontSize(15f).FontWeight(600).Foreground(LauncherTheme.Ink)
                .Visible(_owner.AccountsLoaded && _owner.Accounts.Length == 0)
                .Automation(new()
                {
                    Name = Resources.AccountEmpty,
                    AutomationId = "accounts.empty"
                }),
            _rows.WithRows(_owner.Accounts, _owner.CurrentId, _owner.Busy),
        ])
            .Spacing(0f)
            .Visible(_owner.Page == "list"),
        Text(_owner.Status)
            .FontSize(13f).Foreground(_owner.ErrorCode.Length > 0 ? LauncherTheme.Error : LauncherTheme.Muted)
            .Wrap()
            .Visible(_owner.Status.Length > 0 && _owner.Page != "login")
            .Automation(new()
            {
                Name = _owner.Status,
                AutomationId = "accounts.status"
            }),
        ProgressBar()
            .Indeterminate(true)
            .Thickness(3f)
            .Foreground(LauncherTheme.ProgressAccent)
            .Visible(_owner.Busy && _owner.ShowProgress && _owner.Page != "login")
            .Automation(new()
            {
                Name = Resources.AccountWaiting,
                AutomationId = "accounts.progress"
            }),
    ])
        .Spacing(16f)
        .Padding(_owner.Page == "list" && _owner.Accounts.Length > 0 ? 0f : 16f);
}

[View]
internal readonly partial record struct AccountTypeChoice(LauncherAppearance Theme, bool Microsoft, float CopyWidth, Action Select)
{
    internal string Title => Microsoft ? Resources.AccountMicrosoft : Resources.AccountOffline;
    internal string Description => Microsoft ? Resources.AccountMicrosoftDescription : Resources.AccountOfflineDescription;

    public Ui Body => LauncherControls.Command(
Theme, Row([
            Border(Column([
                    new AccountMicrosoftLogo(28f).Visible(Microsoft),
                    LauncherIcons.Line("account", 28f)
                        .Visible(!Microsoft),
                ])
                    .VAlign(VerticalAlignment.Center))
                .Width(32f)
                .VAlign(VerticalAlignment.Center),
            Column([
                Text(Title)
                    .FontSize(14f).FontWeight(600),
                Text(Description)
                    .FontSize(12f).Foreground(Theme.Muted)
                    .Wrap()
            ])
                .Spacing(6f)
                .Width(CopyWidth)
                .VAlign(VerticalAlignment.Center),
        ])
            .Spacing(16f),
        Select)
        .Style(Theme.Buttons.Button.Padding(16f)
            .ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center))
        .Automation(new()
        {
            Name = Title,
            HelpText = Description,
            AutomationId = Microsoft ? "accounts.type.microsoft" : "accounts.type.offline"
        });
}

[View]
internal readonly partial record struct AccountMicrosoftLogo(float Size, string? Id = null)
{
    public Ui Body => Column([
        Row([
            Rectangle(new Color(242, 80, 34, 255))
                .Width((Size - 2f) / 2f).Height((Size - 2f) / 2f)
                .Automation(new()
                {
                    Name = Resources.AccountMicrosoft,
                    AutomationId = Id
                }),
            Rectangle(new Color(127, 186, 0, 255))
                .Width((Size - 2f) / 2f).Height((Size - 2f) / 2f)
        ])
            .Spacing(2f),
        Row([
            Rectangle(new Color(0, 164, 239, 255))
                .Width((Size - 2f) / 2f).Height((Size - 2f) / 2f),
            Rectangle(new Color(255, 185, 0, 255))
                .Width((Size - 2f) / 2f).Height((Size - 2f) / 2f)
        ])
            .Spacing(2f),
    ])
        .Spacing(2f)
        .Width(Size).Height(Size);
}