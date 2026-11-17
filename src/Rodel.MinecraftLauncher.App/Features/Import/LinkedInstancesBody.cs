// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Instances;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Import;

internal sealed record LinkedInstanceEntry(InstanceImportPreview Preview, bool Selected, string? GameDirectory)
{
    internal string Key => Preview.VersionId + "/" + Preview.ProfileId;
}

[View]
internal readonly partial record struct LinkedInstancesBody(
    LauncherAppearance Theme,
    string Source,
    LinkedInstanceEntry[] Entries,
    bool Busy,
    bool Scanning,
    bool HasNoVersions,
    string Error,
    Action<string> ChangeSource,
    Action Browse,
    Action ToggleAll,
    Action<LinkedInstanceEntry, bool, string?> UpdateEntry)
{
    /// <summary>Keeps the source field fixed while the available result viewport hosts exactly one initial, loading, empty or populated state.</summary>
    public Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Star()],
        [
            Column([
                Text(Resources.LinkExistingHelp).FontSize(12f).Foreground(Theme.Muted).Wrap(),
                Grid([GridLength.Star(), GridLength.Auto], [GridLength.Auto], [
                    TextBox(Source, ChangeSource).Enabled(!Busy).Style(LauncherStyles.Input)
                        .Automation(new() { Name = Resources.ImportDirectory, AutomationId = "link.directory" }).Cell(0, 0),
                    LauncherControls.OutlineCommand(Theme, Row([LauncherIcons.Line("folder", 18f)]), Browse)
                        .Style(Theme.Buttons.BrowseButton).Enabled(!Busy).Width(40f).Height(40f)
                        .ToolTip(string.Format(Resources.BrowsePath, Resources.ImportDirectory))
                        .Automation(new() { Name = string.Format(Resources.BrowsePath, Resources.ImportDirectory), AutomationId = "link.browse" }).Cell(1, 0),
                ]).ColumnSpacing(8f),
            ]).Spacing(12f).Cell(0, 0),
            Overlay([
                new EmptyPlaceholderView("folder", Resources.LinkEmptyTitle, Resources.LinkEmptyDescription,
                    Theme.Ink, Theme.Muted, "link.empty")
                    .MaxWidth(400f).HAlign(HorizontalAlignment.Center).Margin(12f)
                    .Visible(string.IsNullOrWhiteSpace(Source) && !Scanning && Entries.Length == 0 && Error.Length == 0),
                new LoadingPlaceholderView(Theme, Resources.ImportPreviewing, "link.scanning")
                    .Visible(Scanning),
                new EmptyPlaceholderView("instances", Resources.ImportLocalVersions, Resources.LinkNoVersions,
                    Theme.Ink, Theme.Muted, "link.no-versions")
                    .MaxWidth(400f).HAlign(HorizontalAlignment.Center).Margin(12f)
                    .Visible(HasNoVersions && !Scanning && Error.Length == 0),
                ScrollView(new LinkedInstancesList(Theme, Entries, !Busy && !Scanning, Error, ToggleAll, UpdateEntry))
                    .Visible(!Scanning && (Entries.Length > 0 || Error.Length > 0))
                    .Automation(new() { Name = Resources.ImportLocalVersions, AutomationId = "link.results" }),
            ]).Cell(0, 1),
        ])
        .RowSpacing(12f)
        .Padding(new Thickness(24f, 0f, 24f, 20f));
}

[View]
internal readonly partial record struct LinkedInstancesList(
    LauncherAppearance Theme,
    LinkedInstanceEntry[] Entries,
    bool Enabled,
    string Error,
    Action ToggleAll,
    Action<LinkedInstanceEntry, bool, string?> UpdateEntry)
{
    internal static Func<LinkedInstanceEntry, LinkedInstanceRow> RowTemplate(LauncherAppearance theme, bool enabled,
        Action<LinkedInstanceEntry, bool, string?> update) => entry =>
    {
        var preview = entry.Preview;
        return new(theme, preview, entry.Selected, entry.GameDirectory, enabled,
            () => update(entry, !entry.Selected, entry.GameDirectory),
            () => update(entry, entry.Selected, preview.MinecraftDirectory),
            () => update(entry, entry.Selected, preview.GameDirectoryCandidates.LastOrDefault()));
    };

    /// <summary>Scrolls version selection and actual errors without displacing the source field or dialog actions.</summary>
    public Ui Body => Column([
        new LauncherCheckBox(Theme, Resources.LinkSelectAll, Entries.Length > 0 && Entries.All(entry => entry.Selected),
            ToggleAll, Enabled, "link.all").Visible(Entries.Length > 0),
        Border(ItemsRepeater(Entries, entry => entry.Key, RowTemplate(Theme, Enabled, UpdateEntry)).KeyboardNavigation())
            .Background(Theme.ControlBackground).BorderBrush(Theme.Edge).BorderThickness(1f)
            .Visible(Entries.Length > 0),
        Text(Error).FontSize(12f).Foreground(Theme.Error).Wrap().Visible(Error.Length > 0)
            .Automation(new() { Name = Error, AutomationId = "link.error" }),
    ]).Spacing(12f);
}
