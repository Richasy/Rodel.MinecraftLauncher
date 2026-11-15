// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Controls.Reports;

[View]
internal readonly partial record struct PathSummaryView(
    string Label,
    string Path,
    string Id,
    PathSummaryAppearance Appearance,
    int MaxLines = 2)
{
    public Ui Body => Border(Column([
            Text(Label)
                .FontSize(11f).Foreground(Appearance.Label),
            Text(Path)
                .FontSize(11f).Foreground(Appearance.Value)
                .Wrap().MaxLines(MaxLines).Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                .Automation(new()
                {
                    Name = string.Format(Resources.ImportPathAccessible, Label, Path),
                    AutomationId = Id,
                }),
        ])
            .Spacing(3f))
        .Padding(new Thickness(10f, 8f, 10f, 8f))
        .Background(Appearance.Surface)
        .BorderBrush(Appearance.Edge)
        .BorderThickness(1f);
}

internal readonly record struct PathSummaryAppearance(
    Color Label,
    Color Value,
    Color Edge,
    Color Surface)
{
    internal static PathSummaryAppearance Capture() => new(LauncherTheme.Quiet, LauncherTheme.Muted, LauncherTheme.Edge, LauncherTheme.ControlBackground);
}