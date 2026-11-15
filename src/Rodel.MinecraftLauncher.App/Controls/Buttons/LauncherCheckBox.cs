// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Controls.Buttons;

/// <summary>Native checkbox input and semantics, with the launcher's square bevel surface.</summary>
[View]
internal readonly partial record struct LauncherCheckBox(
    LauncherAppearance Theme,
    string Label,
    bool IsChecked,
    Action Toggle,
    bool IsEnabled,
    string AutomationId,
    string? HelpText = null,
    string? AccessibleName = null,
    float FontSize = 14f,
    float BoxSize = 20f,
    float Spacing = 12f)
{
    /// <summary>Provides one native checkbox action with the launcher's shared mark and optional text label.</summary>
    public Ui Body => Sprout.Markup.CheckBox(
        Grid(
            [GridLength.Auto, GridLength.Star()],
            [GridLength.Auto],
            [
                new LauncherCheckMark(Theme, IsChecked, BoxSize).Cell(0, 0),
                Text(Label)
                    .FontSize(FontSize)
                    .Wrap()
                    .VAlign(VerticalAlignment.Center)
                    .Visible(Label.Length > 0)
                    .Cell(1, 0),
            ])
            .ColumnSpacing(Label.Length > 0 ? Spacing : 0f),
        IsChecked,
        Toggle)
        .Style(Theme.Styles.Checkbox)
        .Enabled(IsEnabled)
        .Automation(new()
        {
            Name = AccessibleName ?? Label,
            AutomationId = AutomationId,
            HelpText = HelpText
        });
}

/// <summary>Shares the launcher's checkbox glyph with native checkbox inputs that have richer content.</summary>
[View]
internal readonly partial record struct LauncherCheckMark(LauncherAppearance Theme, bool IsChecked, float BoxSize = 20f)
{
    /// <summary>Draws the square selection mark; the owning native checkbox provides input and accessibility.</summary>
    public Ui Body => Border(Overlay([
            Border()
                .Background(IsChecked ? Theme.Materials.InsetGradient : Theme.Materials.FaceGradient),
            Border()
                .BorderBrush(IsChecked ? Theme.InsetDark : Theme.RaisedLight)
                .BorderThickness(1f, 1f, 0f, 0f),
            Border()
                .BorderBrush(IsChecked ? Theme.InsetLight : Theme.RaisedDark)
                .BorderThickness(0f, 0f, 1f, 1f),
            LauncherIcons.Line("check", Math.Max(10f, BoxSize - 6f), Theme.PrimaryForeground)
                .Visible(IsChecked)
                .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center),
        ]))
        .Background(IsChecked ? Theme.PrimaryBackground : Theme.ControlBackground)
        .BorderBrush(Theme.Edge)
        .BorderThickness(1f)
        .Width(BoxSize).Height(BoxSize)
        .VAlign(VerticalAlignment.Center);
}