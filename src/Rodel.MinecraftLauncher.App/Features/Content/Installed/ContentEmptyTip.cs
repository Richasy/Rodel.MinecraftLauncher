// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Instances;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed partial class ContentEmptyTip(ContentPage page) : Control
{
    private readonly ContentEmptyDetails _details = new(page);
    internal partial float ViewportHeight { get; set; }

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Star(), GridLength.Auto, GridLength.Star()],
        [
            _details.Automation(new() { AutomationId = "content.empty.group" })
                .Cell(0, 1),
        ])
        .MinHeight(ViewportHeight);
}

internal sealed partial class ContentEmptyDetails(ContentPage page) : Control
{
    private string Title => page.State.Loading ? Resources.ContentLoading : string.Format(Resources.ContentEmptyHeading, page.Kind.Title());

    private Ui Body => Border(Column([
            LauncherIcons.Line("instances", 36f, LauncherTheme.Muted)
                .HAlign(HorizontalAlignment.Center)
                .Visible(!page.Compact)
                .Automation(new() { AutomationId = "content.empty.icon" }),
            Text(Title)
                .FontSize(19f).FontWeight(600).Foreground(LauncherTheme.Ink)
                .Wrap()
                .Align(TextAlignment.Center)
                .Automation(new()
                {
                    Name = Title,
                    AutomationId = "content.empty",
                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                }),
            Text(string.Format(Resources.ContentEmptyDescription, page.Kind.Title(), page.Home.Selected?.Name ?? string.Empty))
                .FontSize(12f).Foreground(LauncherTheme.Muted)
                .Wrap()
                .Align(TextAlignment.Center)
                .Visible(!page.State.Loading)
                .Automation(new() { AutomationId = "content.empty.description" }),
        ])
            .Spacing(page.Compact ? 8f : 12f))
        .Padding(page.Compact ? 12f : 20f);
}