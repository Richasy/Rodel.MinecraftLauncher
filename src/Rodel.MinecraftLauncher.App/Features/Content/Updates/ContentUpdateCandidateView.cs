// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed record ContentUpdateCandidateRow(
    ContentUpdateCandidate Item,
    bool Enabled,
    bool Last,
    string Kind,
    bool RightToLeft,
    IReadOnlyDictionary<string, ImageSource> SourceIcons);

[View]
internal readonly partial record struct ContentUpdateCandidateView(LauncherAppearance Theme, ContentUpdateCandidateRow Data, Action<string> Toggle, Action<string, int> ChooseSource)
{
    internal ContentUpdateCandidate Item => Data.Item;
    internal string Reason => Item.Status == "failed" ? Resources.ContentUpdatesCheckFailed : Item.Reason switch
    {
        "pinned" => Resources.ContentUpdatesPinned,
        "untracked" => Resources.ContentUpdatesUntracked,
        "source-selection-required" => Resources.ContentUpdatesChooseSource,
        "manual-source" => Resources.ContentUpdatesManualSource,
        "already-current" => Resources.ContentUpdatesCurrent,
        "file-missing" => Resources.ContentFileMissing,
        "directory-content" => Resources.ContentUpdatesDirectory,
        "update-context-required" => Resources.ContentUpdatesContextRequired,
        _ => Resources.ContentUpdatesUnavailable,
    };
    internal Action ToggleItem
    {
        get
        {
            var file = Item.FileName;
            var toggle = Toggle;
            return () => toggle(file);
        }
    }

    internal string Help => string.Format(Resources.ContentUpdatesAccessibleItem, Item.Current.Length == 0 ? Resources.ValueNotAvailable : Item.Current, Item.Target, Item.Enabled ? Resources.ContentEnabled : Resources.ContentDisabled);
    internal ComboBoxItemSource SourceItems
    {
        get
        {
            var sources = Item.SourceCandidates;
            var theme = Theme;
            var rightToLeft = Data.RightToLeft;
            var icons = Data.SourceIcons;
            var prefix = "content.updates.source.option." + Data.Kind + "." + Item.FileName + ".";
            return new(sources.Count + 1,
                index => index == 0 ? Resources.ContentUpdatesChooseSource : sources[index - 1].Title,
                index => new ContentUpdateSourceView(theme, index == 0 ? null : sources[index - 1],
                    index > 0 && sources[index - 1].IconUrl is { } url ? icons.GetValueOrDefault(url) : null, rightToLeft),
                automationAt: index => new()
                {
                    AutomationId = prefix + (index == 0 ? "none" : sources[index - 1].ProjectId),
                    Name = index == 0 ? Resources.ContentUpdatesChooseSource : string.Format(Resources.ContentUpdatesSourceCandidate,
                        sources[index - 1].Title, sources[index - 1].Author ?? Resources.ValueNotAvailable,
                        sources[index - 1].Slug ?? sources[index - 1].ProjectId)
                });
        }
    }
    internal int SourceIndex
    {
        get
        {
            var item = Item;
            for (var index = 0; index < item.SourceCandidates.Count; index++)
                if (item.SourceCandidates[index].ProjectId == item.SelectedSource) return index + 1;
            return 0;
        }
    }
    internal Action<int> SelectSource
    {
        get
        {
            var file = Item.FileName;
            var choose = ChooseSource;
            return index => choose(file, index);
        }
    }

    /// <summary>Aligns the local icon, version and source choice while keeping optional selection at the trailing edge.</summary>
    public Ui Body => Border(Grid(
            [GridLength.Pixels(42f), GridLength.Star()],
            [GridLength.Auto],
            [
                When(Item.Local is not null,
                        new ContentLocalIcon(Item.Local!, Theme.Muted, Theme.Edge),
                        new ContentFallbackIcon(null, Theme.Muted, Theme.Edge))
                    .VAlign(VerticalAlignment.Top).Cell(0, 0),
                Column([
                    Grid([GridLength.Star(), GridLength.Auto], [GridLength.Auto], [
                        Column([
                            Text(Item.Name)
                                .FontSize(14f).FontWeight(600).Foreground(Theme.Ink)
                                .Wrap()
                                .Automation(new()
                                {
                                    Name = Item.Status == "available" ? Item.Name : string.Format(Resources.ContentUpdatesReadOnlyResult, Item.Name, Reason),
                                    AutomationId = "content.updates.result." + Data.Kind + "." + Item.FileName
                                }),
                            Flow(FlowDirection.LeftToRight, Text(Item.Status == "available"
                                    ? string.Format(Resources.ContentUpdatesVersions, Item.Current.Length == 0 ? Resources.ValueNotAvailable : Item.Current, Item.Target)
                                    : Item.Current)
                                .FontSize(12f).Foreground(Theme.Muted)
                                .Wrap()
                                .Align(Data.RightToLeft ? TextAlignment.Trailing : TextAlignment.Leading))
                                .Visible(Item.Status == "available" || Item.Current.Length > 0),
                        ]).Spacing(4f).Cell(0, 0),
                        new LauncherCheckBox(
                            Theme,
                            string.Empty,
                            Item.Selected,
                            ToggleItem,
                            Data.Enabled,
                            "content.updates.item." + Data.Kind + "." + Item.FileName,
                            Help,
                            Item.Name).Width(24f).Height(32f)
                            .VAlign(VerticalAlignment.Center)
                            .Visible(Item.Status == "available").Cell(1, 0),
                    ]).ColumnSpacing(12f),
                    Text(Reason)
                        .FontSize(12f).Foreground(Item.Status == "failed" ? Theme.Error : Theme.Muted)
                        .Wrap()
                        .Visible(Item.Status != "available" && Item.Reason != "source-selection-required"),
                    Text(Resources.ContentUpdatesKeepDisabled)
                        .FontSize(11f).Foreground(Theme.Muted)
                        .Visible(!Item.Enabled && Item.Status == "available"),
                    LauncherControls.Choice(Theme, SourceItems)
                        .ItemHeight(60f)
                        .SelectedIndex(SourceIndex)
                        .OnSelectionChanged(SelectSource)
                        .Enabled(Data.Enabled)
                        .Visible(Item.SourceCandidates.Count > 0)
                        .MinHeight(40f)
                        .Margin(new Thickness(0f, 4f, 0f, 0f))
                        .Automation(new()
                        {
                            Name = Resources.ContentUpdatesChooseSource,
                            HelpText = Item.FileName,
                            AutomationId = "content.updates.source." + Data.Kind + "." + Item.FileName
                        }),
                    Text(Item.Channel == "beta" ? Resources.ContentUpdatesBeta : Resources.ContentUpdatesAlpha)
                        .FontSize(11f).Foreground(Theme.Muted)
                        .Visible(Item.Channel is "beta" or "alpha"),
                ])
                    .Spacing(4f)
                    .Cell(1, 0),
            ])
            .ColumnSpacing(12f)
            .Padding(0f, 14f))
        .BorderBrush(Theme.Edge)
        .BorderThickness(0f, 0f, 0f, Data.Last ? 0f : 1f);
}