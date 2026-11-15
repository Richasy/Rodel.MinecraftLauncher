// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Shows one selectable model using the shared neutral card and native checkbox roles.</summary>
[View]
internal readonly partial record struct AiModelCard(
    LauncherAppearance Theme,
    string Name,
    string Code,
    bool Selected,
    bool Hovered,
    bool Enabled,
    Action Toggle,
    string Id)
{
    /// <summary>Builds a spaced model card while retaining native checked and disabled semantics.</summary>
    public Ui Body => Border(Column([
            new LauncherCheckBox(
Theme,                Name,
                Selected,
                Toggle,
                Enabled,
                Id),
            Text(Code)
                .FontSize(11f).Foreground(Theme.Muted)
                .Wrap()
                .Visible(Name != Code),
        ])
            .Spacing(6f))
        .Padding(14f)
        .Background(Theme.ControlBackground)
        .BorderBrush(Hovered ? Theme.Focus : Theme.Edge)
        .BorderThickness(1f)
        .Margin(new Thickness(0f, 0f, 0f, 8f));
}