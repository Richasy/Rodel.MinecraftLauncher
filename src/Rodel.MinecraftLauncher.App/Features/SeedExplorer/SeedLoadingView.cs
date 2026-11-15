// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Shows a compact animated work surface without inventing percentage progress or replacing retained map content.</summary>
/// <param name="Title">The operation being performed.</param>
/// <param name="Phase">The actual current stage reported by the controller or original worker.</param>
/// <param name="Active">Whether visible work should animate.</param>
/// <param name="Id">The stable automation identity for the work surface.</param>
/// <param name="Progress">The actual current stage percentage, or null when that stage has no known total.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct SeedLoadingView(LauncherAppearance Theme, string Title, string Phase, bool Active, string Id, double? Progress = null)
{
    /// <summary>Combines the map motif, actual stage and a visible native activity sweep which stops when work ends.</summary>
    public Ui Body => Border(Column([
        Grid([GridLength.Pixels(28f), GridLength.Star()], [GridLength.Auto], [
            LauncherIcons.Line("seed-generate", 28f).VAlign(VerticalAlignment.Center).Cell(0, 0),
            Column([
                Text(Title).FontSize(14f).Wrap().Automation(new() { Name = Title, AutomationId = Id }),
                Text(Phase).Style(LauncherStyles.Text.FontSize(11f).MaxLines(1).Trimming(TextTrimming.CharacterEllipsis))
                    .Foreground(Theme.Muted).TooltipWhenTrimmed().Automation(new() { Name = Phase, AutomationId = Id + ".phase" }),
            ]).Spacing(5f).Cell(1, 0),
        ]).ColumnSpacing(12f),
        ProgressBar().Indeterminate(Progress is null).Value(Math.Clamp(Progress ?? 0, 0, 100)).Thickness(3f).Foreground(Theme.ProgressAccent)
            .Duration(TimeSpan.FromSeconds(1.4)).Visible(Active).Automation(new() { Name = Title, AutomationId = Id + ".activity" }),
    ]).Spacing(16f)).Padding(18f).Width(300f).Background(Theme.Materials.Acrylic).BorderBrush(Theme.Edge).BorderThickness(1f);
}