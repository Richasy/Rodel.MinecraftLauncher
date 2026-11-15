// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Animation;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using System.Globalization;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

[View]
internal readonly partial record struct AiSuggestion(
    string Title,
    string Description,
    Action Choose,
    bool Compact,
    Color Ink,
    Color Muted,
    Color Edge,
    ButtonStyle Appearance,
    string Id)
{
    /// <summary>Threads current language, size and theme into the retained hover interaction.</summary>
    public Ui Body => new AiSuggestionButton(new(
            Title,
            Description,
            Choose,
            Compact,
            Ink,
            Muted,
            Edge,
            Appearance,
            Id));
}

internal sealed record AiSuggestionOptions(
    string Title,
    string Description,
    Action Choose,
    bool Compact,
    Color Ink,
    Color Muted,
    Color Edge,
    ButtonStyle Appearance,
    string Id);

internal sealed partial class AiSuggestionButton(AiSuggestionOptions initial) : Control
{
    private partial AiSuggestionOptions Configuration { get; set; }
    private AiSuggestionOptions Value => Configuration ?? initial;

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is AiSuggestionButton next)
            Configuration = next.Value;
    }

    private partial bool Hovered { get; set; }
    private partial bool Focused { get; set; }
    private float Inset => Hovered || Focused ? 12f : 0f;
    private static readonly AnimationSpec Motion = AnimationSpec.Tween(
        TimeSpan.FromMilliseconds(180),
            .2f,
        0f,
            .2f,
        1f);

    private Ui Body => Border(HoverArea(
            LauncherControls.OutlineCommand(
                Grid(
                    [GridLength.Star(), GridLength.Auto],
                    [GridLength.Auto],
                    [
                        Column([
                            Text(Value.Title)
                                .FontSize(13f).Foreground(Value.Ink)
                                .Wrap(),
                            Text(Value.Description)
                                .FontSize(11f).Foreground(Value.Muted)
                                .Wrap()
                                .Visible(!Value.Compact),
                        ])
                            .Spacing(Value.Compact ? 0f : 5f)
                            .Animation(TransformAnimation.TranslateTo(CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? -Inset : Inset, 0f, Motion))
                            .Cell(0, 0),
                        LauncherIcons.Line("forward", 16f, Value.Muted)
                            .VAlign(VerticalAlignment.Center)
                            .Animation(TransformAnimation.TranslateTo(CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? Inset : -Inset, 0f, Motion))
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(40f),
                Value.Choose)
                .Style(Value.Appearance.Padding(0f, Value.Compact ? 10f : 12f)
                    .ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center))
                .OnGotFocus(_ => Focused = true)
                .OnLostFocus(() => Focused = false)
                .Automation(new()
                {
                    Name = Value.Title,
                    AutomationId = Value.Id
                }),
            value => Hovered = value))
        .BorderBrush(Value.Edge)
        .BorderThickness(0f, 1f, 0f, 0f);
}