// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed record JavaInstallationRowData(
    JavaEntry Entry,
    bool Selected,
    bool Default,
    string Name,
    JavaRuntimeAppearance Appearance);

internal readonly record struct JavaRuntimeAppearance(
    Color Ink,
    Color Muted,
    Color Warning,
    Color Edge,
    Brush Selected);

[View]
internal readonly partial record struct JavaInstallationRow(JavaInstallationRowData Data)
{
    /// <summary>Builds the selected runtime row with explicit theme values and observed runtime data.</summary>
    public Ui Body => Overlay([
        new Controls.LauncherMaterialSurface(Data.Selected ? Data.Appearance.Selected : Colors.Transparent)
            .Margin(new Thickness(0f, 0f, 0f, 1f)),
        Border(Column([
            Grid(
                [GridLength.Star(), GridLength.Auto],
                [GridLength.Auto],
                [
                    Text("Java " + Data.Entry.Version)
                        .FontSize(14f).Foreground(Data.Appearance.Ink)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                        .Cell(0, 0),
                    Text(Data.Default
                            ? Resources.JavaCurrentDefault
                            : Data.Entry.Usable ? Resources.JavaUsable : Resources.JavaUnusable)
                        .FontSize(11f).Foreground(Data.Entry.Usable ? Data.Appearance.Muted : Data.Appearance.Warning)
                        .Cell(1, 0),
                ])
                .ColumnSpacing(12f),
            Text(Data.Entry.Id is null
                    ? Resources.JavaDiscovered
                    : Data.Entry.Managed ? Resources.JavaManaged : Resources.JavaAssociated)
                .FontSize(12f).Foreground(Data.Appearance.Muted),
            Text(Data.Entry.Path.Length == 0 ? Data.Entry.Id ?? string.Empty : Data.Entry.Path)
                .FontSize(12f).Foreground(Data.Appearance.Muted)
                .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                .FlowDirection(FlowDirection.LeftToRight),
        ])
            .Spacing(6f))
        .Padding(14f, 12f)
        .BorderBrush(Data.Appearance.Edge)
        .BorderThickness(0f, 0f, 0f, 1f),
    ]);
}