// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Wallpapers;
using Rodel.MinecraftLauncher.App.Imaging;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Theming;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

[View]
internal readonly partial record struct JavaManagerHeading(
    LauncherAppearance Theme,
    bool RightToLeft,
    Action Back,
    Brush Ink,
    ButtonStyle BackAppearance)
{
    /// <summary>Builds the Java manager subpage title and return action.</summary>
    public Ui Body => Grid(
        [GridLength.Auto, GridLength.Star()],
        [GridLength.Auto],
        [
            LauncherControls.Command(
Theme,                Row([
                    LauncherIcons.Line(RightToLeft ? "forward" : "back", 18f)
                ]),
                Back)
                .Style(BackAppearance)
                .Width(36f).Height(36f)
                .ToolTip(Resources.SettingsJavaMemory)
                .Automation(new()
                {
                    Name = Resources.SettingsJavaMemory,
                    AutomationId = "java.back"
                })
                .Cell(0, 0),
            Text(Resources.JavaManager)
                .FontSize(22f).FontWeight(700).Foreground(Ink)
                .VAlign(VerticalAlignment.Center)
                .Cell(1, 0),
        ])
        .ColumnSpacing(10f);
}