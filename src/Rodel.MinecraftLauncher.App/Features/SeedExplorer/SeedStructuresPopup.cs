// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Finds current-seed structures in an anchored, bounded list; selecting a witness uses the existing location popup.</summary>
internal sealed partial class SeedStructuresPopup : Control
{
    private readonly SeedExplorerState _state;
    private readonly Sprout.Controls.ScrollView _list;
    internal SeedStructuresPopup(SeedExplorerState state) { _state = state; _list = new(new SeedStructureRows(state)); }
    private SeedExplorerSnapshot Current => _state.Current;
    private SeedTarget[] Choices => SeedText.Targets(Current, "structure");
    private bool Ready => Current.Catalog is not null && (Current.UseSavedMap || Current.Seed.Length > 0) && !Current.Opening;
    private string Area => string.Format(Resources.SeedStructureQueryAreaFormat, Current.StructureResult?.CenterX ?? (int)Math.Round(Current.CenterX),
        Current.StructureResult?.CenterZ ?? (int)Math.Round(Current.CenterZ), Current.StructureResult?.Radius ?? Current.StructureRadius);
    private string Summary => Current.StructureSearching ? Resources.SeedStructureQueryRunning : Current.StructurePhase switch
    {
        "stopped" => Resources.SeedStructureQueryStopped,
        "failed" => Current.StructureError,
        _ => Current.StructureResult is not { } result ? Resources.SeedStructureQueryReady : result.Found == 0 ? Resources.SeedStructureQueryNone
            : result.Found > result.Positions.Length ? string.Format(Resources.SeedStructureQueryLimitFormat, result.Positions.Length, result.Found)
            : string.Format(Resources.SeedStructureQueryResultsFormat, result.Found),
    };
    private Ui Body => Column([
        Column([
        Text(Resources.SeedFindStructures).FontSize(16f),
        Text(Resources.SeedSavedStructureScope).FontSize(10f).Foreground(LauncherTheme.Muted).Wrap().Visible(Current.UseSavedMap),
        new SeedChoice(LauncherAppearance.Capture(),Resources.SeedStructure, [.. Choices.Select(choice => choice.Name)], Array.FindIndex(Choices, choice => SeedText.SameTargets(choice.Targets, Current.StructureTargets)),
            index => { var choices = Choices; if (index >= 0 && index < choices.Length) _state.Controller.Structures.Choose(choices[index].Targets); }, "seed.structures.type", Ready),
        Row([Text(Resources.SeedRadius).FontSize(12f).Weight(1f).VAlign(VerticalAlignment.Center),
            new SeedNumber(Resources.SeedRadius, Current.StructureRadius, 0, 8192, _state.Controller.Structures.Radius, "seed.structures.radius", Ready).Width(110f)]).Spacing(8f),
        Row([new SeedCommand(LauncherAppearance.Capture(),Resources.SeedFindStructures, () => _ = _state.Controller.Structures.FindAsync(), "seed.structures.find", Ready && !Current.StructureSearching && Choices.Length > 0, true).Weight(1f),
            new SeedCommand(LauncherAppearance.Capture(),Resources.SeedStructureQueryStop, () => _ = _state.Controller.Structures.StopAsync(), "seed.structures.stop").Visible(Current.StructureSearching)]).Spacing(8f),
        Text(Area).FontSize(10f).Foreground(LauncherTheme.Muted).Wrap().FlowDirection(FlowDirection.LeftToRight)
            .Automation(new() { Name = Area, AutomationId = "seed.structures.area" }),
        Text(Summary).FontSize(11f).Foreground(Current.StructurePhase == "failed" ? LauncherTheme.Error : LauncherTheme.Muted).Wrap()
            .Automation(new() { Name = Summary, AutomationId = "seed.structures.status" }),
        ]).Spacing(8f).Margin(new Thickness(12f, 12f, 12f, Current.StructureResult is { Positions.Length: > 0 } ? 8f : 12f)),
        _list.Padding(new Thickness(12f, 0f, 12f, 12f)).MaxHeight(Math.Max(110, Math.Min(280, Current.Height - 320)) + 12f)
            .Visible(Current.StructureResult is { Positions.Length: > 0 }),
    ]).Width(Math.Min(384f, Current.MapWidth - 8f));
}

internal sealed partial class SeedStructureRows(SeedExplorerState state) : Control
{
    private Ui Body => Column([ItemsRepeater(state.Current.StructureResult?.Positions ?? [], position => state.Controller.Structures.Key(position),
        position => new SeedStructureRowView(position, state.Current, () => state.Controller.Structures.Select(position))).KeyboardNavigation()]);
}

internal sealed partial class SeedStructureRow : Control
{
    internal SeedStructureRow(SeedPosition position, SeedExplorerSnapshot state, Action select) { Position = position; State = state; Select = select; }
    private partial SeedPosition Position { get; set; }
    private partial SeedExplorerSnapshot State { get; set; }
    private partial Action Select { get; set; }
    private string Distance => string.Format(Resources.SeedStructureDistanceFormat, Math.Sqrt(Math.Pow((double)Position.X - (State.StructureResult?.CenterX ?? 0), 2)
        + Math.Pow((double)Position.Z - (State.StructureResult?.CenterZ ?? 0), 2)));
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) { if (source is SeedStructureRow other) { Position = other.Position; State = other.State; Select = other.Select; } }
    /// <summary>Displays a native assembled witness with the shared pin visual, coordinates and distance from the frozen query center.</summary>
    public Ui Body => Button(Row([
        new SeedVisual(SeedAppearance.For(Position), SeedAppearance.Symbols, 32f),
        Column([Text(SeedText.Position(Position, State)).FontSize(12f).Wrap(),
            Text(SeedText.Coordinates(Position)).FontSize(10f).Foreground(LauncherTheme.Muted).FlowDirection(FlowDirection.LeftToRight),
            Text(Distance).FontSize(10f).Foreground(LauncherTheme.Muted)]).Spacing(2f).Weight(1f),
    ]).Spacing(10f), Select).Style(SeedChrome.ListRow).MinHeight(56f)
        .Automation(new() { Name = SeedText.Position(Position, State), AutomationId = $"seed.structures.result.{Position.Id}.{Position.X}.{Position.Z}" });
}

[View]
internal readonly partial record struct SeedStructureRowView(SeedPosition Position, SeedExplorerSnapshot State, Action Select)
{
    /// <summary>Retains the structure result row and its real witness identity.</summary>
    public Ui Body => new SeedStructureRow(Position, State, Select);
}