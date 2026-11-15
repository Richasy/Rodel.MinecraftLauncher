// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

[View]
internal readonly partial record struct SkinEditorEmptyState(LauncherAppearance Theme, string Icon, string Title, string Hint)
{
    /// <summary>Centers a quiet icon and concise text group within the actual available panel.</summary>
    public Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Star(), GridLength.Auto, GridLength.Star()],
        [
            Column([
                LauncherIcons.Line(Icon, 44f, Theme.EmptyPlaceholder)
                    .HAlign(HorizontalAlignment.Center),
                Column([
                    Text(Title)
                        .Automation(new()
                        {
                            Name = Title,
                            AutomationId = "skin.empty.title"
                        })
                        .FontSize(14f).Foreground(Theme.Quiet)
                        .HAlign(HorizontalAlignment.Center)
                        .Wrap(),
                    Text(Hint)
                        .Automation(new()
                        {
                            Name = Hint,
                            AutomationId = "skin.empty.hint"
                        })
                        .FontSize(11f).Foreground(Theme.EmptyPlaceholder)
                        .HAlign(HorizontalAlignment.Center)
                        .Wrap(),
                ])
                    .Spacing(6f),
            ])
                .Spacing(16f)
                .Cell(0, 1),
        ])
        .Padding(20f);
}