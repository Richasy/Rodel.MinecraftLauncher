// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

[View]
internal readonly partial record struct SkinHistoryRow(LauncherAppearance Theme, SkinHistoryEntry Entry, bool Future)
{
    /// <summary>Shows a transaction's real type and document dimensions without inventing timestamps.</summary>
    public Ui Body => Grid(
        [GridLength.Pixels(20f), GridLength.Star(), GridLength.Auto],
        [GridLength.Auto],
        [
            LauncherIcons.Line(Entry.Transaction.Kind == "import" ? "folder" : "history", 16f, Future ? Theme.Quiet : Theme.Ink)
                .VAlign(VerticalAlignment.Center)
                .Cell(0, 0),
            Column([
                Text(SkinHistoryList.Label(Entry))
                    .FontSize(12f).Foreground(Future ? Theme.Quiet : Theme.Ink)
                    .Trim(TextTrimming.CharacterEllipsis).MaxLines(1).TooltipWhenTrimmed(),
                Text(string.Format(Resources.SkinRecentDetails, Entry.Model == SkinModel.Slim ? Resources.SkinSlimModel : Resources.SkinClassicModel, Entry.Size))
                    .FontSize(10f).Foreground(Future ? Theme.Quiet : Theme.Muted)
                    .Trim(TextTrimming.CharacterEllipsis).MaxLines(1).TooltipWhenTrimmed(),
            ])
                .Spacing(4f)
                .Cell(1, 0),
            Text(Resources.SkinHistoryCurrent)
                .FontSize(10f).Foreground(Theme.Muted)
                .VAlign(VerticalAlignment.Center)
                .Visible(Entry.Current)
                .Cell(2, 0),
        ])
        .ColumnSpacing(8f)
        .Padding(10f);
}