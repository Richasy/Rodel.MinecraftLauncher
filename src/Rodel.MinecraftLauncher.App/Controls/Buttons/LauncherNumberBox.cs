// Copyright (c) Richasy. All rights reserved.

using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Popups;
using Sprout.Theming;

namespace Rodel.MinecraftLauncher.App.Controls.Buttons;

/// <summary>Applies shared square input geometry and optionally reports native editor text without waiting for numeric commit.</summary>
internal sealed class LauncherNumberBox(Action<string>? textEdited = null) : NumberBox
{
    /// <summary>Uses the launcher's square acrylic surface while retaining the original focus, stepping and popup geometry.</summary>
    protected override PopupChrome ResolveCompactPopupChrome(Theme theme)
    {
        var appearance = new Theming.LauncherAppearance(theme.Colors, theme.Mode);
        return base.ResolveCompactPopupChrome(theme) with
        {
            Background = appearance.Materials.Acrylic,
            Border = appearance.Edge,
            CornerRadius = CornerRadius.Zero,
        };
    }

    protected override void OnSyncGeometry()
    {
        base.OnSyncGeometry();
        BindTextEdits();
    }

    private void BindTextEdits()
    {
        // NumberBox reapplies its editor callbacks during reconciliation; restore this opt-in callback after geometry sync.
        if (textEdited is not null && Interaction is NumberBoxEditorInteraction { IsEffectivelyEnabled: true } editor)
            editor.Update(true, textEdited);
    }

    protected override IDisposable? OnMounted()
    {
        if (Render is NumberBoxWidget widget)
        {
            widget.TextBox.SetLayoutProps(widget.TextBox.LayoutProps with { MinHeight = Theming.LauncherStyles.InputHeight });
            widget.TextBox.SetControlCornerRadius(CornerRadius.Zero);
            widget.TextBox.SetContentFontSize(Theming.LauncherStyles.InputFontSize);
            widget.TextBox.SetContentPadding(Theming.LauncherStyles.InputPadding);
        }

        if (Interaction is { } interaction)
            interaction.FocusVisualCornerRadius = CornerRadius.Zero;
        BindTextEdits();
        return base.OnMounted();
    }
}