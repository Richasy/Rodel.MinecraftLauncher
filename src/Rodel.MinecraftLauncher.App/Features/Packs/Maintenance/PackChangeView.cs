// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Packs;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.WindowsAppSDK;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Packs;

internal sealed record PackChangeRow(
    int Index,
    string Path,
    string Action,
    string Name = "",
    string BeforeVersion = "",
    string AfterVersion = "",
    string ModId = "")
{
    internal bool IsMod => ModId.Length > 0;
}

[View]
internal readonly partial record struct PackChangeView(LauncherAppearance Theme, PackChangeRow Row)
{
    internal string Label => PackChangeText.Label(Row.Action);
    internal string Marker => Row.Action switch
    {
        "add" => "+",
        "delete" => "\u2212",
        "replace" or "update" => "~",
        "conflict" => "!",
        _ => "="
    };
    internal Color Accent => Theme.HighContrast ? Theme.Ink : Row.Action switch
    {
        "add" => Theme.SuccessForeground,
        "delete" => Theme.Error,
        "replace" or "update" or "conflict" => Theme.Warning,
        _ => Theme.Muted
    };

    /// <summary>Distinguishes reviewed additions, removals and retained conflicts through diff markers, semantic surfaces and explicit labels.</summary>
    public Ui Body => Border(Grid(
            [GridLength.Pixels(24f), GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                Text(Marker)
                    .FontSize(18f).FontWeight(600).Foreground(Accent)
                    .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                Column([
                    Flow(FlowDirection.LeftToRight, Text(Row.IsMod ? Row.Name : Row.Path)
                            .FontSize(13f).Foreground(Theme.Ink)
                            .Automation(new()
                            {
                                Name = string.Format(Resources.PackUpdateReportFile, Row.IsMod ? Row.Name : Row.Path, Label),
                                HelpText = Row.Path,
                                AutomationId = "pack.update.change." + Row.Index
                            })
                            .NoWrap()
                            .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()),
                    Flow(
                        FlowDirection.LeftToRight,
                        Sprout.Markup.Row([
                            Text(Row.BeforeVersion)
                                .FontSize(12f).Foreground(Theme.Error)
                                .Visible(Row.BeforeVersion.Length > 0),
                            LauncherIcons.Line("forward", 12f, Theme.Muted)
                                .Visible(Row.BeforeVersion.Length > 0 && Row.AfterVersion.Length > 0),
                            Text(Row.AfterVersion)
                                .FontSize(12f).Foreground(Theme.SuccessForeground)
                                .Visible(Row.AfterVersion.Length > 0),
                        ])
                            .Spacing(8f))
                        .Visible(Row.IsMod),
                ])
                    .Spacing(4f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(1, 0),
                Border(Text(Label)
                        .FontSize(11f).Foreground(Accent))
                    .Padding(8f, 4f)
                    .Background(Theme.HighContrast ? Theme.ControlBackground : Accent.MultiplyAlpha(.1f))
                    .VAlign(VerticalAlignment.Center)
                    .Cell(2, 0),
            ])
            .ColumnSpacing(12f))
        .Padding(12f)
        .Background(Theme.HighContrast ? Theme.ControlBackground : Accent.MultiplyAlpha(.09f))
        .BorderBrush(Theme.HighContrast ? Theme.Edge : Accent.MultiplyAlpha(.4f))
        .BorderThickness(1f);
}

internal static class PackChangeText
{
    internal static string Label(string? action) => action switch
    {
        "add" => Resources.PackUpdateAdd,
        "replace" => Resources.PackUpdateReplace,
        "update" => Resources.PackUpdateModUpdates,
        "delete" => Resources.PackUpdateDelete,
        "conflict" => Resources.PackUpdateConflict,
        _ => Resources.PackUpdatePreserve
    };
}