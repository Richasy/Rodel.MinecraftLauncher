// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Import;

[View]
internal readonly partial record struct ImportDirectoryChoice(
    LauncherAppearance Theme,
    string Label,
    string Path,
    string Id,
    bool Enabled,
    Action Invoke)
{
    public Ui Body => LauncherControls.Command(
Theme, Column([
            Text(Label)
                .FontSize(13f).FontWeight(600).Foreground(Theme.Ink),
            Text(Path)
                .FontSize(11f).Foreground(Theme.Muted)
                .Wrap().MaxLines(2).Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
        ])
            .Spacing(4f),
        Invoke)
        .Style(Theme.Buttons.Navigation(false))
        .Enabled(Enabled)
        .Automation(new()
        {
            Name = string.Format(Resources.ImportPathAccessible, Label, Path),
            AutomationId = Id,
        });
}