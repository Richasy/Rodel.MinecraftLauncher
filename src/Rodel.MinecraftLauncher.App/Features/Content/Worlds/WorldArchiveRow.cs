// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
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
using Sprout.WindowsAppSDK;
using System.Globalization;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed record WorldArchiveItem(
    int Index,
    string Name,
    string Reason,
    string Time);

[View]
internal readonly partial record struct WorldArchiveRow(WorldArchiveItem Data, WorldOperationDialog Dialog)
{
    public Ui Body => new WorldArchiveRowControl(Data, Dialog);
}

internal sealed partial class WorldArchiveRowControl : Control
{
    internal WorldArchiveRowControl(WorldArchiveItem data, WorldOperationDialog dialog)
    {
        Data = data;
        Dialog = dialog;
    }

    private partial WorldArchiveItem Data { get; set; }
    private partial WorldOperationDialog Dialog { get; set; }

    public override void ReconcileFrom(Control source)
    {
        if (source is WorldArchiveRowControl other)
        {
            Data = other.Data;
            Dialog = other.Dialog;
        }
    }

    private Ui Body => Column([
        LauncherControls.Command(
            Grid(
                [GridLength.Pixels(28f), GridLength.Star()],
                [GridLength.Auto],
                [
                    LauncherIcons.Line("instances", 26f, LauncherTheme.Muted)
                        .VAlign(VerticalAlignment.Center)
                        .Cell(0, 0),
                    Column([
                        Text(Data.Name)
                            .FontSize(14f).FontWeight(600).Foreground(LauncherTheme.Ink)
                            .Wrap(),
                        Text(Data.Reason)
                            .FontSize(12f).Foreground(LauncherTheme.Muted),
                        Text(Data.Time)
                            .FontSize(11f).Foreground(LauncherTheme.Muted),
                    ])
                        .Spacing(4f)
                        .Cell(1, 0),
                ])
                .ColumnSpacing(12f),
            () => Dialog.Choose(Data.Index))
            .Style(LauncherButtons.Navigation(Dialog.Selected == Data.Index)
                .Padding(12f, 10f)
                .ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center))
            .AsSelected(Dialog.Selected == Data.Index)
            .Enabled(Dialog.CanClose)
            .Automation(new()
            {
                Name = string.Format(Resources.WorldArchiveChoice, Data.Name, Data.Reason, Data.Time),
                AutomationId = "world.archive.choice." + Data.Index
            }),
    ])
        .Margin(new Thickness(0f, 0f, 0f, 6f));
}