// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Controls;

/// <summary>A shared noninteractive empty-list placeholder with no icon plate or duplicate action.</summary>
[View]
internal readonly partial record struct EmptyPlaceholderView(
    string Icon,
    string Title,
    string Description,
    Color TitleInk,
    Color DetailInk,
    string AutomationId)
{
    /// <summary>Centers the unframed icon and compact localized text group within the available list viewport.</summary>
    public Ui Body => Column([
        LauncherIcons.Line(Icon, 44f, DetailInk)
            .HAlign(HorizontalAlignment.Center),
        Column([
            Text(Title)
                .Wrap()
                .Align(TextAlignment.Center)
                .FontSize(14f).FontWeight(600).Foreground(TitleInk)
                .Automation(new()
                {
                    Name = Title,
                    AutomationId = AutomationId
                }),
            Text(Description)
                .Wrap()
                .Align(TextAlignment.Center)
                .FontSize(12f).Foreground(DetailInk)
                .Visible(Description.Length > 0),
        ])
            .Spacing(6f),
    ])
        .Spacing(16f)
        .HAlign(HorizontalAlignment.Stretch).VAlign(VerticalAlignment.Center);
}