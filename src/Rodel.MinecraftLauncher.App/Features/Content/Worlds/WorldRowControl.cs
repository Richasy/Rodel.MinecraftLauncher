// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Worlds;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

[View]
internal readonly partial record struct WorldRowView(WorldItem Data, WorldsPanel Panel)
{
    public Ui Body => new WorldRowControl(Data, Panel);
}

internal sealed partial class WorldRowControl : Control
{
    internal WorldRowControl(WorldItem data, WorldsPanel panel)
    {
        Data = data;
        Panel = panel;
    }

    private partial WorldItem Data { get; set; }
    private partial WorldsPanel Panel { get; set; }

    public override void ReconcileFrom(Control source)
    {
        if (source is WorldRowControl other)
        {
            Data = other.Data;
            Panel = other.Panel;
        }
    }

    private string Version => Data.Version.Length > 0 ? Data.Version : Resources.ValueNotAvailable;
    private string Metadata => string.Format(Resources.WorldMetadataLine, Version, WorldText.Mode(Data));

    private Ui Body => Border(Grid(
            [GridLength.Pixels(42f), GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                new ContentIconView(
                    Data.Icon,
                    Data.Warning,
                    Resources.ContentIconUnavailable,
                    LauncherTheme.Muted,
                    LauncherTheme.Edge).VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                Column([
                    Text(Data.Name)
                        .FontSize(14f).FontWeight(600).Foreground(LauncherTheme.Ink)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                        .Automation(new()
                        {
                            Name = Data.Name,
                            AutomationId = "world.row." + Data.Folder
                        }),
                    Text(Metadata)
                        .FontSize(12f).Foreground(LauncherTheme.Muted)
                        .Wrap(),
                    Text(string.Format(Resources.WorldPlayed, WorldText.Date(Data.LastPlayed)))
                        .FontSize(11f).Foreground(LauncherTheme.Muted)
                        .Wrap(),
                    Text(Data.Warning ?? string.Empty)
                        .FontSize(11f).Foreground(LauncherTheme.Warning)
                        .Wrap()
                        .Visible(Data.Warning is not null),
                ])
                    .Spacing(5f)
                    .Cell(1, 0),
                LauncherControls.OutlineCommand(Resources.WorldManage, () => Panel.Actions(Data))
                    .VAlign(VerticalAlignment.Center)
                    .Automation(new()
                    {
                        Name = string.Format(Resources.WorldManageNamed, Data.Name),
                        AutomationId = "world.actions." + Data.Folder
                    })
                    .Cell(2, 0),
            ])
            .ColumnSpacing(16f))
        .Padding(20f, 14f)
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(0f, 0f, 0f, 1f);
}