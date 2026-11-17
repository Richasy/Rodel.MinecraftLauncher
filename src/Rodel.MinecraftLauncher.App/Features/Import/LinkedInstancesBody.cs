// Copyright (c) Richasy. All rights reserved.

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
    internal static Func<LinkedInstanceEntry, LinkedInstanceRow> RowTemplate(LauncherAppearance theme, bool enabled,
        Action<LinkedInstanceEntry, bool, string?> update) => entry =>
    {
        var preview = entry.Preview;
        return new(theme, preview, entry.Selected, entry.GameDirectory, enabled,
            () => update(entry, !entry.Selected, entry.GameDirectory),
            () => update(entry, entry.Selected, preview.MinecraftDirectory),
            () => update(entry, entry.Selected, preview.GameDirectoryCandidates.LastOrDefault()));
    };

    /// <summary>Groups the shared source selector and version list within the dialog's independently scrolling body.</summary>
    public Ui Body => Column([
        Text(Resources.LinkExistingHelp).FontSize(12f).Foreground(Theme.Muted).Wrap(),
        Grid([GridLength.Star(), GridLength.Auto], [GridLength.Auto], [
            TextBox(Source, ChangeSource).Enabled(!Busy).Style(LauncherStyles.Input)
                .Automation(new() { Name = Resources.ImportDirectory, AutomationId = "link.directory" }).Cell(0, 0),
            LauncherControls.OutlineCommand(Theme, Row([LauncherIcons.Line("folder", 18f)]), Browse)
                .Style(Theme.Buttons.BrowseButton).Enabled(!Busy).Width(40f).Height(40f)
                .ToolTip(string.Format(Resources.BrowsePath, Resources.ImportDirectory))
                .Automation(new() { Name = string.Format(Resources.BrowsePath, Resources.ImportDirectory), AutomationId = "link.browse" }).Cell(1, 0),
        ]).ColumnSpacing(8f),
        ProgressBar().Indeterminate(true).Visible(Scanning),
        Text(Resources.LinkNoVersions).Wrap().Foreground(Theme.Muted).Visible(HasNoVersions),
        new LauncherCheckBox(Theme, Resources.LinkSelectAll, Entries.Length > 0 && Entries.All(entry => entry.Selected),
            ToggleAll, !Busy && !Scanning, "link.all").Visible(Entries.Length > 0),
        Border(ItemsRepeater(Entries, entry => entry.Key, RowTemplate(Theme, !Busy && !Scanning, UpdateEntry)).KeyboardNavigation())
            .Background(Theme.ControlBackground).BorderBrush(Theme.Edge).BorderThickness(1f)
            .Visible(Entries.Length > 0),
        Text(Error).FontSize(12f).Foreground(Theme.Error).Wrap().Visible(Error.Length > 0)
            .Automation(new() { Name = Error, AutomationId = "link.error" }),
    ]).Spacing(12f).Padding(new Thickness(24f, 0f, 24f, 20f));
}
