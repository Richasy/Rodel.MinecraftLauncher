// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Content;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

/// <summary>Contains the inspected source, content metadata and current review actions for one list row.</summary>
/// <param name="Key">Stable row identity retained across removal and repeat inspection.</param>
/// <param name="Source">Absolute source path displayed without editing the selected file.</param>
/// <param name="FileName">Original file name used to identify row actions accessibly.</param>
/// <param name="Item">Inspected name, version and lazy local icon source.</param>
/// <param name="Issue">Current inspection issue, empty when the source can be applied.</param>
/// <param name="Replacement">Reviewed installed file names replaced on confirmation, empty for ordinary imports; localization happens during display.</param>
/// <param name="Candidates">Content interpretations available when inspection is ambiguous.</param>
/// <param name="Selected">Index of the currently reviewed interpretation.</param>
/// <param name="Editable">Whether row actions may change the pending selection.</param>
internal sealed record ContentFileReviewRow(
    int Key,
    string Source,
    string FileName,
    ContentItem Item,
    string Issue,
    string Replacement,
    IReadOnlyList<ContentKind?> Candidates,
    int Selected,
    bool Editable)
{
    internal bool HasSeparator { get; init; }
}

[View]
internal readonly partial record struct ContentFileReviewView(ContentFileReviewRow Data, Action<string, int> Choose, Action<string> Remove)
{
    /// <summary>Builds the reviewed source row with its type selection and removal action.</summary>
    public Ui Body => new ContentFileReviewControl(Data, Choose, Remove);
}

internal sealed partial class ContentFileReviewControl : Control
{
    private readonly Action<string, int> _choose;
    private readonly Action<string> _remove;

    internal ContentFileReviewControl(ContentFileReviewRow data, Action<string, int> choose, Action<string> remove)
    {
        Data = data;
        _choose = choose;
        _remove = remove;
    }

    internal partial ContentFileReviewRow Data { get; private set; }

    private Ui Body => Border(Grid(
        [GridLength.Auto, GridLength.Star(), GridLength.Auto],
        [GridLength.Auto],
        [
            new ContentLocalIcon(Data.Item, LauncherTheme.Muted, LauncherTheme.Edge)
                .VAlign(VerticalAlignment.Top)
                .Cell(0, 0),
            Column([
                Text(Data.Item.Name)
                    .FontSize(14f).FontWeight(600).Foreground(LauncherTheme.Ink)
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
                Text(Data.Item.Version)
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                    .Visible(Data.Item.Version.Length > 0),
                Text(Data.Source)
                    .FontSize(11f).Foreground(LauncherTheme.Muted)
                    .NoWrap().MaxLines(1)
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                    .Automation(new()
                    {
                        Name = Data.Source,
                        HelpText = Resources.ContentSource,
                        AutomationId = "content.files.source." + Data.Key
                    }),
                Text(string.Format(Resources.ContentFilesReplaceMods, Data.Replacement))
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Wrap()
                    .Visible(Data.Replacement.Length > 0),
                LauncherControls.Choice([..Data.Candidates.Select(kind => kind?.Title() ?? Resources.ContentAutoType)])
                    .SelectedIndex(Data.Selected)
                    .OnSelectionChanged(index => _choose(Data.Source, index))
                    .Enabled(Data.Editable)
                    .Visible(Data.Candidates.Count > 1)
                    .MinHeight(40f)
                    .Automation(new()
                    {
                        Name = Resources.ContentAutoType,
                        HelpText = Data.FileName,
                        AutomationId = "content.files.kind." + Data.Key
                    }),
                Text(string.Format(Resources.ContentReviewIssue, Data.Issue switch
                {
                    "duplicate-mod-id" => Resources.ContentFilesDuplicateMod,
                    "partial-mod-id-replacement" => Resources.ContentFilesPartialMod,
                    _ => Data.Issue,
                }))
                    .FontSize(12f).Foreground(LauncherTheme.Error)
                    .Wrap()
                    .Visible(Data.Issue.Length > 0),
            ])
                .Spacing(8f)
                .VAlign(VerticalAlignment.Top)
                .Cell(1, 0),
            Button(Row([LauncherIcons.Line("delete", 16f)]), () => _remove(Data.Source))
                .Style(LauncherButtons.CriticalIcon.Padding(0f))
                .Width(32f).Height(32f)
                .VAlign(VerticalAlignment.Center)
                .Enabled(Data.Editable)
                .ToolTip(Resources.ContentRemove)
                .Automation(new()
                {
                    Name = Resources.ContentRemove,
                    HelpText = Data.FileName,
                    AutomationId = "content.files.remove." + Data.Key
                })
                .Cell(2, 0),
        ])
            .ColumnSpacing(12f)
            .Padding(0f, 12f))
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(0f, 0f, 0f, Data.HasSeparator ? 1f : 0f);

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is ContentFileReviewControl row)
            Data = row.Data;
    }
}