// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Import;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

[View]
internal readonly partial record struct ContentActionBody(
    LauncherAppearance Theme,
    string Explanation,
    string Source,
    bool IsImport,
    bool IsEnabled,
    string Status,
    string Error,
    bool Failed,
    string SourceLabel,
    string BrowseName,
    Action<string> Change,
    Action Browse,
    TextBoxStyle InputStyle,
    ButtonStyle BrowseStyle,
    Color Ink,
    Color Muted,
    Color ErrorColor)
{
    public Ui Body => Column([
        Text(Explanation)
            .FontSize(14f).Foreground(Ink)
            .Wrap(),
        new ImportPathInput(
Theme,            SourceLabel,
            Source,
            IsEnabled,
            Change,
            Browse,
            "content.operation.source",
            "content.operation.browse",
            BrowseName,
            InputStyle,
            BrowseStyle,
            IsFile: true).Visible(IsImport),
        Text(Status)
            .FontSize(13f).Foreground(Failed ? ErrorColor : Ink)
            .Wrap()
            .Visible(Status.Length > 0)
            .Automation(new()
            {
                Name = Status,
                AutomationId = "content.operation.status",
                LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
            }),
        Text(Error)
            .FontSize(12f).Foreground(Muted)
            .Wrap()
            .Visible(Failed),
    ])
        .Spacing(12f)
        .Padding(24f, 20f);
}