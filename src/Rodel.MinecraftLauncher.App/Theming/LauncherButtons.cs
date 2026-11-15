// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Theming;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Theming;

/// <summary>Command geometry and role-specific button styles composed from the shared interaction palette.</summary>
internal static class LauncherButtons
{
    /// <summary>Registers only stable command geometry and typography as the immutable application default.</summary>
    internal static ButtonStyle Geometry => LauncherButtonStyles.Geometry;
    internal static ButtonStyle Button => LauncherAppearance.Capture().Buttons.Button;
    /// <summary>Retains the normal command geometry while marking an error-inspection action with error surfaces and ink.</summary>
    internal static ButtonStyle ErrorButton => LauncherAppearance.Capture().Buttons.ErrorButton;

    /// <summary>Gets navigation colors and geometry; render with LauncherControls.Command and AsSelected to retain the shared inset surface.</summary>
    /// <param name = "selected">Whether the navigation command represents the current selection.</param>
    /// <returns>Appearance for the shared bevel command; not a standalone flat selected-button skin.</returns>
    internal static ButtonStyle Navigation(bool selected) => LauncherAppearance.Capture().Buttons.Navigation(selected);
    /// <summary>Gives flat AI actions distinct hover/press fills even when the surrounding surface shares the control background hue.</summary>
    internal static ButtonStyle AiTextAction => LauncherAppearance.Capture().Buttons.AiTextAction;
    internal static ButtonStyle InstanceTitle => LauncherAppearance.Capture().Buttons.InstanceTitle;

    internal static ButtonStyle InstanceRow(bool selected) => LauncherAppearance.Capture().Buttons.InstanceRow(selected);
    internal static ButtonStyle Accent => LauncherAppearance.Capture().Buttons.Accent;
    internal static ButtonStyle CriticalIcon => LauncherAppearance.Capture().Buttons.CriticalIcon;
    internal static ButtonStyle DialogPrimary => LauncherAppearance.Capture().Buttons.DialogPrimary;
    internal static ButtonStyle DialogSecondary => LauncherAppearance.Capture().Buttons.DialogSecondary;
    internal static ButtonStyle DialogHeaderButton => LauncherAppearance.Capture().Buttons.DialogHeaderButton;
    /// <summary>Centers file and folder picker icons without text padding or a bevel decorator.</summary>
    internal static ButtonStyle BrowseButton => LauncherAppearance.Capture().Buttons.BrowseButton;
    /// <summary>Provides a transparent, borderless and centered surface for secondary icon actions, retaining keyboard focus cues.</summary>
    internal static ButtonStyle FlatIconButton => LauncherAppearance.Capture().Buttons.FlatIconButton;
    /// <summary>Uses the shared flat icon surface for popup header close actions.</summary>
    internal static ButtonStyle PopupCloseButton => LauncherAppearance.Capture().Buttons.PopupCloseButton;
    internal static ButtonStyle Primary => LauncherAppearance.Capture().Buttons.Primary;
    internal static ButtonStyle Disclosure => LauncherAppearance.Capture().Buttons.Disclosure;
    internal static ButtonStyle OutlineButton => LauncherAppearance.Capture().Buttons.OutlineButton;
    internal static ButtonStyle IconButton => LauncherAppearance.Capture().Buttons.IconButton;
}