// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Shell;

[View]
internal readonly partial record struct NavItem(
    LauncherAppearance Theme,
    string Label,
    string Glyph,
    bool Selected,
    Action Activate,
    ButtonStyle Appearance,
    bool CompactHeight = false)
{
    /// <summary>Builds a primary navigation command with centered icon and label spacing for the available height.</summary>
    public Ui Body => LauncherControls.Command(
        Theme,
        Column([
            LauncherIcons.Line(Glyph, 22f)
                .HAlign(HorizontalAlignment.Center),
            Viewbox(Text(Label)
                    .FontSize(11f)
                    .WrapWholeWords()
                    .MaxLines(Label.Contains(' ') ? 2 : 1)
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                    .Align(TextAlignment.Center)
                    .MaxWidth(LauncherLayout.NavigationLabelWidth / .8f))
                .Stretch(ImageStretch.Uniform)
                .StretchDirection(StretchDirection.DownOnly)
                .Width(LauncherLayout.NavigationLabelWidth)
                .MinHeight(18f)
                .HAlign(HorizontalAlignment.Center),
        ])
            .Spacing(CompactHeight ? 3f : 7f),
        Activate)
        .Style(Appearance)
        .AsSelected(Selected)
        .Automation(new()
        {
            Name = Label,
            AutomationId = $"nav.{Glyph}",
            HelpText = Selected ? Resources.CurrentPage : Resources.SwitchPage,
        })
        .Width(LauncherLayout.NavigationItemWidth).MinHeight(CompactHeight ? 50f : 66f);
}