// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

/// <summary>One responsive label and selector layout for launcher preferences.</summary>
[View]
internal readonly partial record struct SettingsChoiceRow(
    LauncherAppearance Theme,
    string Title,
    string[] Items,
    int SelectedIndex,
    Action<int> Choose,
    float AvailableWidth,
    Color Ink,
    Color Muted,
    ComboBoxPalette Palette,
    ButtonStyle Face,
    FlyoutStyle Menu,
    string AutomationId,
    string Description = "",
    bool Enabled = true)
{
    internal bool Compact => AvailableWidth < 760f;
    internal GridLength[] Columns => Compact ? [GridLength.Star()] : [GridLength.Star(), GridLength.Pixels(AvailableWidth < 850f ? 190f : 240f)];
    internal GridLength[] Rows => Compact ? [GridLength.Auto, GridLength.Auto] : [GridLength.Auto];

    /// <summary>Preserves the real selector and label while adapting to the available setting width.</summary>
    public Ui Body => Grid(
        Columns,
        Rows,
        [
            Column([
                Text(Title)
                    .FontSize(14f).Foreground(Ink)
                    .Wrap(),
                Text(Description)
                    .FontSize(12f).Foreground(Muted)
                    .Wrap()
                    .Visible(Description.Length > 0),
            ])
                .Spacing(6f)
                .VAlign(VerticalAlignment.Center)
                .Cell(0, 0),
            LauncherControls.Choice(Theme,Items)
                .SelectedIndex(SelectedIndex)
                .OnSelectionChanged(Choose)
                .Enabled(Enabled)
                .Palette(Palette)
                .FaceplateStyle(Face)
                .DropDownStyle(Menu)
                .MinHeight(40f)
                .VAlign(VerticalAlignment.Center)
                .Automation(new()
                {
                    Name = Title,
                    HelpText = Description,
                    AutomationId = AutomationId
                })
                .Cell(Compact ? 0 : 1, Compact ? 1 : 0),
        ])
        .ColumnSpacing(24f).RowSpacing(8f);
}