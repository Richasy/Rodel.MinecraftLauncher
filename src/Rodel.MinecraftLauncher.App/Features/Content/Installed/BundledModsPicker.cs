// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Content;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

/// <summary>Keeps an instance-bound installation selection inside the content toolbar's native flyout.</summary>
internal sealed partial class BundledModsPicker : Control
{
    private readonly Action<IReadOnlyList<string>> _install;

    internal BundledModsPicker(BundledModCatalog? catalog, string error, Action<IReadOnlyList<string>> install)
    {
        Catalog = catalog;
        _install = install;
        Selected = [];
        Error = error.Length > 0 ? string.Format(Resources.ContentBundledReadFailed, error) : string.Empty;
    }

    internal BundledModCatalog? Catalog { get; }
    internal partial string[] Selected { get; private set; }
    internal string Error { get; }

    private static string Description(BundledMod mod) => mod.Id switch
    {
        "rodel_ai" => Resources.ContentBundledAi,
        "bettertools" => Resources.ContentBundledTools,
        _ => Resources.ContentBundledReroll,
    };

    private static string Status(BundledMod mod) => mod.Installed
        ? Resources.ContentBundledInstalled
        : mod.InstalledVersion is not null && mod.InstalledVersion != mod.Version ? string.Format(Resources.ContentBundledReplace, mod.InstalledVersion, mod.Version) : string.Empty;

    private void Toggle(BundledMod mod, bool? selected)
    {
        var ids = Selected.ToHashSet(StringComparer.Ordinal);
        if (selected == true)
        {
            ids.Add(mod.Id);
            foreach (var dependency in mod.Requires)
            {
                if (Catalog!.Mods.Any(item => item.Id == dependency && !item.Installed))
                    ids.Add(dependency);
            }
        }
        else
        {
            ids.Remove(mod.Id);
            foreach (var dependent in Catalog!.Mods.Where(item => item.Requires.Contains(mod.Id)))
                ids.Remove(dependent.Id);
        }

        Selected = [.. ids];
    }

    private BundledModChoice[] Choices => Catalog?.Mods.Select(mod => new BundledModChoice(
LauncherAppearance.Capture(), mod.Id,
            mod.Name,
            Description(mod),
            mod.Version,
            Status(mod),
            mod.Installed || Selected.Contains(mod.Id),
            mod.Installed,
            !mod.Installed && Catalog.Incompatibility is null,
            () => Toggle(mod, !Selected.Contains(mod.Id)))).ToArray() ?? [];

    private Ui Body => Column([
        Text(Resources.ContentBundledMods)
            .FontSize(18f).FontWeight(600)
            .Margin(16f, 0f),
        ScrollView(ItemsRepeater(Choices, static choice => choice.Id, static choice => choice, new StackLayout { Spacing = 8f }).KeyboardNavigation())
            .Padding(16f, 0f)
            .MaxHeight(340f)
            .Automation(new() { AutomationId = "content.bundled.choices" }),
        Text(string.Format(Resources.ContentBundledIncompatible, string.Join("; ", Catalog?.RuntimeTargets ?? [])))
            .FontSize(12f).Foreground(LauncherTheme.Error)
            .Margin(16f, 0f)
            .Wrap()
            .Visible(Catalog?.Incompatibility is not null),
        Text(Error)
            .FontSize(12f).Foreground(LauncherTheme.Error)
            .Margin(16f, 0f)
            .Wrap()
            .Visible(Error.Length > 0),
        Grid(
            [GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                Text(Resources.ContentBundledDependencies)
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Wrap()
                    .Visible(Catalog is not null)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                LauncherControls.Command(Resources.ContentBundledInstall, () => _install([..Selected]))
                    .Style(LauncherButtons.DialogPrimary)
                    .Enabled(Selected.Length > 0 && Catalog?.Incompatibility is null)
                    .VAlign(VerticalAlignment.Center)
                    .Automation(new()
                    {
                        Name = Resources.ContentBundledInstall,
                        AutomationId = "content.bundled.install"
                    })
                    .Cell(1, 0),
            ])
            .ColumnSpacing(16f)
            .Margin(16f, 0f),
    ])
        .Spacing(16f)
        .Width(452f);
}

[Sprout.Reconcile.View]
internal readonly partial record struct BundledModChoice(
    LauncherAppearance Theme,
    string Id,
    string Name,
    string Description,
    string Version,
    string Status,
    bool Selected,
    bool Installed,
    bool Enabled,
    Action Toggle)
{
    /// <summary>Shows an icon, purpose and package state in one accessible, keyboard-operable selection row.</summary>
    public Ui Body => Sprout.Markup.CheckBox(
        Border(Grid(
                [GridLength.Auto, GridLength.Star(), GridLength.Auto],
                [GridLength.Auto],
                [
                    new BundledModArtwork(Id).VAlign(VerticalAlignment.Top)
                        .Cell(0, 0),
                    Column([
                        Text(Name)
                            .FontSize(15f).FontWeight(600).Foreground(Selected ? Theme.SelectionForeground : Theme.Ink),
                        Text(Description)
                            .FontSize(12f).Foreground(Selected ? Theme.SelectionForeground : Theme.Muted)
                            .Wrap(),
                        Row([
                            Text(Version)
                                .FontSize(11f).Foreground(Selected ? Theme.SelectionForeground : Theme.Muted),
                            Text(Status)
                                .FontSize(11f).Foreground(Installed ? Theme.SuccessForeground : Theme.Muted)
                                .Visible(Status.Length > 0),
                        ])
                            .Spacing(12f),
                    ])
                        .Spacing(5f)
                        .Cell(1, 0),
                    new LauncherCheckMark(Theme,Selected).VAlign(VerticalAlignment.Center)
                        .Cell(2, 0),
                ])
                .ColumnSpacing(12f))
            .Padding(12f)
            .Background(Selected ? Theme.SelectionBackground : Theme.ControlBackground)
            .BorderBrush(Theme.Edge)
            .BorderThickness(1f),
        Selected,
        Toggle)
        .Style(Theme.Styles.Checkbox.ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center))
        .Enabled(Enabled)
        .Automation(new()
        {
            Name = Name,
            AutomationId = "content.bundled." + Id,
            HelpText = Description
        })
        .Width(420f);
}

/// <summary>Uses the three mods' packaged artwork without network or source-tree dependencies.</summary>
[Sprout.Reconcile.View]
internal readonly partial record struct BundledModArtwork(string Id)
{
    internal ImageSource Source => ImageSource.FromFile(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "BundledMods", Id + ".png"));

    /// <summary>Draws a consistent 48-DIP identity for each bundled choice.</summary>
    public Ui Body => Image(Source)
        .Stretch(ImageStretch.Uniform)
        .Width(48f).Height(48f);
}