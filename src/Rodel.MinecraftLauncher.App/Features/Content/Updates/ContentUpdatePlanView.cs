// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

[View]
internal readonly partial record struct ContentUpdatePlanView(LauncherAppearance Theme, ContentPlanRow Data, bool RightToLeft)
{
    internal string Action => Data.Action switch
    {
        "reuse" or "reuse-local" or "reuse-or-repair" => Resources.ContentReviewReuse,
        "replace" => Resources.ContentReviewUpdate,
        _ => Resources.ContentReviewImport
    };

    /// <summary>Shows the reviewed update and dependency files on the same icon and text columns as candidate rows.</summary>
    public Ui Body => Border(Grid(
            [GridLength.Pixels(42f), GridLength.Star()],
            [GridLength.Auto],
            [
                new ContentRemoteIconView(Data.Icon, Resources.ContentIconUnavailable, Theme.Muted, Theme.Edge)
                    .VAlign(VerticalAlignment.Top)
                    .Cell(0, 0),
                Column([
                    Grid([GridLength.Star(), GridLength.Auto], [GridLength.Auto], [
                        Text(Data.Name)
                            .FontSize(14f).FontWeight(600).Foreground(Theme.Ink)
                            .Wrap().Cell(0, 0)
                            .Automation(new() { Name = Data.Name, AutomationId = "content.updates.plan." + Data.Key }),
                        Text(Action)
                            .FontSize(11f).Foreground(Theme.Muted)
                            .VAlign(VerticalAlignment.Center).Cell(1, 0),
                    ]).ColumnSpacing(12f),
                    Flow(FlowDirection.LeftToRight, Text(Data.PreviousVersion.Length > 0
                            ? string.Format(Resources.ContentUpdatesVersions, Data.PreviousVersion, Data.Version)
                            : Data.Version)
                        .FontSize(12f).Foreground(Theme.Muted)
                        .Wrap().Align(RightToLeft ? TextAlignment.Trailing : TextAlignment.Leading)),
                    Text(string.Format(Resources.ContentUpdatesRequiredBy, Data.RequiredBy))
                        .FontSize(11f).Foreground(Theme.Muted).Wrap()
                        .Visible(Data.RequiredBy.Length > 0),
                    Text(Resources.ContentUpdatesKeepDisabled)
                        .FontSize(11f).Foreground(Theme.Muted)
                        .Visible(Data.KeepDisabled),
                    Text(Data.ReleaseType == "beta" ? Resources.ContentUpdatesBeta : Resources.ContentUpdatesAlpha)
                        .FontSize(11f).Foreground(Theme.Muted)
                        .Visible(Data.ReleaseType is "beta" or "alpha"),
                ]).Spacing(4f).Cell(1, 0),
            ]).ColumnSpacing(12f).Padding(0f, 14f))
        .BorderBrush(Theme.Edge)
        .BorderThickness(0f, 0f, 0f, Data.Last ? 0f : 1f);
}