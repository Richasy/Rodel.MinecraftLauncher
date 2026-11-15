// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Shows map status without reserving space for legends or the overlaid scale.</summary>
internal sealed partial class SeedExplorerFooter : Control
{
    private readonly SeedExplorerState state;
    private partial string Status { get; set; }
    private partial string MarkerStatus { get; set; }
    internal SeedExplorerFooter(SeedExplorerState state, string status, string markerStatus) { this.state = state; Status = status; MarkerStatus = markerStatus; }
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) { if (source is SeedExplorerFooter other) { Status = other.Status; MarkerStatus = other.MarkerStatus; } }
    private SeedExplorerSnapshot Current => state.Current;
    private string Feedback => Status == Resources.SeedReady ? MarkerStatus : string.Format(Resources.SeedMapStatusFormat, Status, MarkerStatus);
    private string ViewportText => string.Format(Resources.SeedSamplingFormat, Current.Tiles.Length == 0 ? 0 : SeedMapPlanning.Step(SeedMapTiles.View(Current)), (int)Current.CenterX, (int)Current.CenterZ);
    private string WorldContext => string.Format(Resources.SeedWorldContextFormat, Current.WorldSnapshot?.Version ?? string.Empty, Current.WorldSnapshot?.Seed ?? Resources.SeedSeedUnavailable);
    /// <summary>Keeps save provenance, copy, query feedback and viewport coordinates on one fixed-height footer.</summary>
    public Ui Body => Border(Grid([GridLength.Auto, GridLength.Star(), GridLength.Auto], [GridLength.Star()], [
        Row([
            Text(WorldContext).Style(LauncherStyles.Text.FontSize(10f).MaxLines(1).Trimming(TextTrimming.CharacterEllipsis)).Foreground(LauncherTheme.Muted)
                .MaxWidth(Current.Width < 1000f ? 190f : 300f).VAlign(VerticalAlignment.Center).FlowDirection(FlowDirection.LeftToRight).TooltipWhenTrimmed()
                .Automation(new() { Name = WorldContext, AutomationId = "seed.world.context" }),
            new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedCopySeed, "copy", () => { if (Current.WorldSnapshot?.Seed is { } seed) state.Controller.Copy(seed); },
                "seed.world.copy", Current.WorldSnapshot?.Seed is not null, Size: 18f),
        ]).Spacing(4f).VAlign(VerticalAlignment.Center).Margin(new Thickness(0f, 0f, 12f, 0f))
            .Visible(Current.WorldSource && !Current.SearchMode).Cell(0, 0),
        Text(Feedback).Style(LauncherStyles.Text.FontSize(10f).MaxLines(1).Trimming(TextTrimming.CharacterEllipsis)).Foreground(LauncherTheme.Muted).VAlign(VerticalAlignment.Center)
            .TooltipWhenTrimmed().Automation(new() { Name = Feedback, AutomationId = "seed.marker.status" }).Cell(1, 0),
        Text(ViewportText).Style(LauncherStyles.Text.FontSize(10f).MaxLines(1).Trimming(TextTrimming.CharacterEllipsis)).Foreground(LauncherTheme.Muted).MaxWidth(230f).VAlign(VerticalAlignment.Center).Margin(new Thickness(12f, 0f, 0f, 0f))
            .Automation(new() { Name = ViewportText, AutomationId = "seed.viewport" }).Visible(Current.HasMap).Cell(2, 0),
    ]).Height(18f)).Padding(12f, 3f).Background(LauncherTheme.Background).BorderBrush(LauncherTheme.Edge).BorderThickness(0, 1f, 0, 0);
}

/// <summary>Overlays a readable, ticked world-distance scale without changing map geometry.</summary>
internal sealed partial class SeedMapScale(SeedExplorerState state) : Control
{
    private SeedMapScaleMetrics Scale => SeedMapPlanning.Scale(state.Current.BlocksPerPixel);
    private string Label => string.Format(Resources.SeedScaleFormat, Scale.Blocks.ToString("N0"));
    private Ui Body => Border(Overlay([
        new LauncherMaterialSurface(LauncherMaterials.Acrylic),
        Column([
            Text(Label).FontSize(10f).Foreground(LauncherTheme.Ink).HAlign(HorizontalAlignment.Center)
                .Automation(new() { Name = Label, AutomationId = "seed.scale.label" }),
            Overlay([
                Rectangle(LauncherTheme.Ink).Height(1f).VAlign(VerticalAlignment.Bottom),
                Rectangle(LauncherTheme.Ink).Width(1f).HAlign(HorizontalAlignment.Left),
                Rectangle(LauncherTheme.Ink).Width(1f).HAlign(HorizontalAlignment.Right),
            ]).Width(Scale.Pixels).Height(5f).HAlign(HorizontalAlignment.Center),
        ]).Spacing(4f).Padding(6f),
    ])).BorderBrush(LauncherTheme.Edge).BorderThickness(1f)
        .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Bottom).Margin(12f);
}