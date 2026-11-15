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

internal sealed partial class ExternalAccountSources(ExternalAccountPopup owner) : Control
{
    private readonly ExternalSourceChoice _littleSkin = new(
        "LittleSkin",
        Resources.ExternalLittleSkinDescription,
        () => owner.LittleSkin(),
        "accounts.external.littleskin",
        littleSkin: true);
    private readonly ExternalSourceChoice _custom = new(Resources.ExternalOther, Resources.ExternalOtherDescription, () => owner.PasswordPage(), "accounts.external.custom");

    private Ui Body => Column([_littleSkin, _custom,])
        .Spacing(12f);
}

internal sealed partial class ExternalSourceChoice(
    string title,
    string description,
    Action action,
    string id,
    bool littleSkin = false) : Control
{
    private static readonly ImageSource LittleSkinIcon = ImageSource.FromFile(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "littleskin.png"));

    private Ui Body => LauncherControls.Command(
        Row([
            Overlay([
                Image(LittleSkinIcon)
                    .Stretch(ImageStretch.Uniform)
                    .Visible(littleSkin),
                LauncherIcons.Line("globe", 28f, LauncherTheme.ProgressAccent)
                    .Visible(!littleSkin),
            ])
                .Width(28f).Height(28f)
                .VAlign(VerticalAlignment.Center),
            Column([
                Text(title)
                    .FontSize(14f).FontWeight(600),
                Text(description)
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Wrap()
            ])
                .Spacing(6f)
                .Weight(1f),
            LauncherIcons.Line("forward", 16f)
                .VAlign(VerticalAlignment.Center),
        ])
            .Spacing(16f),
        action)
        .Style(LauncherButtons.Button.Padding(16f)
            .ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center))
        .Automation(new()
        {
            Name = title,
            AutomationId = id
        });
}