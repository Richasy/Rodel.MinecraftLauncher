// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
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

internal sealed partial class ExternalAccountPassword(ExternalAccountPopup owner) : Control
{
    private Ui Body => Column([
        Column([
            Text(Resources.ExternalAddress)
                .FontSize(13f).Foreground(LauncherTheme.Ink),
            Flow(FlowDirection.LeftToRight, TextBox(owner.Address, value => owner.Address = value)
                    .Placeholder(Resources.ExternalAddressHint)
                    .Style(LauncherStyles.Input)
                    .Automation(new()
                    {
                        Name = Resources.ExternalAddress,
                        AutomationId = "accounts.external.address"
                    })),
        ])
            .Spacing(8f)
            .Visible(owner.Target is null && !owner.LittleSkinSource),
        Column([
            Text(owner.Source)
                .FontSize(14f).FontWeight(600).Foreground(LauncherTheme.Ink),
            Text(owner.Target?.AuthServer ?? string.Empty)
                .FontSize(11f).Foreground(LauncherTheme.Muted)
                .Wrap(),
        ])
            .Spacing(4f)
            .Visible(owner.Target is not null),
        Column([
            Text(owner.LittleSkinSource ? Resources.ExternalLittleSkinUsername : Resources.ExternalUsername)
                .FontSize(13f).Foreground(LauncherTheme.Ink),
            TextBox(owner.Username, value => owner.Username = value)
                .Placeholder(owner.LittleSkinSource ? Resources.ExternalLittleSkinUsernamePlaceholder : Resources.ExternalUsernamePlaceholder)
                .Style(LauncherStyles.Input)
                .Automation(new()
                {
                    Name = owner.LittleSkinSource ? Resources.ExternalLittleSkinUsername : Resources.ExternalUsername,
                    AutomationId = "accounts.external.username"
                }),
        ])
            .Spacing(8f),
        Column([
            Text(Resources.ExternalPassword)
                .FontSize(13f).Foreground(LauncherTheme.Ink),
            PasswordBox()
                .Placeholder(Resources.AccountPasswordPlaceholder)
                .Password(owner.Password)
                .OnPasswordChanged(value => owner.Password = value)
                .Palette(LauncherStyles.InputPalette)
                .Padding(LauncherStyles.InputPadding)
                .FontSize(14f)
                .CornerRadius(0f)
                .Automation(new()
                {
                    Name = Resources.ExternalPassword,
                    AutomationId = "accounts.external.password"
                }),
        ])
            .Spacing(8f),
    ])
        .Spacing(16f);
}