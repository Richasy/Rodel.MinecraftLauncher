// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed record ContentRowAppearance(
    Color Ink,
    Color Muted,
    Color Edge,
    Brush Surface,
    ButtonStyle Command,
    ButtonStyle Critical,
    ToggleSwitchPalette Toggle)
{
    internal static ContentRowAppearance Capture() => new(
        LauncherTheme.Ink,
        LauncherTheme.Muted,
        LauncherTheme.Edge,
        LauncherTheme.ControlBackground,
        LauncherButtons.OutlineButton,
        LauncherButtons.CriticalIcon,
        LauncherStyles.ContentToggle);
}

internal sealed record ContentRowState(
    string Key,
    ContentRowAppearance Appearance,
    string InstanceId,
    ContentKind Kind,
    bool CanChange,
    bool Compact,
    bool RightToLeft,
    ContentItem? Local = null,
    ContentSearchHit? Remote = null,
    string? Heading = null,
    string? Message = null,
    bool Installed = false,
    bool SearchOnly = false,
    ContentSearchNotice? SearchNotice = null);

[View]
internal readonly partial record struct ContentListRow(LauncherAppearance Theme, ContentRowState Data, ContentPage Page)
{
    public Ui Body => When(Data.SearchNotice is not null, new ContentSearchNoticeView(Data.SearchNotice, Data.Appearance.Ink, Data.Appearance.Muted), When(Data.Heading is not null, new ContentGroupRow(Data), When(Data.Local is not null, When(Data.SearchOnly, new ContentInstalledSearchRow(Data), new ContentLocalRow(Data, Page)), When(Data.Remote is not null, new ContentRemoteRow(Theme, Data, Page), new ContentNoticeRow(Data)))));
}

[View]
internal readonly partial record struct ContentGroupRow(ContentRowState Data)
{
    public Ui Body => Border(Text(Data.Heading ?? string.Empty)
            .FontSize(12f).FontWeight(600).Foreground(Data.Appearance.Muted)
            .Wrap()
            .Automation(new()
            {
                Name = Data.Heading ?? string.Empty,
                AutomationId = "content.group." + Data.Key
            }))
        .Padding(20f, 10f)
        .Background(Data.Appearance.Surface);
}

[View]
internal readonly partial record struct ContentNoticeRow(ContentRowState Data)
{
    public Ui Body => Border(Text(Data.Message ?? string.Empty)
            .FontSize(12f).Foreground(Data.Appearance.Muted)
            .Wrap()
            .Automation(new()
            {
                Name = Data.Message ?? string.Empty,
                AutomationId = "content.notice." + Data.Key
            }))
        .Padding(20f, 12f);
}