// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed record ContentSearchNotice(ContentSearchNoticeKind Kind, string Error = "")
{
    internal string Icon => Kind switch
    {
        ContentSearchNoticeKind.Empty => "search-empty",
        ContentSearchNoticeKind.Failed => "cloud-off",
        _ => "search"
    };
    internal string Title => Kind switch
    {
        ContentSearchNoticeKind.Empty => Resources.ContentSearchEmptyTitle,
        ContentSearchNoticeKind.Failed => Resources.ContentSearchErrorTitle,
        _ => Resources.ContentSearchStartTitle,
    };
    internal string Description => Kind switch
    {
        ContentSearchNoticeKind.Empty => Resources.ContentSearchEmptyDescription,
        ContentSearchNoticeKind.Failed => Resources.ContentSearchErrorDescription,
        _ => Resources.ContentSearchStartDescription,
    };
}

[View]
internal readonly partial record struct ContentSearchNoticeView(ContentSearchNotice? Notice, Color Ink, Color Muted)
{
    public Ui Body => Border(Column([
            LauncherIcons.Line(Notice?.Icon ?? "search", 36f, Muted)
                .HAlign(HorizontalAlignment.Center),
            Text(Notice?.Title ?? string.Empty)
                .FontSize(14f).FontWeight(600).Foreground(Ink)
                .Wrap()
                .Align(TextAlignment.Center)
                .Automation(new()
                {
                    Name = Notice?.Title ?? string.Empty,
                    AutomationId = "content.search.notice.title",
                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                }),
            Text(Notice?.Description ?? string.Empty)
                .FontSize(12f).Foreground(Muted)
                .Wrap()
                .Align(TextAlignment.Center)
                .MaxLines(5).Trim(TextTrimming.CharacterEllipsis)
                .ToolTip(Notice?.Error ?? string.Empty)
                .Automation(new()
                {
                    Name = Notice?.Description ?? string.Empty,
                    HelpText = Notice?.Error ?? string.Empty,
                    AutomationId = "content.search.notice.description"
                }),
        ])
            .Spacing(10f))
        .Padding(24f, 28f)
        .MaxWidth(460f)
        .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center);
}