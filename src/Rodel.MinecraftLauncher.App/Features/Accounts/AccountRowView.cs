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

[View]
internal readonly partial record struct AccountRowView(
    LauncherAppearance Theme,
    IdentityRecord Account,
    AccountAvatarImages Avatars,
    string CurrentId,
    bool Busy,
    bool GroupEnd,
    Action<string> Select,
    Action<IdentityRecord> More,
    Action<IdentityRecord> EditAvatar)
{
    internal string Id => Account.Id!;
    internal string Name => Account.Name ?? string.Empty;
    internal bool Microsoft => Account.Type == "microsoft";
    internal bool Expired => Account.Status == "login-required";
    internal Action UseAction
    {
        get
        {
            var select = Select;
            var id = Id;
            return () => select(id);
        }
    }

    internal Action MoreAction
    {
        get
        {
            var more = More;
            var account = Account;
            return () => more(account);
        }
    }

    internal Action AvatarAction
    {
        get
        {
            var edit = EditAvatar;
            var account = Account;
            return () => edit(account);
        }
    }

    public Ui Body => Border(Row([
            new AccountAvatar(Account, 28f, Avatars).VAlign(VerticalAlignment.Center)
                .Visible(Account.Type != "offline"),
            LauncherControls.Command(
Theme,                Overlay([
                    new AccountAvatar(Account, 28f, Avatars),
                    Border(LauncherIcons.Line("edit", 12f))
                        .Background(Theme.ControlBackground)
                        .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Bottom),
                ]),
                AvatarAction)
                .Style(Theme.Buttons.FlatIconButton.Padding(0f))
                .Width(28f).Height(28f)
                .Enabled(!Busy)
                .Visible(Account.Type == "offline")
                .ToolTip(Resources.ImageAvatarTitle)
                .Automation(new()
                {
                    Name = string.Format(Resources.ImageAvatarAction, Name),
                    AutomationId = "accounts.avatar.edit." + Id
                }),
            Column([
                Text(Name)
                    .FontSize(13f).FontWeight(600).Foreground(Theme.Ink)
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
                Text(Account.Type == "external"
                        ? string.Format(Resources.ExternalAccountState, Account.AuthServerName ?? Resources.AccountExternal, Expired
                            ? Resources.AccountSignInAgain
                            : Account.Status == "profile-removed" ? Resources.ExternalRoleRemoved : Resources.ExternalReady)
                        : Microsoft ? (Expired ? Resources.AccountSignInAgain : Resources.AccountReady) : Resources.AccountLocalReady)
                    .FontSize(12f).Foreground(Expired || Account.Status == "profile-removed" ? Theme.Error : Theme.Muted)
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
            ])
                .Spacing(4f)
                .Weight(1f)
                .VAlign(VerticalAlignment.Center),
            Border(Text(Resources.AccountCurrent)
                    .FontSize(12f).Foreground(Theme.Muted)
                    .Automation(new()
                    {
                        Name = Resources.AccountCurrent,
                        AutomationId = "accounts.current." + Id
                    }))
                .Padding(new Thickness(8f, 4f, 8f, 4f))
                .Background(Theme.ControlBackground)
                .VAlign(VerticalAlignment.Center)
                .Visible(Id == CurrentId),
            LauncherControls.OutlineCommand(Theme,Resources.AccountUse, UseAction)
                .Style(Theme.Buttons.DialogHeaderButton)
                .VAlign(VerticalAlignment.Center)
                .Enabled(!Busy)
                .Visible(Id != CurrentId)
                .Automation(new()
                {
                    Name = string.Format(Resources.AccountSelectLabel, Name),
                    AutomationId = "accounts.use." + Id
                }),
            Column([
                new AccountMicrosoftLogo(16f, "accounts.type." + Id).ToolTip(Resources.AccountMicrosoft)
                    .Visible(Microsoft),
                LauncherIcons.Line("computer", 18f)
                    .ToolTip(Resources.AccountOffline)
                    .Automation(new()
                    {
                        Name = Resources.AccountOffline,
                        AutomationId = "accounts.type." + Id
                    })
                    .Visible(Account.Type == "offline"),
                LauncherIcons.Line("globe", 18f)
                    .ToolTip(AccountsFeature.TypeLabel(Account))
                    .Automation(new()
                    {
                        Name = AccountsFeature.TypeLabel(Account),
                        AutomationId = "accounts.type." + Id
                    })
                    .Visible(Account.Type == "external"),
            ])
                .Width(18f)
                .VAlign(VerticalAlignment.Center),
            LauncherControls.Command(
Theme,                Row([
                    LauncherIcons.Line("more", 18f)
                ]),
                MoreAction)
                .Style(Theme.Buttons.IconButton)
                .Width(32f).Height(32f)
                .VAlign(VerticalAlignment.Center)
                .Enabled(!Busy)
                .Automation(new()
                {
                    Name = string.Format(Resources.AccountMore, Name),
                    AutomationId = "accounts.more." + Id
                }),
        ])
            .Spacing(12f))
        .Padding(16f)
        .BorderBrush(Theme.Edge)
        .BorderThickness(0f, 0f, 0f, GroupEnd ? 1f : 0f);
}