// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Popups;
using Sprout.Reconcile;

namespace Rodel.MinecraftLauncher.App.Controls.Buttons;

/// <summary>Applies the launcher command cascade and shared native selector appearance.</summary>
internal static class LauncherControls
{
    /// <summary>
    /// Creates a native selector with the shared face, editable input, menu and row appearance.
    /// Callers retain selection, filtering, validation and layout; compact roles can override those sizes.
    /// </summary>
    /// <param name="items">The current display choices, retained by Sprout's native item source.</param>
    /// <param name="enabled">Optional per-item availability; disabled rows remain discoverable but cannot be selected.</param>
    /// <returns>A selector ready for its real value, callbacks, accessibility and layout modifiers.</returns>
    public static ComboBox Choice(IEnumerable<string> items, IReadOnlyList<bool>? enabled = null) => Choice(LauncherAppearance.Capture(), items, enabled);

    /// <summary>Creates a selector from an immutable appearance retained by its owning view.</summary>
    public static ComboBox Choice(LauncherAppearance theme, IEnumerable<string> items, IReadOnlyList<bool>? enabled = null) => StyleChoice(theme, CreateChoice(items, enabled));

    /// <summary>Applies the same native selector surface to typed rows, including disabled group headings.</summary>
    /// <param name="source">The caller-owned item projections and selection availability.</param>
    /// <returns>A selector with the launcher's shared face, editor and menu appearance.</returns>
    public static ComboBox Choice(ComboBoxItemSource source) => Choice(LauncherAppearance.Capture(), source);

    /// <summary>Styles typed native rows using the owning view's immutable appearance.</summary>
    public static ComboBox Choice(LauncherAppearance theme, ComboBoxItemSource source) => StyleChoice(theme, new(source));

    private static ComboBox StyleChoice(LauncherAppearance theme, ComboBox choice) => choice
        .Palette(theme.Menus.Choice)
        .FaceplateStyle(Theming.LauncherMenus.ChoiceFace)
        .SurfacePainter(Theming.LauncherMenus.ChoiceSurface)
        .EditorStyle(theme.Menus.ChoiceEditor)
        .DropDownStyle(theme.Menus.ChoiceMenu)
        .DropDownPlacement(PopupPlacement.BottomEdgeAlignedLeft)
        .ItemHeight(40f);

    private static ComboBox CreateChoice(IEnumerable<string> items, IReadOnlyList<bool>? enabled)
    {
        if (enabled is null) return Sprout.Markup.ComboBox(items);
        var values = items.ToArray();
        return new(new ComboBoxItemSource(values.Length, index => values[index],
            index => Sprout.Markup.Text(values[index]).Style(Theming.LauncherStyles.Text)
                .Trim(TextTrimming.CharacterEllipsis).MaxLines(1) with
            { InheritsForeground = true },
            enabledAt: index => index < enabled.Count && enabled[index]));
    }

    /// <summary>Creates a native tool toggle with the shared command bevel.</summary>
    public static LauncherToolToggleView ToolToggle(
        [ContentChild] IUiElement? _,
        bool selected,
        Action<bool?> changed,
        bool enabled,
        ToggleButtonPalette palette,
        Sprout.Styling.ToggleButtonStyle style) => ToolToggle(LauncherAppearance.Capture(), _, selected, changed, enabled, palette, style);

    /// <summary>Retains both toggle input and bevel appearance from the owning reactive view.</summary>
    public static LauncherToolToggleView ToolToggle(
        LauncherAppearance theme,
        [ContentChild] IUiElement? _,
        bool selected,
        Action<bool?> changed,
        bool enabled,
        ToggleButtonPalette palette,
        Sprout.Styling.ToggleButtonStyle style)
    {
        var bevel = theme.ButtonPalette.Create(false, selected);
        if (!selected && !theme.HighContrast)
        {
            bevel = bevel with
            {
                Normal = bevel.Normal with
                {
                    Background = theme.ButtonPalette.Quiet.Normal.Background,
                    Border = bevel.PointerOver.Border.Resolve(theme.Palette).MultiplyAlpha(0f),
                },
                Disabled = theme.ButtonPalette.Quiet.Disabled,
            };
        }

        var chrome = theme.Buttons.Button.Palette(bevel).Padding(0f).BorderThickness(selected ? 1f : 0f);
        return new(Sprout.Markup.ToggleButton(string.Empty, selected, palette).OnToggled(changed).Enabled(enabled).Style(style.CornerRadius(0f)), chrome);
    }

    /// <summary>Creates the material decorator whose child is projected by the Sprout markup compiler.</summary>
    /// <param name = "_">The compiler-projected child; the decorator factory only stores its material.</param>
    /// <param name = "material">The current themed track and selection surfaces.</param>
    /// <returns>The segmented decorator retaining its normal child-projection contract.</returns>
    public static LauncherSegmentedView Segmented([ContentChild] IUiElement? _, LauncherSegmentedMaterial material) => new(material);

    /// <summary>Groups compiler-projected commands into one remembered Tab stop with directional navigation.</summary>
    /// <param name="_">Related controls projected directly by the Sprout markup compiler.</param>
    /// <returns>A layout-neutral keyboard group that preserves the child controls and callbacks.</returns>
    public static LauncherKeyboardGroup KeyboardGroup([ContentChild] IUiElement? _) => new();

    /// <summary>Creates a text command using the current reactive owner appearance.</summary>
    public static ButtonView OutlineCommand(string label, Action? action = null) => OutlineCommand(LauncherAppearance.Capture(), label, action);

    /// <summary>Creates a text command retaining its owning view's appearance for reconciliation.</summary>
    public static ButtonView OutlineCommand(LauncherAppearance theme, string label, Action? action = null) => Sprout.Markup.Button(label, action ?? (static () =>
        {
        })).Style(theme.Buttons.OutlineButton).Enabled(action is not null);

    /// <summary>Creates a projected content command using the current reactive owner appearance.</summary>
    public static ButtonView OutlineCommand([ContentChild] IUiElement? content, Action? action = null) => OutlineCommand(LauncherAppearance.Capture(), content, action);

    /// <summary>Creates projected command content with the owning view's retained appearance.</summary>
    public static ButtonView OutlineCommand(LauncherAppearance theme, [ContentChild] IUiElement? content, Action? action = null) => Sprout.Markup.Button(content, action ?? (static () =>
        {
        })).Style(theme.Buttons.OutlineButton).Enabled(action is not null);

    /// <summary>Creates a beveled text command from the current reactive owner appearance.</summary>
    public static LauncherButtonView Command(string label, Action? action = null) => Command(LauncherAppearance.Capture(), label, action);

    /// <summary>Creates a beveled text command retaining the owning view's immutable appearance.</summary>
    public static LauncherButtonView Command(LauncherAppearance theme, string label, Action? action = null) => new(0, Sprout.Markup.Button(label, action ?? (static () =>
            {
            })).Style(theme.Buttons.Button).Enabled(action is not null));

    /// <summary>Creates a projected bevel command using the current reactive owner appearance.</summary>
    public static LauncherButtonView Command([ContentChild] IUiElement? content, Action? action = null) => Command(LauncherAppearance.Capture(), content, action);

    /// <summary>Creates projected bevel content retaining the owning view's immutable appearance.</summary>
    public static LauncherButtonView Command(LauncherAppearance theme, [ContentChild] IUiElement? content, Action? action = null) => new(0, Sprout.Markup.Button(content, action ?? (static () =>
            {
            })).Style(theme.Buttons.Button).Enabled(action is not null));
}