// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Composes a scene inspector inventory using the same header, contents and footer as the region flyout.</summary>
internal sealed partial class SeedInventoryView : Control
{
    private SeedExplorerState state;
    internal SeedInventoryView(SeedExplorerState state, SeedContainer container, bool region = false) { this.state = state; Container = container; Region = region; }
    private partial bool Region { get; set; }
    private partial SeedContainer Container { get; set; }
    private SeedExplorerSnapshot Current => state.Current;
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) { if (source is SeedInventoryView other) { state = other.state; Container = other.Container; Region = other.Region; } }
    private Ui Body => Column([
        new SeedInventoryHeader(LauncherAppearance.Capture(),Current, Container, Region, () => state.Current = Current with { RegionContainer = null, RegionItem = null }, () => state.Controller.Regions.CopyContainer(Container)),
        new SeedInventoryContents(state, Container, Region),
        new SeedInventoryFooter(LauncherAppearance.Capture(),() => state.Controller.Regions.LocateContainer(Container)),
    ]).Spacing(8f).Margin(Region ? default : new Thickness(12f, 8f, 12f, 12f));
}

/// <summary>Keeps container navigation outside the bounded region contents viewport.</summary>
internal sealed partial class SeedRegionInventory : Control
{
    private readonly Sprout.Controls.ScrollView _scroll;
    internal SeedRegionInventory(SeedExplorerState state)
    {
        State = state;
        _scroll = new(new SeedRegionInventoryContent(this));
        _scroll.Padding(new Thickness(12f, 0f, 12f, 0f));
    }
    internal SeedExplorerState State { get; private set; }
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) { if (source is SeedRegionInventory other) State = other.State; }
    private SeedExplorerSnapshot Current => State.Current;
    private SeedContainer Container => Current.RegionContainer ?? new();
    private Ui Body => Column([
        new SeedInventoryHeader(LauncherAppearance.Capture(),Current, Container, true, () => State.Current = Current with { RegionContainer = null, RegionItem = null }, () => State.Controller.Regions.CopyContainer(Container))
            .Margin(new Thickness(12f, 12f, 12f, 0f)),
        _scroll.MaxHeight(Math.Max(64f, Current.MapHeight - 134f)).Automation(new() { AutomationId = "seed.area.inventory.scroll" }),
        new SeedInventoryFooter(LauncherAppearance.Capture(),() => State.Controller.Regions.LocateContainer(Container)).Margin(new Thickness(12f, 0f, 12f, 12f)),
    ]).Spacing(8f).Width(Math.Min(312f, Current.MapWidth - 16f));
}

internal sealed partial class SeedRegionInventoryContent(SeedRegionInventory owner) : Control
{
    private Ui Body => new SeedInventoryContents(owner.State, owner.State.Current.RegionContainer ?? new(), true);
}

/// <summary>Provides the frozen container title, coordinates and their contextual actions.</summary>
/// <param name="State">The current names and generation context.</param>
/// <param name="Container">The selected frozen container.</param>
/// <param name="Region">Whether this header owns the region's return action and title.</param>
/// <param name="Back">Returns to the region result list.</param>
/// <param name="Copy">Copies this container's original coordinates as a teleport command.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct SeedInventoryHeader(LauncherAppearance Theme, SeedExplorerSnapshot State, SeedContainer Container, bool Region, Action Back, Action Copy)
{
    internal string Position => SeedRegionText.Position(Container);
    /// <summary>Renders one compact context header without a second clipboard feedback message.</summary>
    public Ui Body => Column([
        Column([
            Row([
                new SeedIconCommand(Theme,Resources.SeedBackToList, "back", Back, "seed.area.inventory.back"),
                Text(SeedRegionText.Name(State, Container.Id)).FontSize(15f).Wrap().Weight(1f).VAlign(VerticalAlignment.Center),
                new SeedIconCommand(Theme,Resources.SeedCopyTeleport, "copy", Copy, "seed.inventory.teleport"),
            ]).Spacing(8f).Height(32f),
            Text(Position).FontSize(11f).Foreground(Theme.Muted).FlowDirection(FlowDirection.LeftToRight)
                .Automation(new() { Name = Position, AutomationId = "seed.inventory.coordinates" }),
        ]).Spacing(4f).Visible(Region),
        Row([
            Text(Position).FontSize(11f).Foreground(Theme.Muted).FlowDirection(FlowDirection.LeftToRight).Weight(1f).VAlign(VerticalAlignment.Center)
                .Automation(new() { Name = Position, AutomationId = "seed.inventory.coordinates" }),
            new SeedIconCommand(Theme,Resources.SeedCopyTeleport, "copy", Copy, "seed.inventory.teleport"),
        ]).Spacing(8f).Visible(!Region),
    ]);
}

/// <summary>Provides the sole secondary action below an inventory.</summary>
/// <param name="Locate">Centers the current structure on the container, or reveals its map position when inspecting a region.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct SeedInventoryFooter(LauncherAppearance Theme, Action Locate)
{
    /// <summary>Keeps the contextual locate action aligned with the inventory's right edge.</summary>
    public Ui Body => Button(Resources.SeedLocate, Locate).Style(SeedChrome.For(Theme).Quiet).Height(28f).HAlign(HorizontalAlignment.Right)
        .Automation(new() { Name = Resources.SeedLocate, AutomationId = "seed.inventory.locate" });
}

internal sealed partial class SeedInventoryContents : Control
{
    private SeedExplorerState state;
    internal SeedInventoryContents(SeedExplorerState state, SeedContainer container, bool region = false) { this.state = state; Container = container; Region = region; }
    private partial bool Region { get; set; }
    private SeedLootItem? Selected => Region ? Current.RegionItem : Current.SceneItem;
    private partial SeedContainer Container { get; set; }
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) { if (source is SeedInventoryContents other) { state = other.state; Container = other.Container; Region = other.Region; } }
    private SeedExplorerSnapshot Current => state.Current;
    private string Details => Selected is { } item ? SeedRegionText.Components(Current, item) : string.Empty;
    private string Label => Selected is { } item ? string.Format(Resources.SeedItemCountFormat, SeedRegionText.Name(Current, item.Id, true), item.Count) : Resources.SeedSelectItem;
    private Ui Body => Column([
        Text(Resources.SeedDynamicLoot).FontSize(12f).Wrap().Visible(Container.State == "dynamic_reward"),
        new SeedSavedContainerPrediction(state, Container, Region).Visible(Container.State == "unevaluated" && Current.UseSavedMap && (Region || !Current.ScenePreview)),
        new LauncherNotice(LauncherAppearance.Capture(),Resources.SeedSavedLootIncomplete, Resources.SeedUnevaluatedLoot, LauncherNoticeKind.Information, "seed.inventory.unevaluated")
            .Visible(Container.State == "unevaluated" && (!Current.UseSavedMap || (!Region && Current.ScenePreview))),
        new SeedInventoryGrid(state, Container, Region).Visible(Container.Slots > 0 && Container.State is "generated" or "saved")
            .Automation(new() { AutomationId = "seed.inventory.grid" }),
        Text(Resources.SeedEmptyContainer).FontSize(11f).Foreground(LauncherTheme.Muted).Visible(Container.State is ("generated" or "saved") && Container.Items.Length == 0),
        Text(Resources.SeedSavedSlots).FontSize(10f).Foreground(LauncherTheme.Muted).Wrap().Visible(Container.State == "saved" && Container.Slots > 0),
        Border(Column([Text(Label).FontSize(12f).Wrap().Automation(new() { Name = Label, AutomationId = "seed.inventory.item" }),
            Text(Details).FontSize(11f).Foreground(LauncherTheme.Muted).Wrap().Visible(Details.Length > 0).Automation(new() { Name = Details, AutomationId = "seed.inventory.components" }),
        ]).Spacing(6f)).Padding(10f).Background(LauncherTheme.ControlBackground).Visible(Selected is not null),
    ]).Spacing(8f);
}

internal sealed partial class SeedInventoryGrid : Control
{
    private SeedExplorerState state;
    internal SeedInventoryGrid(SeedExplorerState state, SeedContainer container, bool region) { this.state = state; Container = container; Region = region; }
    private partial SeedContainer Container { get; set; }
    private partial bool Region { get; set; }
    private SeedExplorerSnapshot Current => state.Current;
    private SeedLootItem? Selected => Region ? Current.RegionItem : Current.SceneItem;
    private void Select(SeedLootItem? item) => state.Current = Region ? Current with { RegionItem = item } : Current with { SceneItem = item };
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) { if (source is SeedInventoryGrid other) { state = other.state; Container = other.Container; Region = other.Region; } }
    private SeedInventorySlot[] Slots => [.. Enumerable.Range(0, Container.Slots).Select(index =>
    {
        var item = Container.Items.FirstOrDefault(value => value.Slot == index);
        return new SeedInventorySlot(index, item, item is null ? null : Current.ItemImages.GetValueOrDefault(item.Id), item is not null && item == Selected,
            item is null ? Resources.SeedEmptySlot : string.Format(Resources.SeedItemCountFormat, SeedRegionText.Name(Current, item.Id, true), item.Count));
    })];
    private Ui Body => ItemsRepeater(Slots, slot => slot.Index, slot => new SeedInventorySlotView(LauncherAppearance.Capture(), slot, () => Select(slot.Item)), new SeedInventoryLayout()).KeyboardNavigation();
}

internal sealed record SeedInventorySlot(int Index, SeedLootItem? Item, ImageSource? Image, bool Selected, string Label);
[View]
internal readonly partial record struct SeedInventorySlotView(LauncherAppearance Theme, SeedInventorySlot Slot, Action Select, string Scope = "scene")
{
    /// <summary>Preserves a native slot's position, actual stack count and selectable item details.</summary>
    public Ui Body => Button(Overlay([
        new SeedItemImage(Slot.Image, 20f).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center),
        Border(Text(Slot.Item is { Count: > 1 } ? Slot.Item.Count.ToString() : string.Empty).FontSize(10f).Foreground(Color.FromHex("#ffffff"))).Padding(2f, 0f).Background(Color.FromHex("#000000").MultiplyAlpha(.72f))
            .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Bottom).Visible(Slot.Item is { Count: > 1 }),
    ]), Select).Style(SeedChrome.For(Theme).Inventory(Slot.Selected)).Enabled(Slot.Item is not null).ToolTip(Slot.Label)
        .Automation(new() { Name = Slot.Label, AutomationId = "seed." + Scope + ".slot." + Slot.Index });
}

/// <summary>Keeps actual inventory slots in nine columns regardless of the inspector's width.</summary>
internal sealed class SeedInventoryLayout : VirtualizingLayout
{
    private const int Columns = 9;
    private const float Gap = 2f;
    private float _width;
    private float SlotSize => Math.Max(0f, (_width - ((Columns - 1) * Gap)) / Columns);
    /// <inheritdoc/>
    public override bool ReconcileFrom(VirtualizingLayout source) => source is SeedInventoryLayout;
    /// <inheritdoc/>
    public override Size Measure(VirtualizingLayoutContext context, BoxConstraints constraints)
    {
        _width = float.IsFinite(constraints.Max.Width) ? constraints.Max.Width : 288f;
        var rows = (context.ItemCount + Columns - 1) / Columns;
        return new(_width, rows == 0 ? 0f : (rows * SlotSize) + ((rows - 1) * Gap));
    }
    /// <inheritdoc/>
    public override void Arrange(VirtualizingLayoutContext context, Rect bounds)
    {
        var side = SlotSize;
        var pitch = side + Gap;
        for (var i = 0; i < context.ItemCount; i++)
        {
            var element = context.GetOrCreateElementAt(i);
            var size = new Size(side, side);
            element.Measure(new(size, size), context.LayoutContext);
            element.Arrange(Rect.FromXywh(bounds.Left + (i % Columns * pitch), bounds.Top + (i / Columns * pitch), side, side));
        }
    }
}