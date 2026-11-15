// Copyright (c) Richasy. All rights reserved.

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
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed partial class JavaDefaultSetting(JavaManagerPage owner) : Control
{
    private Ui Body => Grid(
        [GridLength.Star(), GridLength.Auto],
        [GridLength.Auto],
        [
            Column([
                Text(Resources.SettingsJavaDefault)
                    .FontSize(14f).Foreground(LauncherTheme.Ink),
                Text(Resources.SettingsJavaDefaultHelp)
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Wrap(),
                Text(owner.DefaultLabel)
                    .FontSize(13f).Foreground(LauncherTheme.Ink)
                    .Wrap()
                    .Automation(new()
                    {
                        Name = owner.DefaultLabel,
                        AutomationId = "java.default"
                    }),
            ])
                .Spacing(6f)
                .VAlign(VerticalAlignment.Center)
                .Cell(0, 0),
            LauncherControls.OutlineCommand(Resources.JavaAutomatic, () => owner.UseAutomatic())
                .MinHeight(40f)
                .Enabled(owner.State.Editable)
                .Visible(owner.State.Default.Length > 0)
                .Automation(new()
                {
                    Name = Resources.JavaAutomatic,
                    AutomationId = "java.automatic"
                })
                .Cell(1, 0),
        ])
        .ColumnSpacing(16f);
}