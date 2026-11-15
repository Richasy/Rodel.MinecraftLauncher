// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Shares the naturally sized swatch grid between the inspector and palette manager, without an inner scroller.</summary>
/// <param name = "Colors">Ordered canonical RGBA values.</param>
/// <param name = "Foreground">Current painting color when no explicit swatch index is selected.</param>
/// <param name = "Selected">Manager selection, or -1 to compare against the foreground.</param>
/// <param name = "Width">Available width for eight equally sized columns.</param>
/// <param name = "CanAdd">Whether the current foreground can be added.</param>
/// <param name = "AddLabel">Localized purpose of the final add tile.</param>
/// <param name = "IdPrefix">Stable automation namespace for the hosting grid.</param>
/// <param name = "Choose">Selects a color without editing the skin.</param>
/// <param name = "Add">Adds the foreground, or starts a built-in copy.</param>
/// <param name = "Picker">Returns a retained picker for each editable manager swatch; inspector and built-in swatches only select.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct SkinPaletteSwatches(
    LauncherAppearance Theme,
    string[] Colors,
    string Foreground,
    int Selected,
    float Width,
    bool CanAdd,
    string AddLabel,
    string IdPrefix,
    Action<int> Choose,
    Action Add,
    Func<int, IFlyout>? Picker = null)
{
    internal SkinPaletteTile[] Tiles
    {
        get
        {
            var tiles = new SkinPaletteTile[Colors.Length + 1];
            for (var index = 0; index < Colors.Length; index++)
            {
                tiles[index] = new(
Theme, index,
                    Colors[index],
                    Selected >= 0 ? Selected == index : Foreground.Equals(Colors[index], StringComparison.OrdinalIgnoreCase),
                    CanAdd,
                    AddLabel,
                    IdPrefix,
                    Choose,
                    Add,
                    Picker?.Invoke(index));
            }

            tiles[^1] = new(
Theme, Colors.Length,
                string.Empty,
                false,
                CanAdd,
                AddLabel,
                IdPrefix,
                Choose,
                Add);
            return tiles;
        }
    }

    /// <summary>Lets the containing inspector or dialog own all vertical scrolling.</summary>
    public Ui Body => ItemsRepeater(Tiles, static tile => tile.Index, static tile => tile, new UniformGridLayout
    {
        ItemWidth = Math.Max(16f, (Width - 35f) / 8f),
        MinItemHeight = 24f,
        ColumnSpacing = 5f,
        RowSpacing = 5f,
    }).KeyboardNavigation();
}

[View]
internal readonly partial record struct SkinPaletteTile(
    LauncherAppearance Theme,
    int Index,
    string Value,
    bool Selected,
    bool CanAdd,
    string AddLabel,
    string IdPrefix,
    Action<int> Choose,
    Action Add,
    IFlyout? Picker = null)
{
    /// <summary>Uses direct picker activation only for editable manager colors.</summary>
    public Ui Body => When(Picker is not null, new SkinPalettePickerTile(Theme, Value, Selected, IdPrefix + Index, Picker!), new SkinPalettePlainTile(
Theme, Index,
            Value,
            Selected,
            CanAdd,
            AddLabel,
            IdPrefix,
            Choose,
            Add));
}

[View]
internal readonly partial record struct SkinPalettePlainTile(
    LauncherAppearance Theme,
    int Index,
    string Value,
    bool Selected,
    bool CanAdd,
    string AddLabel,
    string IdPrefix,
    Action<int> Choose,
    Action Add)
{
    /// <summary>Mounts only the actual swatch or add command, keeping hidden duplicate actions out of UI Automation.</summary>
    public Ui Body => When(Value.Length > 0, new SkinPaletteSelectTile(
Theme, Index,
            Value,
            Selected,
            IdPrefix,
            Choose), new SkinPaletteAddTile(Theme, CanAdd, AddLabel, IdPrefix, Add));
}

[View]
internal readonly partial record struct SkinPaletteSelectTile(
    LauncherAppearance Theme,
    int Index,
    string Value,
    bool Selected,
    string IdPrefix,
    Action<int> Choose)
{
    internal Action<bool?> Toggled
    {
        get
        {
            var choose = Choose;
            var index = Index;
            return _ => choose(index);
        }
    }

    /// <summary>Selects the real foreground value without opening a picker for inspector or built-in colors.</summary>
    public Ui Body => Row([
        ToggleButton(string.Empty, Selected, SkinEditorChrome.For(Theme).SwatchPalette(Value))
            .OnToggled(Toggled)
            .Style(SkinEditorChrome.ToolStyle.BorderThickness(Selected ? 2f : 1f))
            .Height(24f)
            .Weight(1f)
            .Automation(new()
            {
                Name = Value,
                AutomationId = IdPrefix + Index
            }),
    ]);
}

[View]
internal readonly partial record struct SkinPaletteAddTile(
    LauncherAppearance Theme,
    bool CanAdd,
    string AddLabel,
    string IdPrefix,
    Action Add)
{
    /// <summary>Keeps the final add tile's complete outline within its measured bounds.</summary>
    public Ui Body => Row([
        LauncherControls.OutlineCommand(
Theme,            Row([
                LauncherIcons.Line("skin-add", 15f)
            ]),
            Add)
            .Style(Theme.Buttons.OutlineButton.Padding(0f)
                .BorderThickness(1f)
                .ContentAlignment(HorizontalAlignment.Center, VerticalAlignment.Center))
            .Height(24f)
            .Weight(1f)
            .Enabled(CanAdd)
            .ToolTip(AddLabel)
            .Automation(new()
            {
                Name = AddLabel,
                AutomationId = IdPrefix + "add"
            }),
    ]);
}

[View]
internal readonly partial record struct SkinPalettePickerTile(
    LauncherAppearance Theme,
    string Value,
    bool Selected,
    string Id,
    IFlyout Picker)
{
    internal Color Color => SkinEditorState.TryParseColor(Value, out var color) ? color : throw new InvalidDataException("The palette swatch color is invalid.");

    /// <summary>Opens the native color picker directly from the swatch, without a second edit action.</summary>
    public Ui Body => Row([
        DropDownButton(new SkinPaletteSwatchFace(Theme,Color, Selected), Picker)
            .Style(SkinEditorChrome.For(Theme).ColorTrigger.Padding(0f)
                .BorderThickness(0f)
                .Button(SkinEditorChrome.For(Theme).QuietIconCommand.BorderThickness(0f)
                    .ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Stretch)))
            .Height(24f)
            .Weight(1f)
            .Automation(new()
            {
                Name = Value,
                AutomationId = Id
            }),
    ]);
}

[View]
internal readonly partial record struct SkinPaletteSwatchFace(LauncherAppearance Theme, Color Value, bool Selected)
{
    /// <summary>Fills the complete tile while retaining the selected outline and real alpha.</summary>
    public Ui Body => Border(Rectangle(Value))
        .BorderBrush(Selected ? Theme.Ink : Theme.Edge)
        .BorderThickness(Selected ? 2f : 1f);
}