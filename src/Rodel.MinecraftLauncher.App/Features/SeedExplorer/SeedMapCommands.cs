// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Groups map-specific menus above the map without reserving a separate header row.</summary>
internal sealed partial class SeedMapCommands(SeedExplorerState state, SeedExplorerPopups popups) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private bool Compact => Current.MapWidth < (Current.WorldSource && !Current.SearchMode ? 1000f : 640f);
    private bool Ready => Current.Catalog is not null && !Current.Opening;
    private DropDownButtonStyle MenuAppearance => (Compact ? SeedChrome.IconDropDown : SeedChrome.DropDown)
        .Button(Compact ? SeedChrome.MapTool : SeedChrome.MapTool.Padding(8f, 4f));
    private Ui Body => Border(Overlay([
        new LauncherMaterialSurface(LauncherMaterials.Acrylic),
        Row([
            new SeedMapMenu(Compact, Resources.SeedStructures, "home", popups.Structures, "seed.structures.open", Ready, MenuAppearance, LauncherTheme.Ink),
            new SeedMapMenu(Compact, Resources.SeedAnalyzeArea, "skin-selection", popups.Region, "seed.area.open", Ready, MenuAppearance, LauncherTheme.Ink),
            new SeedMapMenu(Compact, Resources.SeedLayers, "map-layers", popups.Layers, "seed.layers.open", Current.Catalog is not null, MenuAppearance, LauncherTheme.Ink),
            new SeedMapMenu(Compact, Resources.SeedPlayerLocation, "player", popups.Player, "seed.player.open", Current.WorldSnapshot is not null, MenuAppearance, LauncherTheme.Ink)
                .Visible(Current.WorldSource && !Current.SearchMode),
            DropDownButton(new SeedDropDownIconContent("search"), popups.Locate).Style(SeedChrome.IconDropDown.Button(SeedChrome.MapTool))
                .Width(32f).Height(32f).ToolTip(Resources.SeedLocate).Enabled(!Current.Opening)
                .Automation(new() { Name = Resources.SeedLocate, AutomationId = "seed.locate.open" }),
        ]).Spacing(4f).Padding(2f),
    ])).BorderBrush(LauncherTheme.Edge).BorderThickness(1f)
        .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top).Margin(10f);
}

[View]
internal readonly partial record struct SeedMapMenu(bool Compact, string Label, string Icon, Flyout Menu, string Id, bool Enabled, DropDownButtonStyle Appearance, Color Ink)
{
    /// <summary>Keeps a labeled map menu at normal widths and a named icon menu when the map becomes narrow.</summary>
    public Ui Body => DropDownButton(new SeedMapMenuContent(Compact, Label, Icon, Ink), Menu)
        .Style(Appearance)
        .MinWidth(32f).MinHeight(32f).ToolTip(Compact ? Label : string.Empty).Enabled(Enabled).Automation(new() { Name = Label, AutomationId = Id });
}

[View]
internal readonly partial record struct SeedMapMenuContent(bool Compact, string Label, string Icon, Color Ink)
{
    /// <summary>Provides the current theme fallback while retaining native hover and disabled foreground inheritance.</summary>
    public TextView LabelView => Text(Label).FontSize(12f).Foreground(Ink) with { InheritsForeground = true };
    /// <summary>Supplies concrete native dropdown content for either the compact icon or full label.</summary>
    public Ui Body => Row([
        LauncherIcons.Line(Icon, 16f).Visible(Compact),
        LabelView.Visible(!Compact),
    ]);
}