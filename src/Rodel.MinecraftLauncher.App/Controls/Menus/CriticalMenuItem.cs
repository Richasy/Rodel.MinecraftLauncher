// Copyright (c) Richasy. All rights reserved.

using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Reactive;
using Sprout.Semantics;
using Sprout.Theming;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Controls.Menus;

/// <summary>Uses the native menu row and activation with critical ink and live high-contrast pairing.</summary>
internal sealed class CriticalMenuItem(string text, Action action) : MenuFlyoutItemBase, IDisposable
{
    private Effect? _appearance;

    /// <summary>Gets whether this command can be focused and invoked.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <inheritdoc/>
    protected override MenuItemProjection BuildItem(MenuItemBuildContext context)
    {
        _appearance?.Dispose();
        var foreground = IsEnabled ? Brush.Theme(ThemeColorToken.SystemFillColorCritical) : context.Palette.ForegroundDisabled;
        var label = new TextWidget(default, text, context.TextFormat, foreground);
        var row = new MenuItemRowWidget(null, label, null, null);
        if (!IsEnabled)
            return new(row, null, new(context.ItemId, SemanticsRole.MenuItem, text, default) { IsEnabled = false });

        var theme = Theme.Resolve();
        var highlighted = false;
        var pressed = false;
        void RefreshForeground()
        {
            label.SetForeground(theme.Colors.HighContrast is { } colors
                ? highlighted ? colors.SystemColorHighlightTextColor : colors.SystemColorWindowTextColor
                : foreground);
        }

        void Highlight(Brush? background)
        {
            highlighted = background is not null;
            row.SetHighlight(pressed ? context.Palette.PressedBackground ?? background : background);
            RefreshForeground();
        }

        void Activate()
        {
            context.Close();
            action();
        }

        _appearance = new Effect(RefreshForeground);
        var interaction = new MenuItemInteraction(context.ItemId, default, Highlight, context.Palette.HighlightBackground)
        {
            Invoked = Activate,
            PressedChanged = value =>
            {
                pressed = value;
                Highlight(highlighted ? context.Palette.HighlightBackground : null);
            },
        };
        interaction.FocusRequested = () => context.Focus(interaction);
        return new(row, interaction, new(context.ItemId, SemanticsRole.MenuItem, text, default, invoked: Activate), TypeAheadText: text);
    }

    /// <summary>Releases theme observation when the owning menu chain closes.</summary>
    public void Dispose()
    {
        _appearance?.Dispose();
        _appearance = null;
    }
}