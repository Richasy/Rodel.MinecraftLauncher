// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Imaging;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using System.Text.Json;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

[View]
internal readonly partial record struct CacheCleanupEntryView(LauncherAppearance Theme, CacheCleanupEntry Entry, bool RightToLeft)
{
    public Ui Body => Border(Column([
            Flow(FlowDirection.LeftToRight, Text(Entry.Name)
                    .FontSize(13f).Foreground(Theme.Ink)
                    .Trim(TextTrimming.CharacterEllipsis).MaxLines(1).TooltipWhenTrimmed()
                    .Align(RightToLeft ? TextAlignment.Trailing : TextAlignment.Leading)
                    .Automation(new()
                    {
                        Name = Entry.Path,
                        HelpText = Entry.Reason + (Entry.Detail.Length > 0 ? Environment.NewLine + Entry.Detail : string.Empty),
                        AutomationId = "settings.cleanup.item." + Entry.Key
                    })),
            Text(Entry.Reason)
                .FontSize(12f).Foreground(Entry.Warning ? Theme.Warning : Theme.Muted)
                .Wrap(),
        ])
            .Spacing(5f))
        .Padding(0f, 10f)
        .BorderBrush(Theme.Edge)
        .BorderThickness(0f, 0f, 0f, Entry.Last ? 0f : 1f);
}