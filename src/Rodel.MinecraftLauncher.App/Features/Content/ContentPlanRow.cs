// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Content;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed record ContentPlanRow(
    int Key,
    string Name,
    string? Icon,
    string Version,
    string Kind,
    string Action)
{
    internal string RequiredBy { get; init; } = string.Empty;
    internal bool KeepDisabled { get; init; }
    internal bool Last { get; init; }
    internal string PreviousVersion { get; init; } = string.Empty;
    internal string ReleaseType { get; init; } = string.Empty;

    internal static ContentPlanRow Read(ContentPlanNode value, int index) => new(
        index,
        value.Title ?? value.FileName,
        value.IconUrl,
        value.Upstream["version_number"]?.ToString() ?? value.VersionId,
        value.Kind,
        value.ReuseLocal is not null ? "reuse" : value.Change);

    internal static IReadOnlyList<ContentPlanRow> ReadAll(ContentInstallPlan plan, IReadOnlyDictionary<string, string>? previous = null)
    {
        var nodes = plan.Nodes;
        var names = nodes.ToDictionary(node => node.ProjectId, node => node.Title ?? node.FileName);
        var edges = plan.Edges;
        return [..nodes.Select((node, index) => Read(node, index)with
                {
                    Last = index == nodes.Count - 1,
                    PreviousVersion = previous?.GetValueOrDefault(node.ProjectId) ?? string.Empty,
                    ReleaseType = node.Upstream["version_type"]?.ToString() ?? string.Empty,
                    RequiredBy = string.Join(", ", edges.Where(edge => edge.ProjectId == node.ProjectId).Select(edge => edge.RequiredBy).Where(parent => parent is not null && names.ContainsKey(parent)).Select(parent => names[parent!]).Distinct()),
                    KeepDisabled = node.DesiredEnabled == false || (node.DesiredEnabled is null && node.ExistingFiles.Count > 0 && node.ExistingFiles.All(file => file["enabled"]?.GetValue<bool>() == false)),
                })];
    }
}

[View]
internal readonly partial record struct ContentPlanRowView(LauncherAppearance Theme, ContentPlanRow Data, bool RightToLeft = false)
{
    public Ui Body => Border(Grid(
            [GridLength.Pixels(42f), GridLength.Star()],
            [GridLength.Auto],
            [
                new ContentRemoteIconView(Data.Icon, Resources.ContentIconUnavailable, Theme.Muted, Theme.Edge).Cell(0, 0),
                Column([
                    Text(Data.Name)
                        .FontSize(14f).FontWeight(600).Foreground(Theme.Ink)
                        .Wrap(),
                    Flow(FlowDirection.LeftToRight, Text(string.Format(Resources.ContentUpdatesVersions, Data.PreviousVersion, Data.Version))
                            .FontSize(12f).Foreground(Theme.Ink)
                            .Wrap()
                            .Align(RightToLeft ? TextAlignment.Trailing : TextAlignment.Leading))
                        .Visible(Data.PreviousVersion.Length > 0),
                    Text(string.Format(Data.PreviousVersion.Length > 0 ? Resources.ContentUpdatesPlanAction : Resources.ContentPlanItem, "\u2066" + Data.Version + "\u2069", Data.Kind switch
                            {
                                "resourcepacks" => Resources.ContentResourcePacks,
                                "shaderpacks" => Resources.ContentShaderPacks,
                                _ => Resources.ContentMods
                            }, Data.Action switch
                            {
                                "reuse" or "reuse-local" or "reuse-or-repair" => Resources.ContentReviewReuse,
                                "replace" => Resources.ContentReviewUpdate,
                                _ => Resources.ContentReviewImport
                            }))
                        .FontSize(11f).Foreground(Theme.Muted)
                        .Wrap(),
                    Text(string.Format(Resources.ContentUpdatesRequiredBy, Data.RequiredBy))
                        .FontSize(11f).Foreground(Theme.Muted)
                        .Wrap()
                        .Visible(Data.RequiredBy.Length > 0),
                    Text(Resources.ContentUpdatesKeepDisabled)
                        .FontSize(11f).Foreground(Theme.Muted)
                        .Visible(Data.KeepDisabled),
                    Text(Data.ReleaseType == "beta" ? Resources.ContentUpdatesBeta : Resources.ContentUpdatesAlpha)
                        .FontSize(11f).Foreground(Theme.Muted)
                        .Visible(Data.ReleaseType is "beta" or "alpha"),
                ])
                    .Spacing(5f)
                    .Cell(1, 0),
            ])
            .ColumnSpacing(12f)
            .Padding(0f, 12f))
        .BorderBrush(Theme.Edge)
        .BorderThickness(0f, 0f, 0f, Data.Last ? 0f : 1f);
}