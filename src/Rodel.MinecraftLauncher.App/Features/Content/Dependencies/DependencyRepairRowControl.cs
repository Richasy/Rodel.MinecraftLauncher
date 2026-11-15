// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Content;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed record DependencyRepairRow(
    string ProjectId,
    string Name,
    string? IconUrl,
    string RequestedBy,
    IReadOnlyList<ContentVersion> Versions,
    int Selected,
    string VersionText,
    bool Verified,
    bool Compact,
    bool Editable)
{
    internal int Key { get; init; }
    internal bool ReusesLocal { get; init; }
}

[View]
internal readonly partial record struct DependencyRepairRowView(DependencyRepairRow Data, Action<string, int> Choose, Action<string, string> Change)
{
    public Ui Body => new DependencyRepairRowControl(Data, Choose, Change);
}

internal sealed partial class DependencyRepairRowControl : Control
{
    private readonly Action<string, int> _choose;
    private readonly Action<string, string> _change;

    internal DependencyRepairRowControl(DependencyRepairRow data, Action<string, int> choose, Action<string, string> change)
    {
        Data = data;
        _choose = choose;
        _change = change;
    }

    internal partial DependencyRepairRow Data { get; private set; }

    private Ui Body => Border(Grid(
            Data.Compact ? [GridLength.Pixels(42f), GridLength.Star()] : [GridLength.Pixels(42f), GridLength.Star(), GridLength.Pixels(208f)],
            Data.Compact ? [GridLength.Auto, GridLength.Auto] : [GridLength.Auto],
            [
                new ContentRemoteIconView(Data.IconUrl, Resources.ContentIconUnavailable, LauncherTheme.Muted, LauncherTheme.Edge).VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                Column([
                    Text(Data.Name)
                        .FontSize(14f).FontWeight(600).Foreground(LauncherTheme.Ink)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                        .Automation(new()
                        {
                            Name = Data.Name,
                            AutomationId = "content.repair.name." + Data.ProjectId
                        }),
                    Text(string.Format(Resources.DependencyRepairRequestedBy, Data.RequestedBy))
                        .FontSize(11f).Foreground(LauncherTheme.Muted)
                        .Wrap()
                        .Visible(Data.RequestedBy.Length > 0),
                    Text(Resources.DependencyRepairSelectionPending)
                        .FontSize(11f).Foreground(LauncherTheme.Muted)
                        .Wrap()
                        .Visible(!Data.Verified),
                    Text(Resources.DependencyRepairUseLocal)
                        .FontSize(11f).Foreground(LauncherTheme.Muted)
                        .Wrap()
                        .Visible(Data.ReusesLocal),
                ])
                    .Spacing(5f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(1, 0),
                LauncherControls.Choice([..Data.Versions.Select(version => version.Number)])
                    .IsEditable(true)
                    .Text(Data.VersionText)
                    .OnTextChanged(text => _change(Data.ProjectId, text))
                    .FilterMode(ComboBoxFilterMode.Contains)
                    .SelectedIndex(Data.Selected)
                    .OnSelectionChanged(index => _choose(Data.ProjectId, index))
                    .Enabled(Data.Editable)
                    .MinHeight(40f)
                    .ToolTip(Data.VersionText)
                    .Automation(new()
                    {
                        Name = string.Format(Resources.DependencyRepairVersion, Data.Name),
                        AutomationId = "content.repair.version." + Data.ProjectId
                    })
                    .VAlign(VerticalAlignment.Center)
                    .Cell(Data.Compact ? 1 : 2, Data.Compact ? 1 : 0),
            ])
            .ColumnSpacing(16f).RowSpacing(Data.Compact ? 10f : 0f)
            .Padding(0f, 16f))
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(0f, 0f, 0f, 1f);

    public override void ReconcileFrom(Control source)
    {
        if (source is DependencyRepairRowControl row)
            Data = row.Data;
    }
}