// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

internal static class SeedChrome
{
    internal static SeedChromeAppearance For(LauncherAppearance theme) => new(theme);
    internal const float FieldHeight = SeedChromeAppearance.FieldHeight;
    internal static ButtonStyle Quiet => SeedChrome.For(LauncherAppearance.Capture()).Quiet;
    internal static ButtonStyle ListRow => SeedChrome.For(LauncherAppearance.Capture()).ListRow;
    internal static ButtonStyle Icon => SeedChrome.For(LauncherAppearance.Capture()).Icon;
    internal static ButtonStyle MapTool => SeedChrome.For(LauncherAppearance.Capture()).MapTool;
    internal static DropDownButtonStyle DropDown => SeedChrome.For(LauncherAppearance.Capture()).DropDown;
    internal static DropDownButtonStyle IconDropDown => SeedChrome.For(LauncherAppearance.Capture()).IconDropDown;

    internal static ButtonStyle Inventory(bool selected) => SeedChrome.For(LauncherAppearance.Capture()).Inventory(selected);
    internal static ButtonStyle ScenePin(bool selected) => SeedChrome.For(LauncherAppearance.Capture()).ScenePin(selected);
    internal static ButtonStyle Pin(bool selected) => SeedChrome.For(LauncherAppearance.Capture()).Pin(selected);
    internal static ButtonStyle OriginPin(bool selected) => SeedChrome.For(LauncherAppearance.Capture()).OriginPin(selected);
}
[View]
internal readonly partial record struct SeedCommand(LauncherAppearance Theme, string Label, Action Action, string Id, bool Enabled = true, bool Primary = false)
{
    /// <summary>Displays one localized action using the launcher's shared command surfaces.</summary>
    public Ui Body => LauncherControls.Command(Theme, Label, Action).Style(Primary ? Theme.Buttons.DialogPrimary : Theme.Buttons.DialogSecondary)
        .MinHeight(SeedChrome.FieldHeight).Enabled(Enabled).Automation(new() { Name = Label, AutomationId = Id });
}

/// <summary>Provides a compact semantic icon action using the shared native button palettes.</summary>
/// <param name="Label">The localized accessible name and tooltip.</param>
/// <param name="Icon">The line icon used to identify this action.</param>
/// <param name="Action">The action to invoke.</param>
/// <param name="Id">The stable automation identity.</param>
/// <param name="Enabled">Whether the action is currently available.</param>
/// <param name="Selected">Whether the action represents an active choice.</param>
/// <param name="Critical">Whether the action removes data or a condition and uses the shared critical palette.</param>
/// <param name="Size">The square hit area, enlarged for navigation at the caption edge.</param>
/// <param name="Surface">An optional contextual surface, such as a translucent hover on the map's acrylic toolbar.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct SeedIconCommand(LauncherAppearance Theme, string Label, string Icon, Action Action, string Id, bool Enabled = true, bool Selected = false, bool Critical = false, float Size = 32f, ButtonStyle? Surface = null)
{
    /// <summary>Provides a named, keyboard-focusable compact action with a purpose tooltip.</summary>
    public Ui Body => Button(Row([LauncherIcons.Line(Icon, 16f)]), Action).Style(Critical ? Theme.Buttons.CriticalIcon : Selected ? SeedChrome.For(Theme).Icon.Palette(Theme.ButtonPalette.Create(false, true)) : Surface ?? SeedChrome.For(Theme).Icon).Width(Size).Height(Size).Enabled(Enabled)
        .ToolTip(Label).Automation(new() { Name = Label, AutomationId = Id });
}

[View]
internal readonly partial record struct SeedDropDownIconContent(string Icon)
{
    /// <summary>Provides concrete native dropdown content so the trigger retains its anchor and toggle behavior.</summary>
    public Ui Body => Row([LauncherIcons.Line(Icon, 16f)]);
}

[View]
internal readonly partial record struct SeedSegments(LauncherAppearance Theme, string[] Labels, int Index, Action<int> Choose, string Id, float Width = 0f, bool[]? Availability = null, float Height = 32f)
{
    /// <summary>Uses the shared recessed native selection surface, filling available width unless an explicit width is supplied.</summary>
    public Ui Body => Column([LauncherControls.Segmented(new SeedSegmentFilter(Labels, Index, Choose, Id, Width, Availability, Height), Theme.Materials.SegmentedMaterial)]);
}

internal sealed partial class SeedSegmentFilter : Control
{
    internal SeedSegmentFilter(string[] labels, int index, Action<int> choose, string id, float width, bool[]? availability, float height)
    { Labels = labels; Index = index; Choose = choose; Id = id; SegmentWidth = width; Availability = availability; SegmentHeight = height; }
    private partial bool[]? Availability { get; set; }
    private partial string[] Labels { get; set; }
    private partial int Index { get; set; }
    private partial Action<int> Choose { get; set; }
    private partial string Id { get; set; }
    private partial float SegmentWidth { get; set; }
    private partial float SegmentHeight { get; set; }
    private SegmentedItem[] Items => [.. Labels.Select((label, index) => new SegmentedItem(label) { IsEnabled = Availability is null || (index < Availability.Length && Availability[index]), Automation = new() { AutomationId = Id + "." + index } })];
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is SeedSegmentFilter other) { Labels = other.Labels; Index = other.Index; Choose = other.Choose; Id = other.Id; SegmentWidth = other.SegmentWidth; Availability = other.Availability; SegmentHeight = other.SegmentHeight; }
    }
    private Ui Body => Segmented(Items).SelectedIndex(Index).OnSelectionChanged(index => Choose(index)).Style(LauncherStyles.Segmented).MinHeight(SegmentHeight).Width(SegmentWidth)
        .Automation(new() { AutomationId = Id });
}

[View]
internal readonly partial record struct SeedChoice(LauncherAppearance Theme, string Label, string[] Items, int Index, Action<int> Choose, string Id, bool Enabled = true, bool[]? ItemEnabled = null, float Height = SeedChrome.FieldHeight)
{
    /// <summary>Retains native selection semantics, translated labels and the real original registry-backed choices.</summary>
    public Ui Body => LauncherControls.Choice(Theme, Items, ItemEnabled).SelectedIndex(Index).OnSelectionChanged(Choose).Enabled(Enabled)
        .FaceplateStyle(Height < SeedChrome.FieldHeight
            ? LauncherMenus.ChoiceFace.Padding(new Thickness(9f, 4f, 0f, 4f)).LabelStyle(LauncherStyles.Text.FontSize(12f))
            : LauncherMenus.ChoiceFace)
        .ItemHeight(36f).MinHeight(Height)
        .Automation(new() { Name = Label, AutomationId = Id });
}

[View]
internal readonly partial record struct SeedNumber(string Label, int Value, int Minimum, int Maximum, Action<int> Change, string Id, bool Enabled = true, float Height = SeedChrome.FieldHeight)
{
    /// <summary>Hosts one retained row/editor with stable native control identity.</summary>
    public Ui Body => new SeedNumberControl(Label, Value, Minimum, Maximum, Change, Id, Enabled, Height);
}

internal sealed partial class SeedNumberControl : Control
{
    internal SeedNumberControl(string label, int value, int minimum, int maximum, Action<int> change, string id, bool enabled, float height) { Label = label; Value = value; Minimum = minimum; Maximum = maximum; Change = change; Id = id; Enabled = enabled; FieldHeight = height; }
    private partial string Label { get; set; }
    private partial int Value { get; set; }
    private partial int Minimum { get; set; }
    private partial int Maximum { get; set; }
    private partial Action<int> Change { get; set; }
    private partial string Id { get; set; }
    private partial bool Enabled { get; set; }
    private partial float FieldHeight { get; set; }
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) { if (source is SeedNumberControl other) { Label = other.Label; Value = other.Value; Minimum = other.Minimum; Maximum = other.Maximum; Change = other.Change; Id = other.Id; Enabled = other.Enabled; FieldHeight = other.FieldHeight; } }

    /// <summary>Edits a bounded integer using the shared native number editor and its validation.</summary>
    public Ui Body => new LauncherNumberBox().Value(Value).Minimum(Minimum).Maximum(Maximum).SmallChange(1).LargeChange(16)
        .SpinButtonPlacement(NumberBoxSpinButtonPlacementMode.Compact).Style(LauncherStyles.NumberInput)
        .OnValueChanged(args => { if (double.IsFinite(args.NewValue)) Change((int)args.NewValue); })
        .Enabled(Enabled).MinWidth(0f).MinHeight(FieldHeight).Automation(new() { Name = Label, AutomationId = Id });
}